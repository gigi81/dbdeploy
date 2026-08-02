namespace Grillisoft.Tools.DatabaseDeploy.MySql.Tests.Ddl;

/// <summary>
/// The round trip against MySQL. Shares <see cref="MySqlFixture"/> with
/// <see cref="MySqlDatabaseTests"/> - one container for both - so the migration cases it inherits
/// run a second time against the same database rather than against a second one. They are safe to
/// repeat: every case starts by clearing and re-creating the migrations table, and
/// <c>[NotInParallel]</c> keeps them off each other.
/// </summary>
[ClassDataSource<MySqlFixture>(Shared = SharedType.PerAssembly)]
[InheritsTests]
public class MySqlSchemaDdlTests : MySqlSchemaDdlTestsBase
{
    public MySqlSchemaDdlTests(MySqlFixture fixture)
        : base(fixture)
    {
    }
}
