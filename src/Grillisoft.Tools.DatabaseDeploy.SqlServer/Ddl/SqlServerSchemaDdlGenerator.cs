using System.Data.Common;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Database;
using Grillisoft.Tools.DatabaseDeploy.Database.Ddl;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer.Ddl;

/// <summary>
/// Scripts a whole SQL Server database into a single deployable file.
/// </summary>
/// <remarks>
/// <see cref="SchemaDdlGenerator"/> holds the ordering and the writing; what is SQL Server's own is
/// <see cref="SqlServerObjectsDiscovery"/>, which decides what to script and what depends on what,
/// and <see cref="SqlServerObjectScripter"/>, which writes the DDL of one object at a time through
/// SMO. The scripter holds a connection of its own, which is why this is disposable.
/// </remarks>
internal sealed class SqlServerSchemaDdlGenerator : SchemaDdlGenerator, IDisposable
{
    private readonly SqlServerObjectScripter _scripter;
    private readonly SqlServerObjectsDiscovery _discovery;

    public SqlServerSchemaDdlGenerator(
        Func<string, DbCommand> createCommand,
        string connectionString,
        string databaseName,
        string migrationTable,
        ILogger logger)
        : base(databaseName, "database", logger)
    {
        _scripter = new SqlServerObjectScripter(connectionString, databaseName, logger);

        var catalog = new CatalogReader(createCommand, databaseName, logger);
        _discovery = new SqlServerObjectsDiscovery(catalog, databaseName, migrationTable, logger);
    }

    protected override Func<string, int> RankOf => SqlServerObjectType.RankOf;

    /// <summary>
    /// A line holding nothing but <c>GO</c> ends a batch, which is how sqlcmd, SQL Server Management
    /// Studio and <see cref="SqlServerScriptParser"/> all read a script.
    /// </summary>
    protected override DdlScriptWriter CreateWriter(StreamWriter stream) => new(stream, "-- ", "GO");

    protected override string StatementNoun => "batches";

    protected override Task Prepare(CancellationToken cancellationToken)
    {
        _scripter.Connect();
        return Task.CompletedTask;
    }

    protected override Task<(List<DbObject> Objects, List<(DbObject DbObject, DbObject DependsOn)> Dependencies)>
        Discover(CancellationToken cancellationToken)
        => _discovery.Discover(cancellationToken);

    protected override Task<IReadOnlyList<string>> Script(DbObject dbObject, CancellationToken cancellationToken)
    {
        if (_discovery.Find(dbObject) is not { } target)
        {
            Logger.LogWarning("Skipping {ObjectKey}: it is not one of the discovered objects", dbObject.Key);
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        var batches = _scripter.Script(target);

        if (batches.Count == 0)
            Logger.LogWarning("Skipping {ObjectKey}: SMO returned no statement", dbObject.Key);

        return Task.FromResult(batches);
    }

    protected override string Describe(Exception exception) => exception.Describe();

    protected override Exception CreateGenerationException(IEnumerable<(string Object, string Error)> failures)
        => new SqlServerDdlGenerationException(Source, failures);

    public void Dispose() => _scripter.Dispose();
}
