using System.Data.Common;
using System.Text;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Database;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle.Ddl;

/// <summary>
/// Turns one object into the statements that create it, using <c>DBMS_METADATA</c>.
/// </summary>
/// <remarks>
/// <c>DBMS_METADATA</c> is the server's own scripting engine and there is no point reimplementing
/// it, but it has to be talked into producing something portable - see
/// <see cref="OracleDdlQueries.TransformParameters"/> - and it refuses outright for a caller
/// without <c>SELECT_CATALOG_ROLE</c>, which is why there is a data dictionary fallback underneath.
/// </remarks>
internal sealed class OracleObjectScripter
{
    private readonly Func<string, DbCommand> _createCommand;
    private readonly CatalogReader _catalog;
    private readonly string _schema;
    private readonly ILogger _logger;

    public OracleObjectScripter(
        Func<string, DbCommand> createCommand,
        CatalogReader catalog,
        string schema,
        ILogger logger)
    {
        _createCommand = createCommand;
        _catalog = catalog;
        _schema = schema;
        _logger = logger;
    }

    /// <summary>How many objects had to be rebuilt from the dictionary rather than scripted.</summary>
    public int FallbacksUsed { get; private set; }

    /// <summary>
    /// Configures the session so <c>DBMS_METADATA</c> emits DDL that can be replayed on another
    /// database. Applied once per generation, not once per object.
    /// </summary>
    public async Task Configure(CancellationToken cancellationToken)
    {
        var block = BuildTransformBlock(OracleDdlQueries.TransformParameters);

        try
        {
            await using var command = _createCommand(block);
            await command.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogDebug("Applied DDL transform parameters: {Parameters}",
                string.Join(", ", OracleDdlQueries.TransformParameters.Select(p => $"{p.Name}={p.Value}")));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not apply the DDL transform parameters in a single block; applying them one by one");
            await ConfigureIndividually(cancellationToken);
        }
    }

