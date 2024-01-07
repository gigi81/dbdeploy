using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy;

public sealed class DeployManager
{
    public static DeployManager Load(IDirectoryInfo directory)
    {
        var manager = new DeployManager(directory);
        manager.Load();
        return manager;
    }

    private readonly IDirectoryInfo _directory;
    private readonly Dictionary<string, Branch> _branches = new();

    private DeployManager(IDirectoryInfo directory)
    {
        _directory = directory;
    }

    public IReadOnlyDictionary<string, Branch> Branches => _branches;

    private void Load()
    {
        _directory.ThrowIfNotFound();

        foreach (var branch in BranchLoader.LoadAll(_directory))
            _branches.Add(branch.Name, branch);
    }

    public List<string> Validate()
    {
        var errors = new List<string>();
        var steps = _branches.Values.SelectMany(b => b.Steps).Distinct().ToArray();
            
        var mandatoryFiles = steps.SelectMany(s => s.MandatoryFiles)
            .Select(s => s.FullName)
            .Distinct()
            .ToHashSet();

        var extraFiles = steps.SelectMany(s => s.ExtraFiles)
            .Select(s => s.FullName)
            .Distinct()
            .ToHashSet();

        var found = _directory.EnumerateFiles("*.sql", SearchOption.AllDirectories)
            .Select(s => s.FullName)
            .ToHashSet();
            
        var missing = mandatoryFiles.ExceptIgnoreCase(found).ToArray();
        foreach (var file in missing)
            errors.Add($"Could not find mandatory file {file}");
               
        var untracked = found.ExceptIgnoreCase(mandatoryFiles)
            .ExceptIgnoreCase(extraFiles)
            .ToArray();
        
        errors.AddRange(untracked.Select(file => $"Untracked file detected {file}"));

        return errors;
    }
}