namespace Grillisoft.Tools.DatabaseDeploy.SqlServer.Ddl;

/// <summary>
/// Catalog queries used to discover what has to be scripted and in which order.
/// </summary>
/// <remarks>
/// SMO knows how to write the DDL of a single object but nothing about the order the objects have
/// to be written in, so the ordering is worked out here, from the catalog views, and fed to the
/// dependency graph. Each query is kept separate rather than folded into one big statement so that
/// a view the connected login is not allowed to read degrades into a warning instead of taking the
/// whole generation down with it.
/// <para>
/// Every query filters out <c>is_ms_shipped</c> objects and the <c>sysdiagrams</c> table, which SQL
/// Server Management Studio creates in the user's database but marks as its own through an extended
/// property.
/// </para>
/// </remarks>
internal static class SqlServerDdlQueries
{
    private static readonly string SysTypes =
        string.Join(", ", SqlServerObjectType.QueryableSysTypes.Select(t => $"'{t}'"));

    private const string NotADiagramObject = """
            AND NOT EXISTS (
                SELECT 1 FROM sys.extended_properties ep
                WHERE ep.class = 1 AND ep.major_id = o.object_id AND ep.minor_id = 0
                  AND ep.name = N'microsoft_database_tools_support')
        """;

    /// <summary>
    /// Schemas the user created. The built in ones already exist in every database, and the ones
    /// backing a fixed database role would fail to create.
    /// </summary>
    public const string Schemas = """
        SELECT
            s.name
        FROM
            sys.schemas s
            INNER JOIN sys.database_principals p ON p.principal_id = s.principal_id
        WHERE
            s.name NOT IN ('dbo', 'guest', 'sys', 'INFORMATION_SCHEMA')
            AND s.schema_id < 16384
            AND p.is_fixed_role = 0
        ORDER BY
            s.name
        """;

    /// <summary>Tables, views, procedures, functions, synonyms and sequences.</summary>
    public static readonly string Objects = $"""
        SELECT
            s.name AS schema_name,
            o.name AS object_name,
            o.type AS sys_type
        FROM
            sys.objects o
            INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
        WHERE
            o.is_ms_shipped = 0
            AND o.type IN ({SysTypes})
            {NotADiagramObject}
        ORDER BY
            o.type,
            s.name,
            o.name
        """;

    /// <summary>
    /// Object types present in the database that this tool does not know how to script - CLR
    /// modules, service broker queues, and anything a future release of SQL Server adds. Purely
    /// informational, but it is the first thing to look at when the generated script is incomplete.
    /// </summary>
    /// <remarks>
    /// The types excluded here are the ones that are scripted, just not from <c>sys.objects</c>:
    /// constraints come out with their table, triggers and table types have a query of their own,
    /// and an internal table is not a user object at all.
    /// </remarks>
    public static readonly string UnsupportedObjectTypes = $"""
        SELECT
            o.type_desc,
            COUNT(*) AS object_count
        FROM
            sys.objects o
        WHERE
            o.is_ms_shipped = 0
            AND o.type NOT IN ({SysTypes})
            AND o.type NOT IN ('C', 'D', 'F', 'PK', 'UQ', 'TR', 'TT', 'IT')
            {NotADiagramObject}
        GROUP BY
            o.type_desc
        ORDER BY
            o.type_desc
        """;

    /// <summary>User defined alias types and table types.</summary>
    public const string Types = """
        SELECT
            s.name AS schema_name,
            t.name AS type_name,
            t.is_table_type
        FROM
            sys.types t
            INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
        WHERE
            t.is_user_defined = 1
            AND t.is_assembly_type = 0
        ORDER BY
            s.name,
            t.name
        """;

    public const string XmlSchemaCollections = """
        SELECT
            s.name AS schema_name,
            x.name AS collection_name
        FROM
            sys.xml_schema_collections x
            INNER JOIN sys.schemas s ON s.schema_id = x.schema_id
        WHERE
            s.name <> 'sys'
        ORDER BY
            s.name,
            x.name
        """;

    public const string PartitionFunctions = """
        SELECT name FROM sys.partition_functions WHERE is_system = 0 ORDER BY name
        """;

