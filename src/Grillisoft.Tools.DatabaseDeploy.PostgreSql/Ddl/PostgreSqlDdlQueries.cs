namespace Grillisoft.Tools.DatabaseDeploy.PostgreSql.Ddl;

/// <summary>
/// The <c>pg_catalog</c> reads a generation plans itself from, and the ones it builds statements
/// out of.
/// </summary>
/// <remarks>
/// Two filters run through nearly every query here and are written out in each rather than applied
/// afterwards, so that a query failing on a locked down server costs only what that query knew:
/// the schema must not be a system one, and the object must not belong to an extension.
/// <c>CREATE EXTENSION</c> brings its own objects, and scripting them produces statements that
/// either fail or shadow the extension.
/// <para>
/// The kind columns - <c>relkind</c>, <c>typtype</c>, <c>prokind</c>, <c>contype</c> - are all of
/// the internal <c>"char"</c> type, which is not <c>text</c> and which Npgsql refuses to hand back
/// as a string. They are cast here rather than read specially, so that a query reads the same way
/// as the value it produces.
/// </para>
/// <para>
/// The <c>@oid</c> parameter is bound as a <c>bigint</c> and cast in the statement, for the mirror
/// image of the same reason: Npgsql will not send a <see cref="uint"/> without being told the type
/// it is, and teaching the shared <see cref="Database.CatalogReader"/> about
/// PostgreSQL's type mapping would be a worse trade than five characters of SQL.
/// </para>
/// </remarks>
internal static class PostgreSqlDdlQueries
{
    /// <summary>
    /// Empties the search path for the session doing the generation.
    /// </summary>
    /// <remarks>
    /// This is the single most important statement in the whole PostgreSQL generator.
    /// <c>format_type</c> and every <c>pg_get_..._def</c> function leave the schema off any name
    /// the session's search path already reaches, so generated with the default path the script is
    /// full of bare <c>customer</c> and <c>mood</c> and only replays into a session whose path
    /// happens to match. It is what <c>pg_dump</c> does first, for the same reason.
    /// </remarks>
    public const string ClearSearchPath = "SELECT pg_catalog.set_config('search_path', '', false)";

    public const string ShowSearchPath = "SHOW search_path";

    public const string Schemas = """
        SELECT n.oid, n.nspname
        FROM pg_catalog.pg_namespace n
        WHERE n.nspname <> 'information_schema'
          AND n.nspname NOT LIKE 'pg\_%'
          AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_depend e
                          WHERE e.classid = 'pg_namespace'::regclass AND e.objid = n.oid AND e.deptype = 'e')
        ORDER BY n.nspname
        """;

    /// <summary>
    /// Tables, views, materialized views, sequences and indexes.
    /// </summary>
    /// <remarks>
    /// The three exclusions are the counterpart of Oracle's "tables that only store something
    /// else": the sequence behind an identity column is created by the column, the index behind a
    /// constraint is created by the constraint, and a partition of a partitioned index is created
    /// with its parent. Scripting any of them produces a statement that fails.
    /// </remarks>
    public const string Relations = """
        SELECT c.oid, n.nspname, c.relname, c.relkind::text, c.relpersistence::text, c.relispartition,
               COALESCE(array_to_string(c.reloptions, ', '), '') AS reloptions,
               COALESCE(pg_catalog.pg_get_expr(c.relpartbound, c.oid), '') AS partbound,
               COALESCE(pg_catalog.pg_get_partkeydef(c.oid), '') AS partkeydef
        FROM pg_catalog.pg_class c
        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relkind IN ('r', 'p', 'v', 'm', 'S', 'i', 'I')
          AND n.nspname <> 'information_schema'
          AND n.nspname NOT LIKE 'pg\_%'
          AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_depend e
                          WHERE e.classid = 'pg_class'::regclass AND e.objid = c.oid AND e.deptype = 'e')
          AND NOT (c.relkind = 'S' AND EXISTS (
                     SELECT 1 FROM pg_catalog.pg_depend d
                     WHERE d.classid = 'pg_class'::regclass AND d.objid = c.oid AND d.deptype = 'i'))
          AND NOT (c.relkind IN ('i', 'I') AND EXISTS (
                     SELECT 1 FROM pg_catalog.pg_constraint k WHERE k.conindid = c.oid))
          AND NOT (c.relkind IN ('i', 'I') AND EXISTS (
                     SELECT 1 FROM pg_catalog.pg_inherits h WHERE h.inhrelid = c.oid))
        ORDER BY n.nspname, c.relname
        """;

