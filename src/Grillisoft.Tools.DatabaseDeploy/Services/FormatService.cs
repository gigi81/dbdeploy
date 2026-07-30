using System.Diagnostics;
using System.IO.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Exceptions;
using Grillisoft.Tools.DatabaseDeploy.Formatting;
using Grillisoft.Tools.DatabaseDeploy.Options;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Grillisoft.Tools.DatabaseDeploy.Services;

/// <summary>
/// Rewrites SQL scripts in place. By default it walks the branch layout and formats the deploy and
/// rollback script of every step; given <c>--include</c> globs it formats whatever they match,
/// without reading the branch structure or contacting a database.
/// </summary>
public class FormatService : BaseService
{
    private readonly FormatOptions _options;
    private readonly IFileSystem _fileSystem;
    private readonly EditorConfigSqlOptions _editorConfig;
    private readonly IReadOnlyDictionary<string, IDatabaseFactory> _factories;

    public FormatService(
        FormatOptions options,
        IDatabasesCollection databases,
        IEnumerable<IDatabaseFactory> factories,
        IFileSystem fileSystem,
        IOptions<GlobalSettings> globalSettings,
        ILogger<FormatService> logger)
        : base(databases, fileSystem, globalSettings, logger)
    {
        _options = options;
        _fileSystem = fileSystem;
        _editorConfig = new EditorConfigSqlOptions(fileSystem, logger);
        _factories = factories.ToDictionary(f => f.Name, f => f, StringComparer.InvariantCultureIgnoreCase);
    }

    public override Task<int> Execute(CancellationToken cancellationToken) =>
        _options.IsDirectoryMode
            ? FormatMatchingFiles(cancellationToken)
            : FormatBranchScripts(cancellationToken);

    private async Task<int> FormatBranchScripts(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var manager = await LoadBranchesManager(_options.Path, cancellationToken);

        var steps = manager.Branches.Values
            .SelectMany(branch => branch.Steps)
            .DistinctBy(step => (step.Database, step.Name))
            .ToArray();

        var deployed = await GetDeployedSteps(steps, cancellationToken);
        var formatted = 0;
        var failures = 0;

        foreach (var step in steps)
        {
            // Init scripts are generated schema dumps, and reformatting one would only produce an
            // enormous diff of something nobody reads by hand.
            if (step.IsInit)
            {
                _logger.LogDebug("Skipping init step {StepName}", step.Name);
                continue;
            }

            var database = await GetDatabase(step.Database, cancellationToken);

            if (deployed.Contains(DeployedKey(step.Database, step.Name)) && step.DeployScript.Exists)
            {
                _dbl[step.Database].LogWarning(
                    "Step {StepName} is already deployed; formatting {Path} changes its migration hash",
                    step.Name,
                    step.DeployScript.FullName);
            }

            foreach (var file in new[] { step.DeployScript, step.RollbackScript })
            {
                switch (await FormatFile(file, database.SqlFormatter, cancellationToken))
                {
                    case FormatOutcome.Rewritten:
                        formatted++;
                        break;

                    case FormatOutcome.Failed:
                        failures++;
                        break;

                    default:
                        break;
                }
            }
        }

        _logger.LogInformation(
            "Formatted {Count} script(s) in {Elapsed} with {Failures} failure(s)",
            formatted,
            stopwatch.Elapsed,
            failures);

        return failures;
    }

    /// <summary>
    /// Formats whatever the <c>--include</c> globs match. There is no branch structure to consult
    /// here, so nothing is filtered out and no database is contacted.
    /// </summary>
    private async Task<int> FormatMatchingFiles(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var root = GetDirectory(_options.Path);
        root.ThrowIfNotFound();

        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddIncludePatterns(_options.Include);
        matcher.AddExcludePatterns(_options.Exclude);

        // Matching an in-memory list rather than letting the matcher walk the disk itself, so that
        // the whole service keeps running on IFileSystem and stays testable.
        var candidates = root
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .ToDictionary(file => Relative(root, file), file => file, StringComparer.OrdinalIgnoreCase);

        var matches = matcher.Match(candidates.Keys).Files
            .Select(match => candidates[match.Path])
            .OrderBy(file => file.FullName, StringComparer.Ordinal)
            .ToArray();

        if (matches.Length == 0)
        {
            _logger.LogWarning(
                "No file under {Path} matched {Patterns}",
                root.FullName,
                string.Join(", ", _options.Include));

            return 0;
        }

        var formatted = 0;
        var failures = 0;

        foreach (var file in matches)
        {
            var formatter = await ResolveFormatter(root, file, cancellationToken);

            switch (await FormatFile(file, formatter, cancellationToken))
            {
                case FormatOutcome.Rewritten:
                    formatted++;
                    break;

                case FormatOutcome.Failed:
                    failures++;
                    break;

                default:
                    break;
            }
        }

        _logger.LogInformation(
            "Formatted {Count} of {Matched} matched script(s) in {Elapsed} with {Failures} failure(s)",
            formatted,
            matches.Length,
            stopwatch.Elapsed,
            failures);

        return failures;
    }

