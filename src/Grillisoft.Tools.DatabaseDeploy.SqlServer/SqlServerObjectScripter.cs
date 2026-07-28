using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using SmoDatabase = Microsoft.SqlServer.Management.Smo.Database;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer;

/// <summary>
/// Turns one object into the batches that create it, using SQL Server Management Objects.
/// </summary>
/// <remarks>
/// SMO is the same engine behind the "Generate Scripts" wizard of SQL Server Management Studio, and
/// writing a <c>CREATE TABLE</c> by hand that survives computed columns, sparse columns, temporal
/// tables, partitioning and the rest is not something worth reimplementing. What SMO does badly is
/// everything around a single object: asking it to script a whole database with dependencies is an
/// all or nothing operation that a single unscriptable object takes down, and the order it produces
/// cannot be inspected. So it is used one object at a time, and the ordering is dbdeploy's own.
/// </remarks>
internal sealed class SqlServerObjectScripter : IDisposable
{
    private readonly string _connectionString;
    private readonly string _databaseName;
    private readonly ILogger _logger;

    private SqlConnection? _connection;
    private ServerConnection? _serverConnection;
    private SmoDatabase? _database;

    public SqlServerObjectScripter(string connectionString, string databaseName, ILogger logger)
    {
        _connectionString = connectionString;
        _databaseName = databaseName;
        _logger = logger;
    }

    /// <summary>
    /// Types whose whole collection is loaded up front. Anything else is small enough that reading
    /// it one object at a time costs nothing.
    /// </summary>
    private static readonly Type[] PrefetchedTypes =
    [
        typeof(Table),
        typeof(View),
        typeof(StoredProcedure),
        typeof(UserDefinedFunction),
        typeof(UserDefinedTableType),
        typeof(UserDefinedDataType),
    ];

    /// <summary>
    /// Scripting options chosen so the script can be replayed on an empty database on another
    /// server. Everything that is scripted as an object of its own - indexes, foreign keys,
    /// triggers - is switched off here, otherwise it would come out twice.
    /// </summary>
    private ScriptingOptions Options { get; } = CreateOptions();

    /// <summary>
    /// The same options, but asking for the child collections that <see cref="Options"/> turns off.
    /// Those objects are still scripted, one at a time and in their own place in the script; what
    /// changes is that <see cref="SmoDatabase.PrefetchObjects(Type, ScriptingOptions)"/> reads them
    /// for every table at once instead of leaving each one to fetch its own.
    /// </summary>
    private ScriptingOptions PrefetchOptions { get; } = CreateOptions(withChildCollections: true);

    private static ScriptingOptions CreateOptions(bool withChildCollections = false) => new()
    {
        ScriptSchema = true,
        ScriptData = false,
        ScriptDrops = false,
        IncludeIfNotExists = false,
        // no "USE [database]": the script is replayed into whatever database is being deployed
        IncludeDatabaseContext = false,
        IncludeHeaders = false,
        SchemaQualify = true,
        SchemaQualifyForeignKeysReferences = true,
        // a script carrying the filegroups of the server it was taken from cannot be replayed
        // anywhere else, since a filegroup needs physical files that only a DBA can place
        NoFileGroup = true,
        AnsiPadding = false,
        // the closest thing SQL Server has to a comment on a table or a column
        ExtendedProperties = true,
        Permissions = false,
        // check, primary key, unique and default constraints inline in the CREATE TABLE
        DriPrimaryKey = true,
        DriUniqueKeys = true,
        DriChecks = true,
        DriDefaults = true,
        // foreign keys, indexes and triggers are scripted separately, once their table exists
        DriForeignKeys = withChildCollections,
        DriIndexes = withChildCollections,
        Indexes = withChildCollections,
        ClusteredIndexes = withChildCollections,
        NonClusteredIndexes = withChildCollections,
        XmlIndexes = withChildCollections,
        FullTextIndexes = false,
        Triggers = withChildCollections,
        Statistics = false,
        // ordering is worked out from the catalog, see SqlServerDdlQueries
        WithDependencies = false,
        // dbdeploy terminates every batch itself with a "GO" on its own line
        ScriptBatchTerminator = false,
        NoCommandTerminator = true,
        EnforceScriptingOptions = true,
    };

    public void Connect()
    {
        var stopwatch = Stopwatch.StartNew();

        _connection = new SqlConnection(_connectionString);
        _serverConnection = new ServerConnection(_connection);
        var server = new Server(_serverConnection);

        _logger.LogInformation("Connected to {Edition} {Version} on {Server}",
            server.Information.Edition, server.Information.VersionString, server.Name);

        // Without this SMO reads every property of every object with a query of its own, which is
        // the difference between seconds and hours on a database with thousands of objects.
        try
        {
            server.SetDefaultInitFields(true);
            _logger.LogDebug("Asked SMO to read all properties of an object in one go");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not switch SMO to bulk property loading; scripting will be slower");
        }

        _database = server.Databases[_databaseName]
                    ?? throw new InvalidOperationException(
                        $"Database {_databaseName} was not found on {server.Name} by the connected login");

        _logger.LogInformation("Scripting database {DatabaseName} at compatibility level {CompatibilityLevel} in {Elapsed}",
            _database.Name, _database.CompatibilityLevel, stopwatch.Elapsed);

        Prefetch();
    }

