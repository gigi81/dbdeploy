namespace Grillisoft.Tools.DatabaseDeploy.Oracle;

/// <summary>
/// Dictionary queries used to discover what has to be scripted.
/// </summary>
/// <remarks>
/// Everything reads from the <c>ALL_*</c> views so the tool only needs to see what the connected
/// user can see. Each query is kept separate rather than folded into one big statement so that a
/// view the user is not granted on degrades into a warning instead of taking the whole generation
/// down with it.
/// </remarks>
internal static class OracleDdlQueries
{
    private static readonly string SupportedTypes =
        string.Join(", ", OracleObjectType.QueryableNames.Select(t => $"'{t}'"));

    /// <summary>
    /// Objects to script. <c>GENERATED</c>/<c>SECONDARY</c> weed out the objects Oracle created on
    /// behalf of something else (constraint indexes, identity sequences, domain index storage) and
    /// <c>SUBOBJECT_NAME</c> weeds out partitions, which are part of their table's DDL.
    /// </summary>
    public static readonly string Objects = $"""
        SELECT
            o.object_name,
            o.object_type
        FROM
            all_objects o
        WHERE
            o.owner = :owner
            AND o.object_type IN ({SupportedTypes})
            AND o.generated = 'N'
            AND o.secondary = 'N'
            AND o.subobject_name IS NULL
            AND o.object_name NOT LIKE 'BIN$%'
            AND o.object_name NOT LIKE 'MLOG$%'
            AND o.object_name NOT LIKE 'RUPD$%'
            AND o.object_name NOT LIKE 'DR$%'
        ORDER BY
            o.object_type,
            o.object_name
        """;

    /// <summary>
    /// Object types present in the schema that this tool does not know how to script. Purely
    /// informational, but it is the first thing to look at when the generated script is incomplete.
    /// </summary>
    public static readonly string UnsupportedObjectTypes = $"""
        SELECT
            o.object_type,
            COUNT(*) AS object_count
        FROM
            all_objects o
        WHERE
            o.owner = :owner
            AND o.object_type NOT IN ({SupportedTypes})
            AND o.generated = 'N'
            AND o.secondary = 'N'
            AND o.subobject_name IS NULL
            AND o.object_name NOT LIKE 'BIN$%'
        GROUP BY
            o.object_type
        ORDER BY
            o.object_type
        """;

    /// <summary>
    /// Dependencies recorded by the server. Covers views, materialized views, synonyms, triggers and
    /// every PL/SQL unit, but says nothing about indexes or foreign keys.
    /// </summary>
    public const string Dependencies = """
        SELECT
            d.name,
            d.type,
            d.referenced_name,
            d.referenced_type
        FROM
            all_dependencies d
        WHERE
            d.owner = :owner
            AND d.referenced_owner = :owner
        ORDER BY
            d.name,
            d.referenced_name
        """;

    /// <summary>Index to table, so an index is never written before the table it sits on.</summary>
    public const string Indexes = """
        SELECT
            i.index_name,
            i.table_name,
            i.index_type
        FROM
            all_indexes i
        WHERE
            i.owner = :owner
            AND i.table_owner = :owner
        ORDER BY
            i.index_name
        """;

    /// <summary>
    /// Indexes that implement a primary or unique key. They come out with the table DDL, so
    /// scripting them again would fail with ORA-00955.
    /// </summary>
    public const string ConstraintIndexes = """
        SELECT
            c.index_name
        FROM
            all_constraints c
        WHERE
            c.owner = :owner
            AND c.constraint_type IN ('P', 'U')
            AND c.index_name IS NOT NULL
        """;

    /// <summary>Foreign keys, with the table they belong to and the table they point at.</summary>
    public const string ForeignKeys = """
        SELECT
            c.constraint_name,
            c.table_name,
            r.table_name AS referenced_table_name
        FROM
            all_constraints c
            INNER JOIN all_constraints r
                ON r.owner = c.r_owner
                AND r.constraint_name = c.r_constraint_name
        WHERE
            c.owner = :owner
            AND c.r_owner = :owner
            AND c.constraint_type = 'R'
        ORDER BY
            c.constraint_name
        """;

