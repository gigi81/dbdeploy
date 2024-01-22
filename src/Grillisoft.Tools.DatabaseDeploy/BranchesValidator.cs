using System.IO.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy;

public static class BranchesValidator
{
    public static List<string> Validate(IEnumerable<Branch> branches, IDirectoryInfo directory)
    {
        var errors = new List<string>();
        var steps = branches.SelectMany(b => b.Steps).Distinct().ToArray();
            
        var mandatoryFiles = steps.SelectMany(s => s.MandatoryFiles)
            .Select(s => s.FullName)
            .Distinct()
            .ToHashSet();

        var extraFiles = steps.SelectMany(s => s.ExtraFiles)
            .Select(s => s.FullName)
            .Distinct()
            .ToHashSet();

        var found = directory.EnumerateFiles("*.sql", SearchOption.AllDirectories)
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