    /// <summary>
    /// Reads every property of every object of a type in a handful of set based queries, instead of
    /// leaving each object to fetch its own when it is scripted.
    /// </summary>
    /// <remarks>
    /// This is not a micro optimisation. Scripting one object at a time without it costs around
    /// fifteen round trips per object and gets worse as the database grows: measured against SQL
    /// Server 2022, a database of two thousand objects took three minutes and twenty nine thousand
    /// round trips, against eleven seconds and under six hundred round trips with the prefetch. The
    /// gap widens further with network latency between the tool and the server, which is the normal
    /// case.
    /// </remarks>
    private void Prefetch()
    {
        var database = _database!;
        var stopwatch = Stopwatch.StartNew();
        var prefetched = new List<string>();

        foreach (var type in PrefetchedTypes)
        {
            try
            {
                database.PrefetchObjects(type, PrefetchOptions);
                prefetched.Add(type.Name);
            }
            catch (Exception ex)
            {
                // Not every release of SMO can prefetch every type, and it is only an optimisation.
                _logger.LogWarning(ex, "SMO could not preload the {ObjectType} objects of {DatabaseName}. Performance while fetching objects will be degraded",
                    type.Name, _databaseName);
            }
        }

        _logger.LogInformation("Preloaded {TypeCount} object collections in {Elapsed}: {Types}",
            prefetched.Count, stopwatch.Elapsed, string.Join(", ", prefetched));
    }

    /// <summary>
    /// The batches that create the object, or an empty list when the object is no longer there.
    /// </summary>
    public IReadOnlyList<string> Script(SqlServerObject dbObject)
    {
        var scriptable = Resolve(dbObject);
        if (scriptable is null)
        {
            _logger.LogWarning("SMO does not know {ObjectKey}; it may have been dropped since the objects were listed",
                dbObject.Key);
            return [];
        }

        var batches = new List<string>();

        foreach (var batch in scriptable.Script(Options))
        {
            if (!string.IsNullOrWhiteSpace(batch))
                batches.Add(batch.Trim());
        }

        return batches;
    }

    private IScriptable? Resolve(SqlServerObject dbObject)
    {
        SmoDatabase database = _database ?? throw new InvalidOperationException("Connect must be called first");
        var schema = dbObject.Schema;
        var name = dbObject.Name;

        return dbObject.Type.Name switch
        {
            SqlServerObjectType.Schema => database.Schemas[name],
            SqlServerObjectType.PartitionFunction => database.PartitionFunctions[name],
            SqlServerObjectType.PartitionScheme => database.PartitionSchemes[name],
            SqlServerObjectType.XmlSchemaCollection => database.XmlSchemaCollections[name, schema],
            SqlServerObjectType.Type => database.UserDefinedDataTypes[name, schema],
            SqlServerObjectType.TableType => database.UserDefinedTableTypes[name, schema],
            SqlServerObjectType.Sequence => database.Sequences[name, schema],
            SqlServerObjectType.Table => database.Tables[name, schema],
            SqlServerObjectType.Synonym => database.Synonyms[name, schema],
            SqlServerObjectType.View => database.Views[name, schema],
            SqlServerObjectType.Function => database.UserDefinedFunctions[name, schema],
            SqlServerObjectType.Procedure => database.StoredProcedures[name, schema],
            SqlServerObjectType.Index => Parent(dbObject)?.Indexes[name],
            SqlServerObjectType.Trigger => Parent(dbObject)?.Triggers[name],
            SqlServerObjectType.ForeignKey => ParentTable(dbObject)?.ForeignKeys[name],
            _ => null,
        };
    }

    /// <summary>
    /// The table or the view an index or a trigger hangs off. Indexed views carry both, which is
    /// why the view collection is looked at too.
    /// </summary>
    private TableViewBase? Parent(SqlServerObject dbObject)
    {
        var database = _database!;
        if (dbObject.ParentName is not { } parentName)
            return null;

        return (TableViewBase?)database.Tables[parentName, dbObject.ParentSchema]
               ?? database.Views[parentName, dbObject.ParentSchema];
    }

    private Table? ParentTable(SqlServerObject dbObject)
        => dbObject.ParentName is { } parentName ? _database!.Tables[parentName, dbObject.ParentSchema] : null;

    public void Dispose()
    {
        try
        {
            _serverConnection?.Disconnect();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not close the connection SMO was using");
        }

        _connection?.Dispose();
    }
}
