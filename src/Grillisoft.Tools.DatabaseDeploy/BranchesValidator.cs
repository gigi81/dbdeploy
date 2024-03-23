using System.IO.Abstractions;
using System.Text.RegularExpressions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy;

public static class BranchesValidator
{
    public static List<string> Validate(IEnumerable<Branch> branches, GlobalSettings settings, IDirectoryInfo directory)
    {
        var errors = new List<string>();
        var steps = branches.SelectMany(b => b.Steps).Distinct().ToArray();

        var deploy = steps.Select(s => s.DeployScript.FullName);
        var rollback = steps.Select(s => s.RollbackScript.FullName).ToArray();
        var test = steps.Select(s => s.TestScript.FullName);
        
        var mandatoryFiles = (settings.RollbackRequired ? deploy.Concat(rollback) : deploy)
            .Distinct()
            .ToHashSet();
            
        var extraFiles = (settings.RollbackRequired ? test : test.Concat(rollback))
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

        if (!string.IsNullOrWhiteSpace(settings.StepsNameRegex))
        {
            var regex = new Regex(settings.StepsNameRegex);
            foreach (var step in steps.Where(s => !regex.IsMatch(s.Name)))
                errors.Add($"Step {step.Name} for database {step.Database} does not match expected naming convention");
        }
        
        return errors;
    }
}