    /// <summary>
    /// Enums, composites, ranges and domains. The array type every type carries, and the row type
    /// every table carries, are not objects of their own and are excluded.
    /// </summary>
    public const string Types = """
        SELECT t.oid, n.nspname, t.typname, t.typtype::text
        FROM pg_catalog.pg_type t
        JOIN pg_catalog.pg_namespace n ON n.oid = t.typnamespace
        WHERE t.typtype IN ('e', 'c', 'd', 'r')
          AND n.nspname <> 'information_schema'
          AND n.nspname NOT LIKE 'pg\_%'
          AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_depend e
                          WHERE e.classid = 'pg_type'::regclass AND e.objid = t.oid AND e.deptype = 'e')
          AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class c
                          WHERE c.oid = t.typrelid AND c.relkind <> 'c')
          AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_type el
                          WHERE el.oid = t.typelem AND el.typarray = t.oid)
        ORDER BY n.nspname, t.typname
        """;

    /// <summary>
    /// Functions, procedures and aggregates. The identity arguments are part of the name: a
    /// PostgreSQL routine is only unique together with them.
    /// </summary>
    public const string Routines = """
        SELECT p.oid, n.nspname, p.proname, p.prokind::text,
               pg_catalog.pg_get_function_identity_arguments(p.oid) AS args
        FROM pg_catalog.pg_proc p
        JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
        WHERE n.nspname <> 'information_schema'
          AND n.nspname NOT LIKE 'pg\_%'
          AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_depend e
                          WHERE e.classid = 'pg_proc'::regclass AND e.objid = p.oid AND e.deptype = 'e')
        ORDER BY n.nspname, p.proname
        """;

    /// <summary>
    /// Primary keys, unique and check and exclusion constraints, and foreign keys. Anything a
    /// partition or a child table inherited comes with its parent, hence <c>conislocal</c>.
    /// </summary>
    public const string Constraints = """
        SELECT k.oid, n.nspname, c.relname, k.conname, k.contype::text,
               COALESCE(fn.nspname, '') AS refschema, COALESCE(fc.relname, '') AS reftable
        FROM pg_catalog.pg_constraint k
        JOIN pg_catalog.pg_class c ON c.oid = k.conrelid
        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
        LEFT JOIN pg_catalog.pg_class fc ON fc.oid = k.confrelid
        LEFT JOIN pg_catalog.pg_namespace fn ON fn.oid = fc.relnamespace
        WHERE k.contype IN ('p', 'u', 'c', 'x', 'f')
          AND k.conislocal AND k.coninhcount = 0
          AND n.nspname <> 'information_schema'
          AND n.nspname NOT LIKE 'pg\_%'
        ORDER BY n.nspname, c.relname, k.conname
        """;

    /// <summary>
    /// Triggers, and the function each one calls. Constraint triggers the server creates for a
    /// foreign key are internal and come with the constraint.
    /// </summary>
    public const string Triggers = """
        SELECT tg.oid, n.nspname, c.relname, tg.tgname,
               fn.nspname AS fnschema, p.proname,
               pg_catalog.pg_get_function_identity_arguments(p.oid) AS fnargs
        FROM pg_catalog.pg_trigger tg
        JOIN pg_catalog.pg_class c ON c.oid = tg.tgrelid
        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
        JOIN pg_catalog.pg_proc p ON p.oid = tg.tgfoid
        JOIN pg_catalog.pg_namespace fn ON fn.oid = p.pronamespace
        WHERE NOT tg.tgisinternal
          AND n.nspname <> 'information_schema'
          AND n.nspname NOT LIKE 'pg\_%'
        ORDER BY n.nspname, c.relname, tg.tgname
        """;

