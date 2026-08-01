using System.IO.Abstractions;
using System.Text;
using System.Text.RegularExpressions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Soenneker.Extensions.Enumerable.String;
using Soenneker.Extensions.String;

// ReSharper disable EnforceForeachStatementBraces

namespace Grillisoft.Tools.DatabaseDeploy;

public static class BranchesValidator
{
    public static async Task<List<string>> Validate(
        ICollection<Branch> branches,
        GlobalSettings settings,
        IDirectoryInfo directory,
        Func<string, DatabaseHooks>? hooks = null)
    {
        var steps = branches.SelectMany(b => b.Steps).Distinct().ToArray();
        var hookScripts = GetHookScripts(steps, directory, hooks);

        var errors = Array.Empty<string>()
            .Concat(CheckFiles(settings, directory, steps, hookScripts))
            .Concat(CheckHookFiles(hookScripts))
            .Concat(CheckForDuplicateSteps(branches))
            .Concat(CheckForInvalidStepNames(settings, steps))
            .Concat(await CheckForBOMs(directory).ToArrayAsync())
            .ToList();

        return errors;
    }

    /// <summary>
    /// The hook scripts of every database taking part in the branches. A database with no hook
    /// configured contributes none.
    /// </summary>
    private static HookScript[] GetHookScripts(Step[] steps, IDirectoryInfo directory, Func<string, DatabaseHooks>? hooks)
    {
        if (hooks == null)
            return [];

        return steps.Select(s => s.Database)
            .Distinct(StringComparer.InvariantCultureIgnoreCase)
            .SelectMany(database => GetHookScripts(database, hooks(database), directory))
            .ToArray();
    }

    private static IEnumerable<HookScript> GetHookScripts(string database, DatabaseHooks hooks, IDirectoryInfo directory)
    {
        return hooks.Configured.Select(hook => new HookScript(database, hook, hooks[hook], directory));
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

    // ReSharper disable once InconsistentNaming
    private static async IAsyncEnumerable<string> CheckForBOMs(IDirectoryInfo directory)
    {
        var files = directory.EnumerateFiles("*.sql", SearchOption.AllDirectories)
            .Concat(directory.EnumerateFiles("*.csv", SearchOption.TopDirectoryOnly));

        var tasks = files
            .Select(file => Task.Run(async () => (File: file, Encoding: await DetectBOM(file))))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        foreach (var result in results.Where(t => t.Encoding != null))
        {
            yield return $"BOM detected on file {result.File.FullName}. Convert the file to UTF8 without BOM";
        }
    }

    // ReSharper disable once InconsistentNaming
    private static async Task<Encoding?> DetectBOM(IFileInfo file)
    {
        var buffer = new byte[4];
        await using var stream = file.OpenRead();
        var count = await stream.ReadAsync(buffer).ConfigureAwait(false);
        if (count <= 0)
            return null;

        // Check for BOM patterns
        if (buffer[0] == 0x2b && buffer[1] == 0x2f && buffer[2] == 0x76)
#pragma warning disable SYSLIB0001
            return Encoding.UTF7;
#pragma warning restore SYSLIB0001
        if (buffer[0] == 0xef && buffer[1] == 0xbb && buffer[2] == 0xbf)
            return Encoding.UTF8;
        if (buffer[0] == 0xff && buffer[1] == 0xfe && buffer[2] == 0x00 && buffer[3] == 0x00)
            return Encoding.UTF32; // UTF-32 LE
        if (buffer[0] == 0x00 && buffer[1] == 0x00 && buffer[2] == 0xfe && buffer[3] == 0xff)
            return Encoding.GetEncoding("UTF-32BE"); // UTF-32 BE
        if (buffer[0] == 0xff && buffer[1] == 0xfe)
            return Encoding.Unicode; // UTF-16 LE
        if (buffer[0] == 0xfe && buffer[1] == 0xff)
            return Encoding.BigEndianUnicode; // UTF-16 BE

        // No BOM found
        return null;
    }
}