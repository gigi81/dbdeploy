namespace Grillisoft.Tools.DatabaseDeploy.MySql.Tests.Ddl;

/// <summary>
/// The same round trip against MariaDB, which the provider serves without distinguishing it from
/// MySQL and which the integration job in CI actually runs against.
/// </summary>
/// <remarks>
/// Three things differ here and all three are the generator's problem: MariaDB has sequences, and
/// reports them in <c>information_schema.TABLES</c> alongside the tables, so a discovery that asks
/// for "everything that is not a view" scripts one as a table; and it has neither
/// <c>VIEW_TABLE_USAGE</c> nor <c>VIEW_ROUTINE_USAGE</c>, so the ordering of the two views in the
/// fixture comes from the fallback that reads the view definitions instead.
/// </remarks>
[ClassDataSource<MariaDbFixture>(Shared = SharedType.PerAssembly)]
[InheritsTests]
public class MariaDbSchemaDdlTests : MySqlSchemaDdlTestsBase
{
    public MariaDbSchemaDdlTests(MariaDbFixture fixture)
        : base(fixture)
    {
    }

    protected override IEnumerable<string> EngineSpecificSchema =>
    [
        "CREATE SEQUENCE order_seq START WITH 100 INCREMENT BY 1",
    ];
}