    /// <summary>Partition schemes, with the function each one is built on.</summary>
    public const string PartitionSchemes = """
        SELECT
            ps.name AS scheme_name,
            pf.name AS function_name
        FROM
            sys.partition_schemes ps
            INNER JOIN sys.partition_functions pf ON pf.function_id = ps.function_id
        ORDER BY
            ps.name
        """;

    /// <summary>
    /// Indexes to script on their own. The ones implementing a primary or a unique key come out
    /// with the <c>CREATE TABLE</c> and would fail a second time, heaps have nothing to create, and
    /// a hypothetical index is a leftover of the tuning advisor.
    /// </summary>
    public static readonly string Indexes = $"""
        SELECT
            s.name AS schema_name,
            o.name AS table_name,
            i.name AS index_name,
            i.type_desc
        FROM
            sys.indexes i
            INNER JOIN sys.objects o ON o.object_id = i.object_id
            INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
        WHERE
            o.is_ms_shipped = 0
            AND o.type IN ('U', 'V')
            AND i.name IS NOT NULL
            AND i.type <> 0
            AND i.is_primary_key = 0
            AND i.is_unique_constraint = 0
            AND i.is_hypothetical = 0
            {NotADiagramObject}
        ORDER BY
            s.name,
            o.name,
            i.index_id
        """;

    /// <summary>
    /// A secondary XML index can only be created once the primary one it extends exists, and the
    /// catalog is the only place that pairing is recorded.
    /// </summary>
    public const string XmlIndexDependencies = """
        SELECT
            s.name AS schema_name,
            o.name AS table_name,
            i.name AS index_name,
            p.name AS primary_index_name
        FROM
            sys.xml_indexes i
            INNER JOIN sys.objects o ON o.object_id = i.object_id
            INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
            INNER JOIN sys.xml_indexes p ON p.object_id = i.object_id AND p.index_id = i.using_xml_index_id
        WHERE
            i.using_xml_index_id IS NOT NULL
        """;

    /// <summary>Foreign keys, with the table they belong to and the table they point at.</summary>
    public const string ForeignKeys = """
        SELECT
            ps.name AS parent_schema,
            p.name AS parent_name,
            fk.name AS constraint_name,
            rs.name AS referenced_schema,
            r.name AS referenced_name
        FROM
            sys.foreign_keys fk
            INNER JOIN sys.objects p ON p.object_id = fk.parent_object_id
            INNER JOIN sys.schemas ps ON ps.schema_id = p.schema_id
            INNER JOIN sys.objects r ON r.object_id = fk.referenced_object_id
            INNER JOIN sys.schemas rs ON rs.schema_id = r.schema_id
        WHERE
            fk.is_ms_shipped = 0
        ORDER BY
            ps.name,
            p.name,
            fk.name
        """;

    /// <summary>DML triggers, with the table or view they are defined on.</summary>
    public const string Triggers = """
        SELECT
            ps.name AS parent_schema,
            p.name AS parent_name,
            t.name AS trigger_name
        FROM
            sys.triggers t
            INNER JOIN sys.objects p ON p.object_id = t.parent_id
            INNER JOIN sys.schemas ps ON ps.schema_id = p.schema_id
        WHERE
            t.is_ms_shipped = 0
            AND t.parent_class = 1
            AND t.type = 'TR'
        ORDER BY
            ps.name,
            p.name,
            t.name
        """;

