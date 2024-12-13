using System.Diagnostics;
using System.IO.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Exceptions;
using Grillisoft.Tools.DatabaseDeploy.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Grillisoft.Tools.DatabaseDeploy.Services;

public class FormatService : BaseService
{
    private readonly FormatOptions _options;
    private readonly ISqlFormatterFactory _sqlFormatterFactory;

    public FormatService(
        FormatOptions options,
        ISqlFormatterFactory sqlFormatterFactory,
        IDatabasesCollection databases,
        IFileSystem fileSystem,
        IOptions<GlobalSettings> globalSettings,
        ILogger logger)
        : base(databases, fileSystem, globalSettings, logger)
    {
        _options = options;
        _sqlFormatterFactory = sqlFormatterFactory;
    }

    public async override Task<int> Execute(CancellationToken cancellationToken)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();
            _logger.LogInformation("Starting SQL formatting");
            var rootDir = _fileSystem.DirectoryInfo.New(_options.Path);
            var branches = await LoadBranchesManager(_options.Path, cancellationToken);
            var databases = branches.Branches.Values.SelectMany(b => b.Databases).DistinctIgnoreCase();

            foreach (var databaseName in databases)
            {
                //var database = await GetDatabase(databaseName, cancellationToken);
                //TODO: fix dialect
                await FormatDatabaseSqlFiles(rootDir.SubDirectory(databaseName), "sql", cancellationToken);
            }

            _logger.LogInformation("SQL formatting completed successfully in {Elapsed}", stopwatch.Elapsed);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Formatting of SQL failed with error {ErrorMessage} errors", ex.Message);
            return -1;
        }
    }

    private async Task FormatDatabaseSqlFiles(IDirectoryInfo directory, string sqlDialect, CancellationToken cancellationToken)
    {
        var formatter = _sqlFormatterFactory.GetSqlFormatter(sqlDialect);
        var files = directory.GetFiles("*.sql", SearchOption.TopDirectoryOnly);

        foreach (var file in files)
        {
            await formatter.FormatSql(file, cancellationToken);
        }
    }
}