using System.Diagnostics;
using System.IO.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Soenneker.Extensions.String;

// ReSharper disable InconsistentNaming

namespace Grillisoft.Tools.DatabaseDeploy.Services;

public abstract class BaseService : IExecutable
{
    private readonly IDatabasesCollection _databases;
    private readonly IFileSystem _fileSystem;
    private readonly ScriptsRunner _scripts;
    protected readonly IOptions<GlobalSettings> _globalSettings;
    protected readonly ILogger _logger;
    protected readonly DatabaseLoggerFactory _dbl;

    protected BaseService(
        IDatabasesCollection databases,
        IFileSystem fileSystem,
        IOptions<GlobalSettings> globalSettings,
        ILogger logger)
    {
        _databases = databases;
        _fileSystem = fileSystem;
        _globalSettings = globalSettings;
        _logger = logger;
        _dbl = new DatabaseLoggerFactory(logger);
        _scripts = new ScriptsRunner(logger);
    }

    public abstract Task<int> Execute(CancellationToken cancellationToken);

    protected IDirectoryInfo GetDirectory(string path)
    {
        return _fileSystem.DirectoryInfo.New(path);
    }

    protected async Task<BranchesReader> LoadBranches(string path, CancellationToken cancellationToken)
    {
        var directory = this.GetDirectory(path);
        var branches = new BranchesReader(directory, _globalSettings.Value, _databases.GetHooks);

        _logger.LogInformation("Loading branches from {Directory}", directory.FullName);
        var errors = await branches.Load();

        foreach (var error in errors)
            _logger.LogError(error);

        if (errors.Count > 0)
            throw new InvalidBranchesConfigurationException(errors);

        return branches;
    }

    protected Task RunScripts(IEnumerable<IFileInfo> scriptFiles, IDatabase database, CancellationToken cancellationToken)
    {
        return _scripts.Run(scriptFiles, database, cancellationToken);
    }

    protected Task RunScript(IFileInfo scriptFile, IDatabase database, CancellationToken cancellationToken)
    {
        return _scripts.Run(scriptFile, database, cancellationToken);
    }

    protected async Task<Strategy> GetStrategy(Step[] steps, CancellationToken cancellationToken)
    {
        return new Strategy(steps, await GetAllMigrations(steps, cancellationToken), _logger);
    }

    private async Task<Dictionary<string, DatabaseMigration[]>> GetAllMigrations(Step[] steps, CancellationToken cancellationToken)
    {
        var tasks = steps.Select(s => s.Database)
            .Distinct()
            .Select(name => GetMigrations(name, cancellationToken))
            .ToArray();

        var tuples = await Task.WhenAll(tasks);

        return tuples.ToDictionary(
            m => m.Item1,
            m => m.Item2
        );
    }

    protected IReadOnlyCollection<string> Databases => _databases.Databases;

    protected async Task<IDatabase> GetDatabase(string name, CancellationToken cancellationToken)
    {
        return await _databases.GetDatabase(name, cancellationToken);
    }

    /// <summary>
    /// The formatter for a database's dialect, without building the database or needing a
    /// connection. Null when the configuration does not say which provider the database uses.
    /// </summary>
    protected ISqlFormatter? GetSqlFormatter(string name)
    {
        return _databases.GetSqlFormatter(name);
    }

    private async Task<(string, DatabaseMigration[])> GetMigrations(string name, CancellationToken cancellationToken)
    {
        var database = await _databases.GetDatabase(name, cancellationToken);
        var migrations = await database.GetMigrations(cancellationToken);
        _dbl[database.Name].LogInformation("Found {MigrationsCount} existing migrations in database", migrations.Count);
        return (name, migrations.ToArray());
    }
}