using System.IO.Abstractions;
using System.Text.RegularExpressions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
// ReSharper disable EnforceForeachStatementBraces

namespace Grillisoft.Tools.DatabaseDeploy;

public static class BranchesValidator
{
    public static List<string> Validate(ICollection<Branch> branches, GlobalSettings settings, IDirectoryInfo directory)
    {
        var errors = new List<string>();
        var steps = branches.SelectMany(b => b.Steps).Distinct().ToArray();

        var deploy = steps.Select(s => s.DeployScript.FullName);
        var data = steps.SelectMany(s => s.DataScripts).Select(s => s.FullName);
        var rollback = steps.Where(s => !s.IsInit).Select(s => s.RollbackScript.FullName).ToArray();
        var test = steps.Select(s => s.TestScript.FullName);

        var mandatoryFiles = (settings.RollbackRequired ? deploy.Concat(rollback) : deploy)
            .ToHashSetIgnoreCase();

        var extraFiles = (settings.RollbackRequired ? test : test.Concat(rollback))
            .Concat(data)
            .ToHashSetIgnoreCase();

        var found = directory.EnumerateFiles("*.sql", SearchOption.AllDirectories)
            .Select(s => s.FullName)
            .ToHashSetIgnoreCase();

        var missing = mandatoryFiles.Where(m => !found.Contains(m)).ToArray();
        var untracked = found.Where(f => !mandatoryFiles.Contains(f) && !extraFiles.Contains(f))
            .ToArray();

        foreach (var branch in branches)
        {
            var duplicates = branch.Steps
                .GroupBy(s => s.Name)
                .Where(g => g.Count() > 1)
                .ToArray();
            
            foreach (var duplicate in duplicates)
                errors.Add($"The script {duplicate.Key} was incorrectly specified more than once on branch {branch.Name}");
        }

        foreach (var file in missing)
            errors.Add($"Could not find mandatory file {file}");

        foreach (var file in untracked)
            errors.Add($"Untracked file detected {file}");

        if (!string.IsNullOrWhiteSpace(settings.StepsNameRegex))
        {
            var regex = new Regex(settings.StepsNameRegex);
            foreach (var step in steps.Where(s => !regex.IsMatch(s.Name)))
                errors.Add($"Step {step.Name} for database {step.Database} does not match expected naming convention");
        }

        return errors;
    }
}