    /// <summary>Triggers and the table or view they are defined on.</summary>
    public const string Triggers = """
        SELECT
            t.trigger_name,
            t.table_name,
            t.base_object_type
        FROM
            all_triggers t
        WHERE
            t.owner = :owner
            AND t.table_owner = :owner
            AND t.base_object_type IN ('TABLE', 'VIEW')
        ORDER BY
            t.trigger_name
        """;

    /// <summary>Storage tables of nested table columns: part of their parent table's DDL.</summary>
    public const string NestedTables = """
        SELECT table_name FROM all_nested_tables WHERE owner = :owner
        """;

    /// <summary>Container tables of materialized views: part of the materialized view's DDL.</summary>
    public const string MaterializedViewTables = """
        SELECT mview_name FROM all_mviews WHERE owner = :owner
        """;

    /// <summary>Overflow and mapping segments of index organized tables.</summary>
    public const string IotSegments = """
        SELECT table_name FROM all_tables WHERE owner = :owner AND iot_type IS NOT NULL AND iot_type <> 'IOT'
        """;

    /// <summary>Advanced Queuing tables: created by DBMS_AQADM, not by DDL.</summary>
    public const string QueueTables = """
        SELECT queue_table FROM all_queue_tables WHERE owner = :owner
        """;

    /// <summary>Everything Oracle attached to the migrations table, which must stay out of the script.</summary>
    public const string MigrationTableObjects = """
        SELECT index_name AS object_name FROM all_indexes WHERE owner = :owner AND table_name = :migration_table
        UNION
        SELECT trigger_name FROM all_triggers WHERE owner = :owner AND table_name = :migration_table
        UNION
        SELECT constraint_name FROM all_constraints WHERE owner = :owner AND table_name = :migration_table
        """;

    public const string TableComments = """
        SELECT
            c.table_name,
            c.comments
        FROM
            all_tab_comments c
        WHERE
            c.owner = :owner
            AND c.comments IS NOT NULL
        ORDER BY
            c.table_name
        """;

    public const string ColumnComments = """
        SELECT
            c.table_name,
            c.column_name,
            c.comments
        FROM
            all_col_comments c
        WHERE
            c.owner = :owner
            AND c.comments IS NOT NULL
        ORDER BY
            c.table_name,
            c.column_name
        """;

    public const string ObjectDdl =
        "SELECT DBMS_METADATA.GET_DDL(:object_type, :object_name, :owner) FROM DUAL";

    /// <summary>
    /// Last resort for a program unit whose DDL <c>DBMS_METADATA</c> refused to hand over, which
    /// typically means the caller lacks SELECT_CATALOG_ROLE. <c>ALL_SOURCE</c> holds the text
    /// without the leading <c>CREATE OR REPLACE</c>, which is added back by the caller.
    /// </summary>
    public const string ObjectSource = """
        SELECT
            s.text
        FROM
            all_source s
        WHERE
            s.owner = :owner
            AND s.name = :object_name
            AND s.type = :object_type
        ORDER BY
            s.line
        """;

    /// <summary>Last resort for a view.</summary>
    public const string ViewSource = """
        SELECT
            v.text
        FROM
            all_views v
        WHERE
            v.owner = :owner
            AND v.view_name = :object_name
        """;

    /// <summary>Last resort for a trigger; the description already carries the whole header.</summary>
    public const string TriggerSource = """
        SELECT
            t.description,
            t.trigger_body
        FROM
            all_triggers t
        WHERE
            t.owner = :owner
            AND t.trigger_name = :object_name
        """;

    /// <summary>
    /// Session wide DDL formatting. The point of most of these is portability: a script that carries
    /// the tablespace names, storage clauses and schema names of the database it was taken from
    /// cannot be replayed anywhere else.
    /// </summary>
    public static readonly (string Name, bool Value)[] TransformParameters =
    [
        ("PRETTY", true),
        // dbdeploy terminates every statement itself with a "/" on its own line
        ("SQLTERMINATOR", false),
        ("SEGMENT_ATTRIBUTES", false),
        ("STORAGE", false),
        ("TABLESPACE", false),
        // check, primary and unique keys inline in the CREATE TABLE
        ("CONSTRAINTS", true),
        ("CONSTRAINTS_AS_ALTER", false),
        // foreign keys are scripted separately, after every table exists
        ("REF_CONSTRAINTS", false),
        // no owner prefix: the script is replayed into whatever schema is being deployed
        ("EMIT_SCHEMA", false),
        ("PARTITIONING", true),
        ("OID", false),
    ];
}
