using System.IO.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Exceptions;
using Soenneker.Extensions.String;

namespace Grillisoft.Tools.DatabaseDeploy;

/// <summary>
/// Reads the csv branch files of a directory and exposes the branches they describe.
/// Nothing here writes: changing the files is <see cref="BranchesWriter"/>'s job.
/// </summary>
public class BranchesReader
{
    private readonly IDirectoryInfo _directory;
    private readonly GlobalSettings _globalSettings;
    private readonly Func<string, DatabaseHooks>? _hooks;
    private readonly Dictionary<string, Branch> _branches = new(StringComparer.InvariantCultureIgnoreCase);
    private readonly Dictionary<string, IFileInfo[]> _branchFiles = new(StringComparer.InvariantCultureIgnoreCase);
    private Branch? _mainBranch;

    /// <param name="hooks">
    /// Resolves the hook scripts configured for a database. When null no hook is validated, which
    /// is what the callers that only care about the branch layout want.
    /// </param>
    public BranchesReader(IDirectoryInfo directory, GlobalSettings globalSettings, Func<string, DatabaseHooks>? hooks = null)
    {
        _directory = directory;
        _globalSettings = globalSettings;
        _hooks = hooks;
    }

    public IReadOnlyDictionary<string, Branch> Branches => _branches;

    public IDirectoryInfo Directory => _directory;

    public Branch GetBranch(string name)
    {
        if (!_branches.TryGetValue(name, out var branch))
            throw new BranchNotFoundException(name);

        return branch;
    }

    public IEnumerable<Step> GetSteps(Branch branch)
    {
        if (branch.Name.EqualsIgnoreCase(_globalSettings.DefaultBranch) || _mainBranch == null)
            return branch.Steps;

        return _mainBranch.Steps.Concat(branch.Steps);
    }

    /// <summary>
    /// The files the branch was read from: its own, plus the file of every branch it includes.
    /// </summary>
    public IReadOnlyCollection<IFileInfo> GetBranchFiles(Branch branch)
    {
        if (!_branchFiles.TryGetValue(branch.Name, out var files))
            throw new BranchNotFoundException(branch.Name);

        return files;
    }

    public async Task<List<string>> Load()
    {
        _directory.ThrowIfNotFound();

        _mainBranch = await Load(MainBranchFile);
        _branches.Add(_mainBranch.Name, _mainBranch);

        var files = BranchFiles.EnumerateFiles(_directory)
            .Where(f => !f.Name.EqualsIgnoreCase(MainBranchFile.Name))
            .ToArray();

        foreach (var file in files)
        {
            var branch = await Load(file);
            _branches.Add(branch.Name, branch);
        }

        return await BranchesValidator.Validate(_branches.Values, _globalSettings, _directory, _hooks);
    }

    private IFileInfo MainBranchFile => BranchFiles.GetFile(_globalSettings.DefaultBranch, _directory);

    private async Task<Branch> Load(IFileInfo file)
    {
        var files = new Dictionary<string, IFileInfo>(StringComparer.InvariantCultureIgnoreCase);
        var branch = await LoadInternal(file, files);
        _branchFiles.Add(branch.Name, files.Values.ToArray());
        return branch;
    }

    private async Task<Branch> LoadInternal(IFileInfo file, IDictionary<string, IFileInfo> files)
    {
        file.ThrowIfNotFound();
        if (!files.TryAdd(file.Name, file))
            throw new CircularIncludeException(file.Name);

        var branchName = BranchFiles.GetBranchName(file);
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

            var include = BranchFiles.GetIncludedBranch(line);
            if (include != null)
            {
                var includeBranch = await LoadInternal(BranchFiles.GetFile(include, directory), files);
                steps.AddRange(includeBranch.Steps);
                continue;
            }

            steps.Add(ReadStep(line, branchName, count++, directory));
        }

        return new Branch(branchName, steps);
    }

    private Step ReadStep(string line, string branchName, int count, IDirectoryInfo directory)
    {
        var split = line.Split(',');
        if (split.Length != 2)
            throw new ArgumentException($"Invalid format '{line}' on line {count}");

        var database = split[0].Trim();
        var name = split[1].Trim();

        return new Step(
            database,
            name,
            branchName,
            name.EqualsIgnoreCase(_globalSettings.InitStepName),
            directory.SubDirectory(database));
    }
}
