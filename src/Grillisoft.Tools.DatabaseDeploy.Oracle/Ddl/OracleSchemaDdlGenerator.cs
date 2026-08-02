using System.Data.Common;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Database;
using Grillisoft.Tools.DatabaseDeploy.Database.Ddl;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle.Ddl;

/// <summary>
/// Scripts a whole Oracle schema into a single deployable file.
/// </summary>
/// <remarks>
/// <see cref="SchemaDdlGenerator"/> holds the ordering and the writing; what is Oracle's own is
/// <see cref="OracleObjectsDiscovery"/>, which decides what to script and what depends on what, and
/// <see cref="OracleObjectScripter"/>, which writes the DDL of one object at a time through
/// <c>DBMS_METADATA</c>. The comments are the one thing that has to be written outside the object
/// loop - see <see cref="WriteEpilogue"/>.
/// </remarks>
internal sealed class OracleSchemaDdlGenerator : SchemaDdlGenerator
{
    private readonly OracleObjectScripter _scripter;
    private readonly OracleObjectsDiscovery _discovery;

    public OracleSchemaDdlGenerator(
        Func<string, DbCommand> createCommand,
        string schema,
        string migrationTable,
        ILogger logger)
        : base(schema, "schema", logger)
    {
        var catalog = new CatalogReader(createCommand, schema, logger);
        _scripter = new OracleObjectScripter(createCommand, catalog, schema, logger);
        _discovery = new OracleObjectsDiscovery(catalog, schema, migrationTable, logger);
    }

    protected override Func<string, int> RankOf => OracleObjectType.RankOf;

    /// <summary>
    /// SQL*Plus <c>REM</c> rather than <c>--</c>, because <see cref="OracleScriptParser"/> drops
    /// those lines instead of sending them to the server as a statement of their own, and a line
    /// holding nothing but <c>/</c> is what both it and SQL*Plus end a statement on.
    /// </summary>
    protected override DdlScriptWriter CreateWriter(StreamWriter stream) => new(stream, "REM ", "/");

    protected override Task Prepare(CancellationToken cancellationToken)
        => _scripter.Configure(cancellationToken);

    protected override Task<(List<DbObject> Objects, List<(DbObject DbObject, DbObject DependsOn)> Dependencies)>
        Discover(CancellationToken cancellationToken)
        => _discovery.Discover(cancellationToken);

    protected override Task<IReadOnlyList<string>> Script(DbObject dbObject, CancellationToken cancellationToken)
        => _scripter.Script(dbObject, cancellationToken);

    protected override string Describe(Exception exception) => exception.Describe();

    protected override Exception CreateGenerationException(IEnumerable<(string Object, string Error)> failures)
        => new OracleDdlGenerationException(Source, failures);

    /// <summary>
    /// Comments are not schema objects and <c>DBMS_METADATA</c> only returns them attached to their
    /// table, which is not an option here since the table DDL is emitted on its own.
    /// </summary>
    protected async override Task WriteEpilogue(
        DdlScriptWriter writer,
        IReadOnlyList<DbObject> ordered,
        CancellationToken cancellationToken)
    {
        var comments = await _discovery.GetComments(ordered, cancellationToken);

        foreach (var (table, column, comment) in comments)
        {
            var target = column is null ? table.Quote() : $"{table.Quote()}.{column.Quote()}";
            var what = column is null ? "TABLE" : "COLUMN";

            await writer.WriteStatement($"COMMENT ON {what} {target} IS {comment.ToSqlLiteral()}");
        }

        CountStatements(comments.Count);
        Logger.LogInformation("Scripted {CommentCount} comments", comments.Count);
    }

    protected override void LogSummaryDetails()
    {
        if (_scripter.FallbacksUsed > 0)
        {
            Logger.LogWarning(
                "{FallbackCount} object(s) were rebuilt from the data dictionary because DBMS_METADATA refused them; " +
                "granting SELECT_CATALOG_ROLE to the connected user produces a more faithful script",
                _scripter.FallbacksUsed);
        }
    }
}