    /// <summary>
    /// Older releases reject transform parameters that newer ones accept. Applying them one at a
    /// time keeps the ones that work and names the ones that do not.
    /// </summary>
    private async Task ConfigureIndividually(CancellationToken cancellationToken)
    {
        foreach (var parameter in OracleDdlQueries.TransformParameters)
        {
            try
            {
                await using var command = _createCommand(BuildTransformBlock([parameter]));
                await command.ExecuteNonQueryAsync(cancellationToken);
                _logger.LogDebug("Applied DDL transform parameter {Parameter}={Value}", parameter.Name, parameter.Value);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "DDL transform parameter {Parameter}={Value} was rejected by the server ({Error}); the generated DDL may be less portable",
                    parameter.Name, parameter.Value, ex.Describe());
            }
        }
    }

    private static string BuildTransformBlock(IEnumerable<(string Name, bool Value)> parameters)
    {
        var block = new StringBuilder("BEGIN").AppendLine();

        foreach (var (name, value) in parameters)
        {
            block.Append("  DBMS_METADATA.SET_TRANSFORM_PARAM(DBMS_METADATA.SESSION_TRANSFORM, '")
                 .Append(name)
                 .Append("', ")
                 .Append(value ? "TRUE" : "FALSE")
                 .AppendLine(");");
        }

        return block.Append("END;").ToString();
    }

    /// <summary>
    /// The statements that create the object, or an empty list when there is nothing to write.
    /// </summary>
    public async Task<IReadOnlyList<string>> Script(DbObject dbObject, CancellationToken cancellationToken)
    {
        var type = OracleObjectType.Find(dbObject.Type);
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

        var statements = OracleDdlSplitter.Split(ddl, type.IsPlSql);
        if (statements.Count == 0)
            _logger.LogWarning("Skipping {ObjectKey}: the DDL returned by the server holds no statement", dbObject.Key);
        else
            _logger.LogDebug("Scripted {ObjectKey} into {StatementCount} statement(s), {Length} chars",
                dbObject.Key, statements.Count, ddl.Length);

        return statements;
    }

    /// <summary>
    /// Asks <c>DBMS_METADATA</c> for the DDL, retrying with the alternate object type name when the
    /// server does not know the one we tried, and falling back to the dictionary source when it
    /// refuses altogether.
    /// </summary>
    private async Task<string?> GetDdl(DbObject dbObject, OracleObjectType type, CancellationToken cancellationToken)
    {
        try
        {
            return await GetMetadataDdl(dbObject.Name, type.MetadataType, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "DBMS_METADATA.GET_DDL('{MetadataType}', '{ObjectName}') failed", type.MetadataType, dbObject.Name);

            if (type.FallbackMetadataType is { } alternate)
            {
                try
                {
                    var ddl = await GetMetadataDdl(dbObject.Name, alternate, cancellationToken);
                    _logger.LogDebug("Scripted {ObjectKey} using the alternate metadata type {MetadataType}", dbObject.Key, alternate);
                    return ddl;
                }
                catch (Exception alternateEx) when (alternateEx is not OperationCanceledException)
                {
                    _logger.LogDebug(alternateEx, "DBMS_METADATA.GET_DDL('{MetadataType}', '{ObjectName}') failed", alternate, dbObject.Name);
                }
            }

            var source = await GetSourceDdl(dbObject, type, cancellationToken);
            if (source is null)
                throw;

            FallbacksUsed++;
            _logger.LogWarning(
                "DBMS_METADATA could not script {ObjectKey} ({Error}); rebuilt the statement from the data dictionary instead",
                dbObject.Key, ex.Describe());

            return source;
        }
    }

    private async Task<string?> GetMetadataDdl(string objectName, string metadataType, CancellationToken cancellationToken)
    {
        await using var command = _createCommand(OracleDdlQueries.ObjectDdl);
        command.AddParameter("object_type", metadataType)
               .AddParameter("object_name", objectName)
               .AddParameter("owner", _schema);

        return await ReadClob(command, cancellationToken);
    }

    /// <summary>
    /// Rebuilds a statement from <c>ALL_SOURCE</c>, <c>ALL_VIEWS</c> or <c>ALL_TRIGGERS</c>. Not as
    /// faithful as <c>DBMS_METADATA</c>, but it only needs the privileges any schema owner has, and
    /// it is the difference between a usable script and no script at all on a locked down database.
    /// </summary>
    private async Task<string?> GetSourceDdl(DbObject dbObject, OracleObjectType type, CancellationToken cancellationToken)
    {
        try
        {
            if (dbObject.Type == "VIEW")
            {
                await using var command = _createCommand(OracleDdlQueries.ViewSource);
                command.AddParameter("owner", _schema).AddParameter("object_name", dbObject.Name);

                var text = await ReadClob(command, cancellationToken);
                return string.IsNullOrWhiteSpace(text)
                    ? null
                    : $"CREATE OR REPLACE FORCE VIEW {dbObject.Name.Quote()} AS\n{text.Trim()}";
            }

            if (dbObject.Type == "TRIGGER")
            {
                await using var command = _createCommand(OracleDdlQueries.TriggerSource);
                command.AddParameter("owner", _schema).AddParameter("object_name", dbObject.Name);

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    return null;

                var description = reader.IsDBNull(0) ? null : reader.GetString(0);
                var body = reader.IsDBNull(1) ? null : reader.GetString(1);

                return string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(body)
                    ? null
                    : $"CREATE OR REPLACE TRIGGER {description.Trim()}\n{body.Trim()}";
            }

            if (type.SourceType is null)
                return null;

            var lines = await _catalog.Query(
                OracleDdlQueries.ObjectSource,
                $"source of {dbObject.Key}",
                r => r.IsDBNull(0) ? string.Empty : r.GetString(0),
                cancellationToken,
                ("owner", _schema), ("object_name", dbObject.Name), ("object_type", type.SourceType));

            if (lines.Count == 0)
                return null;

            // ALL_SOURCE stores the text without the CREATE OR REPLACE prefix.
            return "CREATE OR REPLACE " + string.Concat(lines).TrimEnd();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Could not rebuild {ObjectKey} from the data dictionary", dbObject.Key);
            return null;
        }
    }

    /// <summary>
    /// Reads the CLOB through a reader rather than <c>ExecuteScalar</c>, which is what lets a large
    /// package body come back whole.
    /// </summary>
    private static async Task<string?> ReadClob(DbCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        if (await reader.IsDBNullAsync(0, cancellationToken))
            return null;

        using var text = reader.GetTextReader(0);
        return await text.ReadToEndAsync(cancellationToken);
    }
}
