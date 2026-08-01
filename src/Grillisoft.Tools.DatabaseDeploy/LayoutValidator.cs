using System.IO.Abstractions;
using System.Text.RegularExpressions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Soenneker.Extensions.Enumerable.String;
using Soenneker.Extensions.String;

// ReSharper disable EnforceForeachStatementBraces

namespace Grillisoft.Tools.DatabaseDeploy;

/// <summary>
/// Checks the layout of a deployment folder against what its branch files declare: the scripts
/// that have to be there, the ones that are there and should not be, how the steps are named, and
/// the hook scripts the databases have configured.
/// </summary>
public static class LayoutValidator
{
    /// <summary>
    /// Everything that can be wrong with what <see cref="BranchesReader"/> read: missing or
    /// untracked files, duplicate or badly named steps, files that are not UTF-8 without BOM, and
    /// the hook scripts the databases have configured.
    /// </summary>
    public static async Task<List<string>> Validate(
        BranchesReader reader,
        GlobalSettings settings,
        Func<string, DatabaseHooks> hooks)
    {
        return await Validate(reader.Branches.Values.ToArray(), settings, reader.Directory, hooks);
    }

    private static async Task<List<string>> Validate(
        ICollection<Branch> branches,
        GlobalSettings settings,
        IDirectoryInfo directory,
        Func<string, DatabaseHooks> hooks)
    {
        var steps = branches.SelectMany(b => b.Steps).Distinct().ToArray();
        var hookScripts = GetHookScripts(steps, directory, hooks);

        var errors = Array.Empty<string>()
            .Concat(CheckFiles(settings, directory, steps, hookScripts))
            .Concat(CheckHookFiles(hookScripts))
            .Concat(CheckForDuplicateSteps(branches))
            .Concat(CheckForInvalidStepNames(settings, steps))
            .Concat(await CheckEncodings(directory))
            .ToList();

        return errors;
    }

    /// <summary>
    /// The hook scripts of every database taking part in the branches. A database with no hook
    /// configured contributes none.
    /// </summary>
    private static HookScript[] GetHookScripts(Step[] steps, IDirectoryInfo directory, Func<string, DatabaseHooks> hooks)
    {
        return steps.Select(s => s.Database)
            .Distinct(StringComparer.InvariantCultureIgnoreCase)
            .SelectMany(database => hooks(database).GetHookScripts(database, directory))
            .ToArray();
    }

    private static IEnumerable<string> CheckHookFiles(HookScript[] hookScripts)
    {
        foreach (var script in hookScripts.Where(s => s.File == null))
            yield return script.NotFoundMessage;
    }

    private static IEnumerable<string> CheckFiles(GlobalSettings settings, IDirectoryInfo directory, Step[] steps, HookScript[] hookScripts)
    {
        var deploy = steps.Select(s => s.DeployScript.FullName);
        var data = steps.SelectMany(s => s.DataScripts).Select(s => s.FullName);
        var rollback = steps.Where(s => !s.IsInit).Select(s => s.RollbackScript.FullName).ToArray();
        var test = steps.Select(s => s.TestScript.FullName);

        var mandatoryFiles = (settings.RollbackRequired ? deploy.Concat(rollback) : deploy)
            .ToHashSetIgnoreCase();

        //a hook script is tracked wherever it is allowed to live, so that configuring one does not
        //turn its file into an untracked file
        var hookFiles = hookScripts.SelectMany(s => s.Candidates).Select(f => f.FullName);

        var extraFiles = (settings.RollbackRequired ? test : test.Concat(rollback))
            .Concat(data)
            .Concat(hookFiles)
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

        var regex = new Regex(settings.StepsNameRegex, RegexOptions.None, TimeSpan.FromSeconds(1));
        foreach (var step in steps.Where(s => !s.IsInit && !regex.IsMatch(s.Name)))
            yield return $"Step {step.Name} for database {step.Database} does not match expected naming convention";
    }

    /// <summary>
    /// The encoding of every file the deployment reads back, which <see cref="ScriptEncoding"/> has
    /// the say on.
    /// </summary>
    private static async Task<string[]> CheckEncodings(IDirectoryInfo directory)
    {
        var files = directory.EnumerateFiles("*.sql", SearchOption.AllDirectories)
            .Concat(directory.EnumerateFiles("*.csv", SearchOption.TopDirectoryOnly));

        var tasks = files
            .Select(file => Task.Run(() => ScriptEncoding.Validate(file)))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        return results.OfType<string>().ToArray();
    }
}