    /// <summary>
    /// The dialect to format a file with: the nearest folder above it that names a configured
    /// database wins, so a normal layout still gets the right dialect per database; otherwise the
    /// provider given on the command line, and failing that the configured default.
    /// </summary>
    private async Task<ISqlFormatter> ResolveFormatter(
        IDirectoryInfo root,
        IFileInfo file,
        CancellationToken cancellationToken)
    {
        for (var directory = file.Directory; directory is not null; directory = directory.Parent)
        {
            if (directory.FullName.Equals(root.FullName, StringComparison.OrdinalIgnoreCase))
                break;

            var match = this.Databases.FirstOrDefault(
                name => name.Equals(directory.Name, StringComparison.InvariantCultureIgnoreCase));

            if (match is not null)
            {
                var database = await GetDatabase(match, cancellationToken);
                return database.SqlFormatter;
            }
        }

        return GetFactory().SqlFormatter;
    }

    private IDatabaseFactory GetFactory()
    {
        var name = _globalSettings.Value.DefaultProvider.OverrideWith(_options.Provider);

        if (!string.IsNullOrWhiteSpace(name) && _factories.TryGetValue(name, out var factory))
            return factory;

        throw new SqlDialectNotFoundException(name, _factories.Keys);
    }

    private static string Relative(IDirectoryInfo root, IFileInfo file) =>
        file.FullName[(root.FullName.Length + 1)..].Replace('\\', '/');

    private enum FormatOutcome
    {
        Skipped,
        Unchanged,
        Rewritten,
        Failed
    }

    private async Task<FormatOutcome> FormatFile(
        IFileInfo file,
        ISqlFormatter formatter,
        CancellationToken cancellationToken)
    {
        if (!file.Exists)
            return FormatOutcome.Skipped;

        var sql = await _fileSystem.File.ReadAllTextAsync(file.FullName, cancellationToken);
        var options = _editorConfig.For(file.FullName, DetectNewLine(sql));

        if (!options.Enabled)
        {
            _logger.LogDebug("Formatting is disabled for {Path}", file.FullName);
            return FormatOutcome.Skipped;
        }

        var result = formatter.Format(sql, options);

        if (!result.Verified)
        {
            // The formatter produced something that is not the same script. Leaving the file alone
            // is the only safe outcome, and the run fails so it cannot pass unnoticed.
            _logger.LogError(
                "Not formatting {Path}: the result did not match the source ({Error})",
                file.FullName,
                result.VerificationError);

            return FormatOutcome.Failed;
        }

        if (string.Equals(result.Sql, sql, StringComparison.Ordinal))
        {
            _logger.LogDebug("{Path} is already formatted", file.FullName);
            return FormatOutcome.Unchanged;
        }

        await _fileSystem.File.WriteAllTextAsync(file.FullName, result.Sql, options.Encoding, cancellationToken);
        _logger.LogInformation("Formatted {Path}", file.FullName);

        return FormatOutcome.Rewritten;
    }

    /// <summary>
    /// Whatever the file already uses, so that a repository with no <c>end_of_line</c> setting does
    /// not have its line endings flipped as a side effect.
    /// </summary>
    private static string? DetectNewLine(string sql)
    {
        var index = sql.IndexOf('\n');

        if (index < 0)
            return null;

        return index > 0 && sql[index - 1] == '\r' ? "\r\n" : "\n";
    }

    /// <summary>
    /// Reads the recorded migrations so that formatting a script that has already been deployed can
    /// be called out. Formatting must still work with no database in reach, so a failure here only
    /// costs the warning.
    /// </summary>
    private async Task<HashSet<string>> GetDeployedSteps(
        IEnumerable<Step> steps,
        CancellationToken cancellationToken)
    {
        var deployed = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);

        foreach (var name in steps.Select(step => step.Database).DistinctIgnoreCase())
        {
            try
            {
                var database = await GetDatabase(name, cancellationToken);

                foreach (var migration in await database.GetMigrations(cancellationToken))
                    deployed.Add(DeployedKey(name, migration.Name));
            }
            catch (Exception ex)
            {
                // Formatting must work with no database in reach, so the warning carries only the
                // message; the stack trace would drown the output of an otherwise successful run.
                _dbl[name].LogWarning(
                    "Could not read the deployed migrations ({Reason}), so already-deployed scripts will not be called out",
                    ex.Message);

                _dbl[name].LogDebug(ex, "Reading the deployed migrations failed");
            }
        }

        return deployed;
    }

    private static string DeployedKey(string database, string step) => database + ' ' + step;
}