    /// <summary>
    /// Everything a module refers to by name: what a view selects from, what a procedure touches,
    /// what a computed column or a check constraint calls.
    /// </summary>
    /// <remarks>
    /// A default or a check constraint is not scripted on its own - it comes out with its table -
    /// so a dependency declared by one is attributed to the table that carries it. Triggers are the
    /// opposite case: they have a parent too, but they are scripted separately and keep their own
    /// dependencies.
    /// <para>
    /// <c>referenced_id</c> is null when SQL Server cannot resolve the name, which happens for
    /// cross database references and for a module written against an object that no longer exists.
    /// Neither is something this tool can order, so both are dropped.
    /// </para>
    /// </remarks>
    public const string ObjectDependencies = """
        SELECT
            rs.name AS referencing_schema,
            ro.name AS referencing_name,
            ro.type AS referencing_type,
            ds.name AS referenced_schema,
            dep.name AS referenced_name,
            dep.type AS referenced_type
        FROM
            sys.sql_expression_dependencies d
            INNER JOIN sys.objects src ON src.object_id = d.referencing_id
            INNER JOIN sys.objects ro
                ON ro.object_id = CASE WHEN src.type IN ('C', 'D') THEN src.parent_object_id ELSE src.object_id END
            INNER JOIN sys.schemas rs ON rs.schema_id = ro.schema_id
            INNER JOIN sys.objects dep ON dep.object_id = d.referenced_id
            INNER JOIN sys.schemas ds ON ds.schema_id = dep.schema_id
        WHERE
            d.referencing_class = 1
            AND d.referenced_class = 1
            AND d.referenced_id IS NOT NULL
            AND ro.is_ms_shipped = 0
            AND dep.is_ms_shipped = 0
            AND ro.object_id <> dep.object_id
        """;

    /// <summary>
    /// Tables, views and modules using a user defined type, which the catalog does not record as an
    /// expression dependency.
    /// </summary>
    public const string TypeDependencies = """
        SELECT
            s.name AS object_schema,
            o.name AS object_name,
            o.type AS object_type,
            ts.name AS type_schema,
            t.name AS type_name,
            t.is_table_type
        FROM
            sys.columns c
            INNER JOIN sys.objects o ON o.object_id = c.object_id
            INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
            INNER JOIN sys.types t ON t.user_type_id = c.user_type_id
            INNER JOIN sys.schemas ts ON ts.schema_id = t.schema_id
        WHERE
            t.is_user_defined = 1
            AND o.is_ms_shipped = 0
        UNION
        SELECT
            s.name,
            o.name,
            o.type,
            ts.name,
            t.name,
            t.is_table_type
        FROM
            sys.parameters p
            INNER JOIN sys.objects o ON o.object_id = p.object_id
            INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
            INNER JOIN sys.types t ON t.user_type_id = p.user_type_id
            INNER JOIN sys.schemas ts ON ts.schema_id = t.schema_id
        WHERE
            t.is_user_defined = 1
            AND o.is_ms_shipped = 0
        """;

    /// <summary>Columns typed as XML validated against a schema collection.</summary>
    public const string XmlSchemaCollectionDependencies = """
        SELECT
            s.name AS object_schema,
            o.name AS object_name,
            o.type AS object_type,
            xs.name AS collection_schema,
            x.name AS collection_name
        FROM
            sys.columns c
            INNER JOIN sys.objects o ON o.object_id = c.object_id
            INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
            INNER JOIN sys.xml_schema_collections x ON x.xml_collection_id = c.xml_collection_id
            INNER JOIN sys.schemas xs ON xs.schema_id = x.schema_id
        WHERE
            c.xml_collection_id <> 0
            AND o.is_ms_shipped = 0
        """;

    /// <summary>
    /// Filegroups other than the default one and partition schemes actually in use. The generated
    /// script cannot create a filegroup - that needs physical files - so their presence is worth a
    /// warning.
    /// </summary>
    public const string NonDefaultStorage = """
        SELECT
            ds.name,
            ds.type_desc
        FROM
            sys.data_spaces ds
        WHERE
            ds.is_default = 0
            AND EXISTS (SELECT 1 FROM sys.indexes i
                        INNER JOIN sys.objects o ON o.object_id = i.object_id
                        WHERE i.data_space_id = ds.data_space_id AND o.is_ms_shipped = 0)
        ORDER BY
            ds.name
        """;

    /// <summary>Everything that hangs off the migrations table and must stay out of the script.</summary>
    public const string MigrationTableObjects = """
        SELECT
            o.name
        FROM
            sys.objects o
        WHERE
            o.parent_object_id = OBJECT_ID(@migration_table)
        UNION
        SELECT
            i.name
        FROM
            sys.indexes i
        WHERE
            i.object_id = OBJECT_ID(@migration_table)
            AND i.name IS NOT NULL
        """;
}