    /// <summary>Rules other than the one every view is made of.</summary>
    public const string Rules = """
        SELECT r.oid, n.nspname, c.relname, r.rulename
        FROM pg_catalog.pg_rewrite r
        JOIN pg_catalog.pg_class c ON c.oid = r.ev_class
        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
        WHERE r.rulename <> '_RETURN'
          AND n.nspname <> 'information_schema'
          AND n.nspname NOT LIKE 'pg\_%'
        ORDER BY n.nspname, c.relname, r.rulename
        """;

    /// <summary>What a view or materialized view is built on. This is what orders a view on a view.</summary>
    public const string ViewDependencies = """
        SELECT DISTINCT dn.nspname, dc.relname, dc.relkind::text, rn.nspname, rc.relname, rc.relkind::text
        FROM pg_catalog.pg_depend d
        JOIN pg_catalog.pg_rewrite r ON r.oid = d.objid AND d.classid = 'pg_rewrite'::regclass
        JOIN pg_catalog.pg_class dc ON dc.oid = r.ev_class
        JOIN pg_catalog.pg_namespace dn ON dn.oid = dc.relnamespace
        JOIN pg_catalog.pg_class rc ON rc.oid = d.refobjid AND d.refclassid = 'pg_class'::regclass
        JOIN pg_catalog.pg_namespace rn ON rn.oid = rc.relnamespace
        WHERE dc.oid <> rc.oid AND dc.relkind IN ('v', 'm')
        """;

    /// <summary>The routines a view calls, so a view using a function comes out after it.</summary>
    public const string ViewRoutineDependencies = """
        SELECT DISTINCT dn.nspname, dc.relname, dc.relkind::text, rn.nspname, p.proname,
               pg_catalog.pg_get_function_identity_arguments(p.oid) AS args, p.prokind::text
        FROM pg_catalog.pg_depend d
        JOIN pg_catalog.pg_rewrite r ON r.oid = d.objid AND d.classid = 'pg_rewrite'::regclass
        JOIN pg_catalog.pg_class dc ON dc.oid = r.ev_class
        JOIN pg_catalog.pg_namespace dn ON dn.oid = dc.relnamespace
        JOIN pg_catalog.pg_proc p ON p.oid = d.refobjid AND d.refclassid = 'pg_proc'::regclass
        JOIN pg_catalog.pg_namespace rn ON rn.oid = p.pronamespace
        WHERE dc.relkind IN ('v', 'm')
        """;

    /// <summary>
    /// The user defined type of any column, so an enum or a domain is created before the table
    /// using it. The join through <c>typelem</c> is what catches a column of an array of an enum.
    /// </summary>
    public const string ColumnTypeDependencies = """
        SELECT DISTINCT n.nspname, c.relname, c.relkind::text, tn.nspname, bt.typname, bt.typtype::text
        FROM pg_catalog.pg_attribute a
        JOIN pg_catalog.pg_class c ON c.oid = a.attrelid
        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
        JOIN pg_catalog.pg_type t ON t.oid = a.atttypid
        JOIN pg_catalog.pg_type bt ON bt.oid = COALESCE(NULLIF(t.typelem, 0), t.oid)
        JOIN pg_catalog.pg_namespace tn ON tn.oid = bt.typnamespace
        WHERE a.attnum > 0 AND NOT a.attisdropped
          AND bt.typtype IN ('e', 'c', 'd', 'r')
          AND c.relkind IN ('r', 'p', 'v', 'm')
          AND tn.nspname <> 'information_schema'
          AND tn.nspname NOT LIKE 'pg\_%'
        """;

