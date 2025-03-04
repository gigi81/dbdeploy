﻿using System.IO.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Exceptions;
using Soenneker.Extensions.String;

namespace Grillisoft.Tools.DatabaseDeploy;

public class BranchesManager
{
    private const string IncludeKeyword = "@include ";
    private const string ListFileExtension = "csv";

    private readonly IDirectoryInfo _directory;
    private readonly GlobalSettings _globalSettings;
    private readonly Dictionary<string, Branch> _branches = new(StringComparer.InvariantCultureIgnoreCase);
    private Branch? _mainBranch;

    public BranchesManager(IDirectoryInfo directory, GlobalSettings globalSettings)
    {
        _directory = directory;
        _globalSettings = globalSettings;
    }

    public IReadOnlyDictionary<string, Branch> Branches => _branches;

    public IDirectoryInfo Directory => _directory;

    public IEnumerable<Step> GetSteps(Branch branch)
    {
        if (branch.Name.EqualsIgnoreCase(_globalSettings.DefaultBranch) || _mainBranch == null)
            return branch.Steps;

        return _mainBranch.Steps.Concat(branch.Steps);
    }

    public async Task<List<string>> Load()
    {
        _directory.ThrowIfNotFound();

        _mainBranch = await Load(_directory.File(MainBranchFilename));
        _branches.Add(_mainBranch.Name, _mainBranch);

        var files = _directory.EnumerateFiles($"*.{ListFileExtension}", SearchOption.TopDirectoryOnly)
            .Where(f => !f.Name.EqualsIgnoreCase(MainBranchFilename))
            .ToArray();

        foreach (var file in files)
        {
            var branch = await Load(file);
            _branches.Add(branch.Name, branch);
        }

        return await BranchesValidator.Validate(_branches.Values, _globalSettings, _directory);
    }

    private string MainBranchFilename => $"{_globalSettings.DefaultBranch}.{ListFileExtension}";

    private async Task<Branch> Load(IFileInfo file)
    {
        return await LoadInternal(file, new HashSet<string>(StringComparer.InvariantCultureIgnoreCase));
    }

    private static IFileInfo GetBranchFile(string branchName, IDirectoryInfo directory)
    {
        branchName = branchName.Replace('/', '_');
        return directory.File($"{branchName}.{ListFileExtension}");
    }

    private async Task<Branch> LoadInternal(IFileInfo file, ISet<string> files)
    {
        file.ThrowIfNotFound();
        if (!files.Add(file.Name))
            throw new CircularIncludeException(file.Name);

        var branchName = file.Name.BranchName();
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
                var includeFile = GetBranchFile(line.Substring(IncludeKeyword.Length).Trim(), directory);
                var includeBranch = await LoadInternal(includeFile, files);
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
