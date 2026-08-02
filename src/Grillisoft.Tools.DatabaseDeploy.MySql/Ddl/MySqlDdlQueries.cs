namespace Grillisoft.Tools.DatabaseDeploy.MySql.Ddl;

/// <summary>
/// The <c>information_schema</c> reads a generation plans itself from.
/// </summary>
/// <remarks>
/// Every one of these filters on <c>DATABASE()</c> rather than on a name passed in, so the script
/// comes out unqualified and replays into a database of any name. Anything MariaDB does not have -
/// <c>VIEW_TABLE_USAGE</c>, <c>VIEW_ROUTINE_USAGE</c> - is read through
/// <see cref="Database.CatalogReader.TryQuery{T}"/> and worked around rather than required.
/// </remarks>
internal static class MySqlDdlQueries
{
    /// <summary>
    /// Tables, matched positively on <c>BASE TABLE</c>. MariaDB lists its sequences in this same
    /// view with <c>TABLE_TYPE = 'SEQUENCE'</c>, so "everything that is not a view" would script a
    /// sequence as a table.
    /// </summary>
    public const string Tables = """
        SELECT TABLE_NAME
        FROM information_schema.TABLES
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE'
        ORDER BY TABLE_NAME
        """;

    /// <summary>MariaDB only; returns nothing on MySQL, which has no sequences.</summary>
    public const string Sequences = """
        SELECT TABLE_NAME
        FROM information_schema.TABLES
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'SEQUENCE'
        ORDER BY TABLE_NAME
        """;

    public const string Views = """
        SELECT TABLE_NAME
        FROM information_schema.VIEWS
        WHERE TABLE_SCHEMA = DATABASE()
        ORDER BY TABLE_NAME
        """;

    /// <summary>
    /// Routines. MariaDB also reports <c>PACKAGE</c> and <c>PACKAGE BODY</c> here; a
    /// <c>ROUTINE_TYPE</c> that maps to no known type is logged rather than fatal.
    /// </summary>
    public const string Routines = """
        SELECT ROUTINE_NAME, ROUTINE_TYPE
        FROM information_schema.ROUTINES
        WHERE ROUTINE_SCHEMA = DATABASE()
        ORDER BY ROUTINE_TYPE, ROUTINE_NAME
        """;

    public const string Triggers = """
        SELECT TRIGGER_NAME, EVENT_OBJECT_TABLE
        FROM information_schema.TRIGGERS
        WHERE TRIGGER_SCHEMA = DATABASE()
        ORDER BY TRIGGER_NAME
        """;

    public const string Events = """
        SELECT EVENT_NAME
        FROM information_schema.EVENTS
        WHERE EVENT_SCHEMA = DATABASE()
        ORDER BY EVENT_NAME
        """;

    /// <summary>
    /// The foreign keys and the two tables each one needs. Both ends are read because a key can
    /// only be created once the table it points at exists as well as the one carrying it.
    /// </summary>
    public const string ForeignKeys = """
        SELECT CONSTRAINT_NAME, TABLE_NAME, REFERENCED_TABLE_NAME
        FROM information_schema.REFERENTIAL_CONSTRAINTS
        WHERE CONSTRAINT_SCHEMA = DATABASE()
        ORDER BY TABLE_NAME, CONSTRAINT_NAME
        """;

    /// <summary>Which tables and views a view is built on. MySQL 8 only.</summary>
    public const string ViewTableUsage = """
        SELECT VIEW_NAME, TABLE_NAME
        FROM information_schema.VIEW_TABLE_USAGE
        WHERE VIEW_SCHEMA = DATABASE() AND TABLE_SCHEMA = DATABASE()
        """;

    /// <summary>Which routines a view calls. MySQL 8 only.</summary>
    public const string ViewRoutineUsage = """
        SELECT TABLE_NAME, SPECIFIC_NAME
        FROM information_schema.VIEW_ROUTINE_USAGE
        WHERE TABLE_SCHEMA = DATABASE() AND SPECIFIC_SCHEMA = DATABASE()
        """;

    /// <summary>
    /// The text of every view, for the servers with no <c>VIEW_TABLE_USAGE</c> to read the
    /// dependencies out of.
    /// </summary>
    public const string ViewDefinitions = """
        SELECT TABLE_NAME, VIEW_DEFINITION
        FROM information_schema.VIEWS
        WHERE TABLE_SCHEMA = DATABASE()
        """;

    /// <summary>
    /// What belongs to the migrations table and must not reach the script: the keys pointing at it
    /// or hanging off it, and any trigger on it.
    /// </summary>
    public const string MigrationTableObjects = """
        SELECT CONSTRAINT_NAME
        FROM information_schema.REFERENTIAL_CONSTRAINTS
        WHERE CONSTRAINT_SCHEMA = DATABASE()
          AND (TABLE_NAME = @migration_table OR REFERENCED_TABLE_NAME = @migration_table)
        UNION
        SELECT TRIGGER_NAME
        FROM information_schema.TRIGGERS
        WHERE TRIGGER_SCHEMA = DATABASE() AND EVENT_OBJECT_TABLE = @migration_table
        """;

    /// <summary>
    /// Object types living in the database that this tool does not script. Not an error, but the
    /// first thing to check when a deployment of the generated script comes up short.
    /// </summary>
    public const string UnsupportedObjectTypes = """
        SELECT ROUTINE_TYPE, COUNT(*)
        FROM information_schema.ROUTINES
        WHERE ROUTINE_SCHEMA = DATABASE()
          AND ROUTINE_TYPE NOT IN ('FUNCTION', 'PROCEDURE', 'PACKAGE', 'PACKAGE BODY')
        GROUP BY ROUTINE_TYPE
        """;
}