    /// <summary>
    /// The types a routine's signature names.
    /// </summary>
    /// <remarks>
    /// <c>typrelid</c> is what makes this worth a query of its own: a function declared
    /// <c>RETURNS SETOF customer</c> depends on the row type of the customer table, which is the
    /// table, not on a type object that is being scripted.
    /// </remarks>
    public const string RoutineTypeDependencies = """
        SELECT DISTINCT n.nspname, p.proname, pg_catalog.pg_get_function_identity_arguments(p.oid) AS args,
               p.prokind::text, tn.nspname, t.typname, t.typtype::text,
               COALESCE(rc.relname, '') AS relname, COALESCE(rc.relkind::text, ' ') AS relkind,
               COALESCE(rn.nspname, '') AS relschema
        FROM pg_catalog.pg_depend d
        JOIN pg_catalog.pg_proc p ON p.oid = d.objid AND d.classid = 'pg_proc'::regclass
        JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
        JOIN pg_catalog.pg_type t ON t.oid = d.refobjid AND d.refclassid = 'pg_type'::regclass
        JOIN pg_catalog.pg_namespace tn ON tn.oid = t.typnamespace
        LEFT JOIN pg_catalog.pg_class rc ON rc.oid = t.typrelid AND rc.relkind <> 'c'
        LEFT JOIN pg_catalog.pg_namespace rn ON rn.oid = rc.relnamespace
        WHERE d.deptype = 'n'
          AND tn.nspname <> 'information_schema'
          AND tn.nspname NOT LIKE 'pg\_%'
        """;

    /// <summary>
    /// The sequence a <c>serial</c> column's default calls, which has to exist before the table.
    /// </summary>
    public const string ColumnSequenceDependencies = """
        SELECT DISTINCT n.nspname, c.relname, sn.nspname, s.relname
        FROM pg_catalog.pg_depend d
        JOIN pg_catalog.pg_attrdef ad ON ad.oid = d.objid AND d.classid = 'pg_attrdef'::regclass
        JOIN pg_catalog.pg_class c ON c.oid = ad.adrelid
        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
        JOIN pg_catalog.pg_class s ON s.oid = d.refobjid AND s.relkind = 'S'
        JOIN pg_catalog.pg_namespace sn ON sn.oid = s.relnamespace
        WHERE d.refclassid = 'pg_class'::regclass
        """;

    /// <summary>
    /// The column a sequence is owned by. Only <c>serial</c> shows up here: an identity sequence is
    /// tied to its column with <c>deptype = 'i'</c> and is not scripted at all.
    /// </summary>
    public const string SequenceOwners = """
        SELECT sn.nspname, s.relname, n.nspname, c.relname, a.attname
        FROM pg_catalog.pg_depend d
        JOIN pg_catalog.pg_class s ON s.oid = d.objid AND d.classid = 'pg_class'::regclass AND s.relkind = 'S'
        JOIN pg_catalog.pg_namespace sn ON sn.oid = s.relnamespace
        JOIN pg_catalog.pg_class c ON c.oid = d.refobjid AND d.refclassid = 'pg_class'::regclass
        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
        JOIN pg_catalog.pg_attribute a ON a.attrelid = c.oid AND a.attnum = d.refobjsubid
        WHERE d.deptype = 'a'
          AND sn.nspname <> 'information_schema'
          AND sn.nspname NOT LIKE 'pg\_%'
        """;

    /// <summary>Which table each partition belongs to, and the bound that attaches it.</summary>
    public const string Partitions = """
        SELECT cn.nspname, c.relname, pn.nspname, p.relname,
               pg_catalog.pg_get_expr(c.relpartbound, c.oid) AS bound
        FROM pg_catalog.pg_inherits h
        JOIN pg_catalog.pg_class c ON c.oid = h.inhrelid
        JOIN pg_catalog.pg_namespace cn ON cn.oid = c.relnamespace
        JOIN pg_catalog.pg_class p ON p.oid = h.inhparent
        JOIN pg_catalog.pg_namespace pn ON pn.oid = p.relnamespace
        WHERE c.relispartition AND c.relkind IN ('r', 'p')
        """;

