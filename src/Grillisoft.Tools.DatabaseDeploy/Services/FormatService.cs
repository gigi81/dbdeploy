using System.Diagnostics;
using System.IO.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Exceptions;
using Grillisoft.Tools.DatabaseDeploy.Formatting;
using Grillisoft.Tools.DatabaseDeploy.Options;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Logging;
using Soenneker.Extensions.String;

namespace Grillisoft.Tools.DatabaseDeploy.Services;

/// <summary>
/// Rewrites SQL scripts in place. By default it walks the branch layout and formats the deploy and
/// rollback script of every step that has not been released yet - a released deploy script is left
/// alone unless <c>--force</c> is given, because its MD5 is the migration hash the databases that
/// ran it recorded; given <c>--include</c> globs it formats whatever they match,
/// without reading the branch structure. Either way this is a pure file operation: the scripts, the
/// branch files, <c>.editorconfig</c> and the settings file are all it reads, and no database is
/// ever built or contacted.
/// </summary>
public class FormatService : BaseService
{
    private readonly FormatOptions _options;
    private readonly IFileSystem _fileSystem;
    private readonly EditorConfigSqlOptions _editorConfig;
    private readonly IReadOnlyDictionary<string, IDatabaseFactory> _factories;

    public FormatService(
        FormatOptions options,
        IEnumerable<IDatabaseFactory> factories,
        ServiceDependencies dependencies,
        ILogger<FormatService> logger)
        : base(dependencies, logger)
    {
        _options = options;
        _fileSystem = dependencies.FileSystem;
        _editorConfig = new EditorConfigSqlOptions(_fileSystem, logger);
        _factories = factories.ToDictionary(f => f.Name, f => f, StringComparer.InvariantCultureIgnoreCase);
    }

    public override Task<int> Execute(CancellationToken cancellationToken) =>
        _options.IsDirectoryMode
            ? FormatMatchingFiles(cancellationToken)
            : FormatBranchScripts(cancellationToken);

    private async Task<int> FormatBranchScripts(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var branches = await LoadBranches(_options.Path, cancellationToken);

        var steps = branches.Branches.Values
            .SelectMany(branch => branch.Steps)
            .DistinctBy(step => (step.Database, step.Name))
            .ToArray();

        var defaultBranch = _globalSettings.Value.DefaultBranch;
        var formatted = 0;
        var released = 0;
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

            var formatter = ResolveFormatter(step.Database);

            // A step that sits in the default branch file has been released, so it has very likely
            // been deployed somewhere. The branch files are the only evidence of that on disk, and
            // formatting never asks a database.
            var isReleased = step.Branch.EqualsIgnoreCase(defaultBranch);

            foreach (var (file, isDeployScript) in new[] { (step.DeployScript, true), (step.RollbackScript, false) })
            {
                // The migration hash is the MD5 of the deploy script, so rewriting one that is
                // already out there stops it matching what the databases that ran it recorded. Only
                // the deploy script is hashed, so the rollback script of a released step is still
                // formatted. The warning does not depend on the file needing any change: what
                // matters is that it was left alone, and why.
                if (isDeployScript && isReleased && !_options.Force)
                {
                    if (file.Exists)
                    {
                        released++;
                        _dbl[step.Database].LogWarning(
                            "Step {StepName} is released in {Branch}: not formatting {Path}, it would change its migration hash. Pass --force to format it anyway",
                            step.Name,
                            defaultBranch,
                            file.FullName);
                    }

                    continue;
                }

                switch (await FormatFile(file, formatter, cancellationToken))
                {
                    case FormatOutcome.Rewritten:
                        formatted++;

                        if (isDeployScript && isReleased)
                        {
                            _dbl[step.Database].LogWarning(
                                "Step {StepName} is released in {Branch} and may already be deployed: formatting {Path} changed its migration hash",
                                step.Name,
                                defaultBranch,
                                file.FullName);
                        }

                        break;

                    case FormatOutcome.Failed:
                        failures++;
                        break;

                    case FormatOutcome.Skipped:
                    case FormatOutcome.Unchanged:
                    default:
                        break;
                }
            }
        }

        _logger.LogInformation(
            "Formatted {Count} script(s) in {Elapsed} with {Failures} failure(s), leaving {Released} released script(s) alone",
            formatted,
            stopwatch.Elapsed,
            failures,
            released);

        return failures;
    }

    /// <summary>
    /// Formats whatever the <c>--include</c> globs match. There is no branch structure to consult
    /// here, so nothing is filtered out.
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
            switch (await FormatFile(file, ResolveFormatter(root, file), cancellationToken))
            {
                case FormatOutcome.Rewritten:
                    formatted++;
                    break;

                case FormatOutcome.Failed:
                    failures++;
                    break;

                case FormatOutcome.Skipped:
                case FormatOutcome.Unchanged:
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
    private ISqlFormatter ResolveFormatter(IDirectoryInfo root, IFileInfo file)
    {
        for (var directory = file.Directory; directory is not null; directory = directory.Parent)
        {
            if (directory.FullName.Equals(root.FullName, StringComparison.OrdinalIgnoreCase))
                break;

            var match = this.Databases.FirstOrDefault(
                name => name.Equals(directory.Name, StringComparison.InvariantCultureIgnoreCase));

            if (match is not null && GetSqlFormatter(match) is { } formatter)
                return formatter;
        }

        return GetFactory().SqlFormatter;
    }

    /// <summary>
    /// The dialect a branch step's scripts are in. It comes from the provider configured for that
    /// database - configuration only, so no database is built and no connection string is needed -
    /// and falls back to <c>--provider</c> when the settings say nothing about it.
    /// </summary>
    private ISqlFormatter ResolveFormatter(string database) =>
        GetSqlFormatter(database) ?? GetFactory().SqlFormatter;

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

        _logger.LogDebug("Formatting {Path} as {Dialect}", file.FullName, formatter.Dialect);

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
        _logger.LogInformation("Formatted {Path} as {Dialect}", file.FullName, formatter.Dialect);

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
}
