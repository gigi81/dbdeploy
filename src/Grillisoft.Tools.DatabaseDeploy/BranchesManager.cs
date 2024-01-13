using System.IO.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy;

public class BranchesManager
{
    private const string IncludeKeyword = "@include ";
    private const string MainBranchFilename = "main.csv";
    
    private readonly IDirectoryInfo _directory;
    private readonly Dictionary<string, Branch> _branches = new();
    private Branch? _mainBranch;

    public BranchesManager(IDirectoryInfo directory)
    {
        _directory = directory;
    }

    public IReadOnlyDictionary<string, Branch> Branches => _branches;

    public IDirectoryInfo Directory => _directory;

    public async Task<List<string>> Load()
    {
        _directory.ThrowIfNotFound();
        
        _mainBranch = await Load(_directory.File(MainBranchFilename));
        _branches.Add(_mainBranch.Name, _mainBranch);
        
        var files = _directory.EnumerateFiles("*.csv", SearchOption.TopDirectoryOnly)
            .Where(f => !f.Name.Equals(MainBranchFilename, StringComparison.InvariantCultureIgnoreCase))
            .ToArray();

        foreach (var file in files)
        {
            var branch = await Load(file);
            _branches.Add(branch.Name, branch);
        }

        return BranchesValidator.Validate(_branches.Values, _directory);
    }

    private async Task<Branch> Load(IFileInfo file)
    {
        return await LoadInternal(file, new HashSet<string>(StringComparer.InvariantCultureIgnoreCase));
    }

    private static IFileInfo GetBranchFile(string branchName, IDirectoryInfo directory)
    {
        branchName = branchName.Replace('/', '_');
        return directory.File($"{branchName}.csv");
    }

    private async Task<Branch> LoadInternal(IFileInfo file, ISet<string> files)
    {
        file.ThrowIfNotFound();
        if (!files.Add(file.Name))
            throw new Exception($"Circular include detected for file '{file.Name}'");

        var directory = file.Directory ?? file.FileSystem.CurrentDirectory();
        var steps = new List<Step>();
        var count = 1;

        await foreach (var l in file.FileSystem.File.ReadLinesAsync(file.FullName))
        {
            if (string.IsNullOrWhiteSpace(l))
            {
                continue; //empty line
            }

            var line = l.Trim();
            if (line.StartsWith('#'))
            {
                continue; //comment
            }

            if (line.StartsWith(IncludeKeyword))
            {
                var includeFile = GetBranchFile(line.Substring(IncludeKeyword.Length + 1), directory);
                var includeBranch = await LoadInternal(includeFile, files);
                steps.AddRange(includeBranch.Steps);
                continue;
            }

            steps.Add(ReadStep(line, count++, directory));
        }

        if (_mainBranch != null)
            steps = _mainBranch.Steps.Concat(steps).ToList();
        
        return new Branch(GetBranchName(file), steps);
    }

    private static string GetBranchName(IFileInfo file)
    {
        var name = file.Name;
        var index = name.LastIndexOf('.');
        if (index >= 0)
            name = name.Substring(0, index);

        name = name.Replace('_', '/');
        return name;
    }

    private static Step ReadStep(string line, int count, IDirectoryInfo directory)
    {
        var split = line.Split(',');
        if (split.Length != 2)
            throw new ArgumentException($"Invalid format '{line}' on line {count}");

        return new Step(split[0].Trim(), split[1].Trim(), directory.SubDirectory(split[0].Trim()));
    }
}