    /// <summary>The tables a table inherits from, which is not the same thing as partitioning.</summary>
    public const string Inheritance = """
        SELECT cn.nspname, c.relname, pn.nspname, p.relname, h.inhseqno
        FROM pg_catalog.pg_inherits h
        JOIN pg_catalog.pg_class c ON c.oid = h.inhrelid
        JOIN pg_catalog.pg_namespace cn ON cn.oid = c.relnamespace
        JOIN pg_catalog.pg_class p ON p.oid = h.inhparent
        JOIN pg_catalog.pg_namespace pn ON pn.oid = p.relnamespace
        WHERE NOT c.relispartition AND c.relkind IN ('r', 'p')
        ORDER BY h.inhseqno
        """;

    /// <summary>
    /// Every comment in the database, already qualified and quoted by the server.
    /// </summary>
    /// <remarks>
    /// <c>pg_identify_object</c> is what makes one query enough: it hands back an identity that can
    /// be written straight into a statement, and a type name that is the <c>COMMENT ON</c> keyword
    /// with no mapping needed.
    /// </remarks>
    public const string Comments = """
        SELECT o.type, o.identity, d.description, COALESCE(o.schema, '') AS schema
        FROM pg_catalog.pg_description d
        CROSS JOIN LATERAL pg_catalog.pg_identify_object(d.classoid, d.objoid, d.objsubid) o
        WHERE o.schema IS NULL
           OR (o.schema <> 'information_schema' AND o.schema NOT LIKE 'pg\_%')
        """;

    /// <summary>The table an index sits on, which is part of the index's identity.</summary>
    public const string IndexTable = """
        SELECT c.relname
        FROM pg_catalog.pg_index i
        JOIN pg_catalog.pg_class c ON c.oid = i.indrelid
        WHERE i.indexrelid = @oid::oid
        """;

    /// <summary>The columns of one relation, with everything needed to write its definition.</summary>
    public const string Columns = """
        SELECT a.attname,
               pg_catalog.format_type(a.atttypid, a.atttypmod) AS coltype,
               a.attnotnull, a.attidentity, a.attgenerated, a.attislocal,
               COALESCE(pg_catalog.pg_get_expr(ad.adbin, ad.adrelid), '') AS defaultexpr,
               CASE WHEN a.attcollation <> 0 AND a.attcollation <> t.typcollation
                    THEN COALESCE(cn.nspname, '') ELSE '' END AS collschema,
               CASE WHEN a.attcollation <> 0 AND a.attcollation <> t.typcollation
                    THEN COALESCE(cl.collname, '') ELSE '' END AS collname
        FROM pg_catalog.pg_attribute a
        JOIN pg_catalog.pg_type t ON t.oid = a.atttypid
        LEFT JOIN pg_catalog.pg_attrdef ad ON ad.adrelid = a.attrelid AND ad.adnum = a.attnum
        LEFT JOIN pg_catalog.pg_collation cl ON cl.oid = a.attcollation
        LEFT JOIN pg_catalog.pg_namespace cn ON cn.oid = cl.collnamespace
        WHERE a.attrelid = @oid::oid AND a.attnum > 0 AND NOT a.attisdropped
        ORDER BY a.attnum
        """;

    /// <summary>
    /// The sequence a column's identity is backed by, so its options can be written into the
    /// <c>GENERATED ... AS IDENTITY</c> clause.
    /// </summary>
    public const string IdentitySequences = """
        SELECT a.attname, pg_catalog.format_type(s.seqtypid, NULL) AS seqtype,
               s.seqstart, s.seqincrement, s.seqmin, s.seqmax, s.seqcache, s.seqcycle
        FROM pg_catalog.pg_attribute a
        JOIN pg_catalog.pg_depend d ON d.refobjid = a.attrelid AND d.refobjsubid = a.attnum
                                   AND d.classid = 'pg_class'::regclass AND d.deptype = 'i'
        JOIN pg_catalog.pg_sequence s ON s.seqrelid = d.objid
        WHERE a.attrelid = @oid::oid AND a.attnum > 0 AND NOT a.attisdropped
        """;

