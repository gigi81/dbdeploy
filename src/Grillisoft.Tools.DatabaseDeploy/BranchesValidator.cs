using System.IO.Abstractions;
using System.Text.RegularExpressions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
// ReSharper disable EnforceForeachStatementBraces

namespace Grillisoft.Tools.DatabaseDeploy;

public static class BranchesValidator
{
    public static List<string> Validate(ICollection<Branch> branches, GlobalSettings settings, IDirectoryInfo directory)
    {
        var steps = branches.SelectMany(b => b.Steps).Distinct().ToArray();

        var errors = Array.Empty<string>()
            .Concat(CheckFiles(settings, directory, steps))
            .Concat(CheckForDuplicateSteps(branches))
            .Concat(CheckForInvalidStepNames(settings, steps))
            .ToList();

        return errors;
    }

    private static IEnumerable<string> CheckFiles(GlobalSettings settings, IDirectoryInfo directory, Step[] steps)
    {
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

        foreach (var file in missing)
            yield return $"Could not find mandatory file {file}";

        foreach (var file in untracked)
            yield return $"Untracked file detected {file}";
    }
    
    private static IEnumerable<string> CheckForDuplicateSteps(ICollection<Branch> branches)
    {
        foreach (var branch in branches)
        {
            foreach (var database in branch.Databases)
            {
                var duplicates = branch.Steps
                    .Where(s => s.Database.EqualsIgnoreCase(database))
                    .GroupBy(s => s.Name, StringComparer.InvariantCultureIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .ToArray();
                
                foreach (var duplicate in duplicates)
                    yield return $"The script {duplicate.Key} for database {database} was specified more than once on branch {branch.Name}";
            }
        }
    }
    
    private static IEnumerable<string> CheckForInvalidStepNames(GlobalSettings settings, Step[] steps)
    {
        if (string.IsNullOrWhiteSpace(settings.StepsNameRegex))
            yield break;

        var regex = new Regex(settings.StepsNameRegex);
        foreach (var step in steps.Where(s => !s.IsInit && !regex.IsMatch(s.Name)))
            yield return $"Step {step.Name} for database {step.Database} does not match expected naming convention";
    }
}