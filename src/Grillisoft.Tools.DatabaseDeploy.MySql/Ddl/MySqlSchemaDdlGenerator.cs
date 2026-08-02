using System.Data.Common;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Database;
using Grillisoft.Tools.DatabaseDeploy.Database.Ddl;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.MySql.Ddl;

/// <summary>
/// Scripts a whole MySQL or MariaDB database into a single deployable file.
/// </summary>
/// <remarks>
/// <see cref="SchemaDdlGenerator"/> holds the ordering and the writing; what is MySQL's own is
/// <see cref="MySqlObjectsDiscovery"/>, which decides what to script and what depends on what, and
/// <see cref="MySqlObjectScripter"/>, which asks the server for the DDL of one object at a time.
/// </remarks>
internal sealed class MySqlSchemaDdlGenerator : SchemaDdlGenerator
{
    private readonly MySqlObjectsDiscovery _discovery;
    private readonly MySqlObjectScripter _scripter;

    public MySqlSchemaDdlGenerator(
        Func<string, DbCommand> createCommand,
        string database,
        string migrationTable,
        ILogger logger)
        : base(database, "database", logger)
    {
        var catalog = new CatalogReader(createCommand, database, logger);
        _discovery = new MySqlObjectsDiscovery(catalog, database, migrationTable, logger);
        _scripter = new MySqlObjectScripter(createCommand, database, logger);
    }

    protected override Func<string, int> RankOf => MySqlObjectType.RankOf;

    protected override DdlScriptWriter CreateWriter(StreamWriter stream) => new MySqlDdlScriptWriter(stream);

    protected override Task<(List<DbObject> Objects, List<(DbObject DbObject, DbObject DependsOn)> Dependencies)>
        Discover(CancellationToken cancellationToken)
        => _discovery.Discover(cancellationToken);

    protected override Task<IReadOnlyList<string>> Script(DbObject dbObject, CancellationToken cancellationToken)
        => _scripter.Script(dbObject, cancellationToken);

    protected override string Describe(Exception exception) => exception.Describe();

    protected override Exception CreateGenerationException(IEnumerable<(string Object, string Error)> failures)
        => new MySqlDdlGenerationException(Source, failures);
}