    /// <summary>The definition of one sequence.</summary>
    public const string Sequence = """
        SELECT pg_catalog.format_type(s.seqtypid, NULL) AS seqtype,
               s.seqstart, s.seqincrement, s.seqmin, s.seqmax, s.seqcache, s.seqcycle
        FROM pg_catalog.pg_sequence s
        WHERE s.seqrelid = @oid::oid
        """;

    public const string EnumLabels = """
        SELECT e.enumlabel
        FROM pg_catalog.pg_enum e
        WHERE e.enumtypid = @oid::oid
        ORDER BY e.enumsortorder
        """;

    public const string CompositeAttributes = """
        SELECT a.attname, pg_catalog.format_type(a.atttypid, a.atttypmod) AS coltype
        FROM pg_catalog.pg_attribute a
        JOIN pg_catalog.pg_type t ON t.oid = a.atttypid
        WHERE a.attrelid = (SELECT typrelid FROM pg_catalog.pg_type WHERE oid = @oid::oid)
          AND a.attnum > 0 AND NOT a.attisdropped
        ORDER BY a.attnum
        """;

    public const string Domain = """
        SELECT pg_catalog.format_type(t.typbasetype, t.typtypmod) AS basetype,
               t.typnotnull,
               COALESCE(t.typdefault, '') AS typdefault
        FROM pg_catalog.pg_type t
        WHERE t.oid = @oid::oid
        """;

    public const string DomainConstraints = """
        SELECT pg_catalog.pg_get_constraintdef(k.oid) AS definition, k.conname
        FROM pg_catalog.pg_constraint k
        WHERE k.contypid = @oid::oid
        ORDER BY k.conname
        """;

    /// <summary>
    /// An aggregate cannot be scripted with <c>pg_get_functiondef</c> - the server raises an error
    /// rather than returning anything - so it is assembled from <c>pg_aggregate</c> instead.
    /// </summary>
    public const string Aggregate = """
        SELECT pg_catalog.format_type(a.aggtranstype, NULL) AS stype,
               a.aggtransfn::regprocedure::text AS sfunc,
               COALESCE(NULLIF(a.aggfinalfn, 0)::regprocedure::text, '') AS finalfunc,
               COALESCE(NULLIF(a.aggcombinefn, 0)::regprocedure::text, '') AS combinefunc,
               COALESCE(a.agginitval, '') AS initcond,
               a.agginitval IS NOT NULL AS hasinitcond,
               COALESCE(NULLIF(a.aggsortop, 0)::regoperator::text, '') AS sortop
        FROM pg_catalog.pg_aggregate a
        WHERE a.aggfnoid = @oid::oid
        """;

    public const string FunctionDefinition = "SELECT pg_catalog.pg_get_functiondef(@oid::oid)";

    public const string ViewDefinition = "SELECT pg_catalog.pg_get_viewdef(@oid::oid, true)";

    public const string IndexDefinition = "SELECT pg_catalog.pg_get_indexdef(@oid::oid)";

    public const string ConstraintDefinition = "SELECT pg_catalog.pg_get_constraintdef(@oid::oid)";

    public const string TriggerDefinition = "SELECT pg_catalog.pg_get_triggerdef(@oid::oid, true)";

    public const string RuleDefinition = "SELECT pg_catalog.pg_get_ruledef(@oid::oid, true)";

    /// <summary>
    /// Object kinds living in the database that this tool does not script yet. Not an error, but
    /// the first thing to check when a deployment of the generated script comes up short.
    /// </summary>
    public const string UnsupportedObjectTypes = """
        SELECT 'EXTENSION', COUNT(*) FROM pg_catalog.pg_extension WHERE extname <> 'plpgsql'
        UNION ALL
        SELECT 'FOREIGN TABLE', COUNT(*)
        FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relkind = 'f' AND n.nspname <> 'information_schema' AND n.nspname NOT LIKE 'pg\_%'
        UNION ALL
        SELECT 'COLLATION', COUNT(*)
        FROM pg_catalog.pg_collation c JOIN pg_catalog.pg_namespace n ON n.oid = c.collnamespace
        WHERE n.nspname <> 'information_schema' AND n.nspname NOT LIKE 'pg\_%'
        """;
}
