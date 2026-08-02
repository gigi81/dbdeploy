using System.Data.Common;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Database;
using Grillisoft.Tools.DatabaseDeploy.Database.Ddl;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.PostgreSql.Ddl;

/// <summary>
/// Scripts a whole PostgreSQL database into a single deployable file.
/// </summary>
/// <remarks>
/// <see cref="SchemaDdlGenerator"/> holds the ordering and the writing; what is PostgreSQL's own is
/// <see cref="PostgreSqlObjectsDiscovery"/> and <see cref="PostgreSqlObjectScripter"/>. Every
/// schema of the database is scripted, not one - the unit here is the database, as it is on SQL
/// Server, and schemas are objects inside it.
/// </remarks>
internal sealed class PostgreSqlSchemaDdlGenerator : SchemaDdlGenerator
{
    private readonly Func<string, DbCommand> _createCommand;
    private readonly CatalogReader _catalog;
    private readonly PostgreSqlObjectsDiscovery _discovery;
    private readonly PostgreSqlObjectScripter _scripter;

    private string? _searchPath;

    public PostgreSqlSchemaDdlGenerator(
        Func<string, DbCommand> createCommand,
        string database,
        string migrationTable,
        ILogger logger)
        : base(database, "database", logger)
    {
        _createCommand = createCommand;
        _catalog = new CatalogReader(createCommand, database, logger);
        _discovery = new PostgreSqlObjectsDiscovery(_catalog, database, migrationTable, logger);
        _scripter = new PostgreSqlObjectScripter(_catalog, _discovery, logger);
    }

    protected override Func<string, int> RankOf => PostgreSqlObjectType.RankOf;

    /// <summary>
    /// <see cref="PostgreSqlScriptParser"/> hands the whole file to Npgsql as one command, so a
    /// semicolon terminating each statement is all the separation there is - and, as a
    /// consequence, the whole script replays inside one implicit transaction.
    /// </summary>
    protected override DdlScriptWriter CreateWriter(StreamWriter stream) => new(stream, "-- ", ";");

    /// <summary>
    /// Empties the session's search path for the duration of the generation, and puts it back
    /// afterwards because the connection is the database's own and outlives this.
    /// </summary>
    /// <remarks>
    /// Without this the script is quietly wrong rather than broken: <c>format_type</c> and every
    /// <c>pg_get_..._def</c> leave the schema off anything the search path already reaches, so the
    /// output is full of unqualified names that resolve correctly here and nowhere else. It is the
    /// first thing <c>pg_dump</c> does, and the PostgreSQL counterpart of Oracle's transform
    /// parameters.
    /// </remarks>
    protected async override Task Prepare(CancellationToken cancellationToken)
    {
        _searchPath = await ReadSearchPath(cancellationToken);

        await using var command = _createCommand(PostgreSqlDdlQueries.ClearSearchPath);
        await command.ExecuteNonQueryAsync(cancellationToken);

        Logger.LogDebug("Emptied the search path for the generation; it was {SearchPath}", _searchPath);
    }

    private async Task<string?> ReadSearchPath(CancellationToken cancellationToken)
    {
        try
        {
            await using var command = _createCommand(PostgreSqlDdlQueries.ShowSearchPath);
            return await command.ExecuteScalarAsync(cancellationToken) as string;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogDebug(ex, "Could not read the search path; it will be left empty after the generation");
            return null;
        }
    }

    private async Task RestoreSearchPath(CancellationToken cancellationToken)
    {
        if (_searchPath is null)
            return;

        try
        {
            await using var command = _createCommand(
                $"SELECT pg_catalog.set_config('search_path', {_searchPath.ToSqlLiteral()}, false)");

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning(ex, "Could not restore the search path to {SearchPath}", _searchPath);
        }
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

        return _scripter.Script(target, cancellationToken);
    }

    /// <summary>
    /// The comments, and the restoring of the search path.
    /// </summary>
    /// <remarks>
    /// <c>pg_identify_object</c> hands back an identity that is already qualified and quoted and a
    /// type name that is the <c>COMMENT ON</c> keyword, so one query covers a comment on anything.
    /// Only the comments on objects that were actually scripted are written.
    /// </remarks>
    protected async override Task WriteEpilogue(
        DdlScriptWriter writer,
        IReadOnlyList<DbObject> ordered,
        CancellationToken cancellationToken)
    {
        var scripted = ordered.Select(o => Normalize(o.Name)).ToHashSet(StringComparer.Ordinal);

        var comments = await _catalog.TryQuery(
            PostgreSqlDdlQueries.Comments,
            "comments",
            reader => (
                Type: reader.GetString(0),
                Identity: reader.GetString(1),
                Description: reader.GetString(2)),
            cancellationToken);

        var written = 0;

        foreach (var (type, identity, description) in comments)
        {
            if (!IsScripted(type, identity, scripted))
                continue;

            await writer.WriteStatement(
                $"COMMENT ON {CommentKeyword(type)} {identity} IS {description.ToSqlLiteral()}");

            written++;
        }

        CountStatements(written);
        Logger.LogInformation("Scripted {CommentCount} comments", written);

        await RestoreSearchPath(cancellationToken);
    }

    /// <summary>
    /// The <c>COMMENT ON</c> keyword for what <c>pg_identify_object</c> called the object.
    /// </summary>
    /// <remarks>
    /// The two agree everywhere but on columns, which it names after the relation they belong to -
    /// "table column", "view column", "materialized view column" - where the statement wants
    /// nothing but <c>COLUMN</c>.
    /// </remarks>
    private static string CommentKeyword(string type)
        => type.EndsWith("column", StringComparison.Ordinal) ? "COLUMN" : type.ToUpperInvariant();

    /// <summary>
    /// Whether a comment belongs to something that reached the script. A column's identity is its
    /// table's plus the column name, so the table is what has to be looked for.
    /// </summary>
    private static bool IsScripted(string type, string identity, HashSet<string> scripted)
    {
        var normalized = Normalize(identity);

        if (scripted.Contains(normalized))
            return true;

        if (!type.EndsWith("column", StringComparison.Ordinal))
            return false;

        var separator = normalized.LastIndexOf('.');
        return separator > 0 && scripted.Contains(normalized[..separator]);
    }

    /// <summary>
    /// A name reduced to what both sides of the comparison agree on.
    /// </summary>
    /// <remarks>
    /// The two names come from different places and are spelled differently:
    /// <see cref="PostgreSqlObject"/> quotes every identifier, while
    /// <c>pg_identify_object</c> quotes only the ones that need it, and it names a routine by its
    /// identity arguments where the object carries its full ones. Dropping the quotes and the
    /// argument list is what leaves the part that is the same.
    /// </remarks>
    private static string Normalize(string identity)
    {
        var parenthesis = identity.IndexOf('(', StringComparison.Ordinal);
        var withoutArguments = parenthesis < 0 ? identity : identity[..parenthesis];

        return withoutArguments.Replace("\"", string.Empty, StringComparison.Ordinal).Trim();
    }

    protected override string Describe(Exception exception) => exception.Describe();

    protected override Exception CreateGenerationException(IEnumerable<(string Object, string Error)> failures)
        => new PostgreSqlDdlGenerationException(Source, failures);
}
