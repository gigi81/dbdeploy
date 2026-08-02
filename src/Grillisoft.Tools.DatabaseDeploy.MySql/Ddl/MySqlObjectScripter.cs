using System.Data.Common;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.MySql.Ddl;

/// <summary>
/// Turns one object into the statements that create it, using <c>SHOW CREATE</c>.
/// </summary>
/// <remarks>
/// <c>SHOW CREATE</c> is the server's own scripting engine and there is no point reimplementing it,
/// but what it returns describes the object as it stands here rather than as it should be recreated
/// elsewhere - see <see cref="MySqlDdlRewriter"/> for everything that has to come back off.
/// </remarks>
internal sealed class MySqlObjectScripter
{
    private readonly Func<string, DbCommand> _createCommand;
    private readonly string _database;
    private readonly ILogger _logger;

    /// <summary>The foreign keys taken out of a table, keyed by the object they were split into.</summary>
    private readonly Dictionary<string, string> _foreignKeys = new(StringComparer.OrdinalIgnoreCase);

    public MySqlObjectScripter(Func<string, DbCommand> createCommand, string database, ILogger logger)
    {
        _createCommand = createCommand;
        _database = database;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> Script(DbObject dbObject, CancellationToken cancellationToken)
    {
        if (dbObject.Type == MySqlObjectType.ForeignKey)
            return ScriptForeignKey(dbObject);

        var type = MySqlObjectType.Find(dbObject.Type);
        if (type is null)
        {
            _logger.LogWarning("Skipping {ObjectKey}: unsupported object type", dbObject.Key);
            return [];
        }

        var ddl = await GetDdl(dbObject, type, cancellationToken);
        if (string.IsNullOrWhiteSpace(ddl))
        {
            _logger.LogWarning("Skipping {ObjectKey}: the server returned no DDL", dbObject.Key);
            return [];
        }

        ddl = MySqlDdlRewriter.StripDefiner(ddl);
        ddl = MySqlDdlRewriter.RemoveDatabaseQualifier(ddl, _database);

        if (type.Name != MySqlObjectType.Table)
            return [ddl.Trim()];

        ddl = MySqlDdlRewriter.StripAutoIncrement(ddl);

        var (table, foreignKeys) = MySqlDdlRewriter.SplitForeignKeys(ddl, dbObject.Name);
        foreach (var (name, statement) in foreignKeys)
            _foreignKeys[$"{dbObject.Name}.{name}"] = statement;

        return [table.Trim()];
    }

    /// <summary>
    /// A foreign key has no DDL of its own to ask for: it was taken out of its table's
    /// <c>CREATE TABLE</c>, which the ordering guarantees was scripted first.
    /// </summary>
    private IReadOnlyList<string> ScriptForeignKey(DbObject dbObject)
    {
        if (_foreignKeys.TryGetValue(dbObject.Name, out var statement))
            return [statement];

        _logger.LogWarning("Skipping {ObjectKey}: its table did not yield the constraint", dbObject.Key);
        return [];
    }

    private async Task<string?> GetDdl(DbObject dbObject, MySqlObjectType type, CancellationToken cancellationToken)
    {
        // SHOW CREATE takes no parameters, so the name goes in quoted rather than bound.
        await using var command = _createCommand($"{type.ShowStatement} {dbObject.Name.Quote()}");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var ordinal = FindDdlColumn(reader, type);
        if (ordinal < 0)
        {
            _logger.LogWarning("Skipping {ObjectKey}: no column of the result holds the DDL", dbObject.Key);
            return null;
        }

        return await reader.IsDBNullAsync(ordinal, cancellationToken) ? null : reader.GetString(ordinal);
    }

    /// <summary>
    /// The column holding the statement, by name.
    /// </summary>
    /// <remarks>
    /// The shape of a <c>SHOW CREATE</c> result is not the same for every type: a trigger comes
    /// back in seven columns and calls its DDL <c>SQL Original Statement</c>, so reading the second
    /// column works for a table and quietly returns the wrong thing for a trigger. Falling back to
    /// the first column named <c>Create ...</c> covers a server that spells it differently.
    /// </remarks>
    private static int FindDdlColumn(DbDataReader reader, MySqlObjectType type)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (string.Equals(reader.GetName(i), type.DdlColumn, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (reader.GetName(i).StartsWith("Create", StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }
}
