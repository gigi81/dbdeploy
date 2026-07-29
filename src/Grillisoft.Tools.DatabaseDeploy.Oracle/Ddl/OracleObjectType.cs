namespace Grillisoft.Tools.DatabaseDeploy.Oracle.Ddl;

/// <summary>
/// Maps an <c>ALL_OBJECTS.OBJECT_TYPE</c> (plus the <see cref="RefConstraint"/> pseudo type, which
/// comes from <c>ALL_CONSTRAINTS</c>) to the object type name understood by <c>DBMS_METADATA</c>,
/// and to the position the object takes in the generated script when the dependency graph
/// leaves the ordering open.
/// </summary>
/// <remarks>
/// The <c>DBMS_METADATA</c> names are not the same as the <c>ALL_OBJECTS</c> ones: an object type
/// containing a space is always spelled with an underscore (<c>PACKAGE BODY</c> becomes
/// <c>PACKAGE_BODY</c>) and the specification of a package or a type has its own name
/// (<c>PACKAGE_SPEC</c> / <c>TYPE_SPEC</c>). Passing the <c>ALL_OBJECTS</c> spelling straight to
/// <c>DBMS_METADATA.GET_DDL</c> raises ORA-31600.
/// </remarks>
internal sealed record OracleObjectType(
    string Name,
    string MetadataType,
    int Rank,
    bool IsPlSql = false,
    string? SourceType = null,
    string? FallbackMetadataType = null)
{
    /// <summary>
    /// Foreign keys are not listed in <c>ALL_OBJECTS</c>, so they are synthesized from
    /// <c>ALL_CONSTRAINTS</c> under this pseudo type.
    /// </summary>
    public const string RefConstraint = "REF_CONSTRAINT";

    /// <summary>
    /// Supported types, in the order they are written to the script when nothing else forces
    /// a different order. Sequences and types come first, then tables, then everything that can
    /// only be created on top of a table.
    /// </summary>
    private static readonly OracleObjectType[] Supported =
    [
        new("TYPE", "TYPE_SPEC", 10, IsPlSql: true, SourceType: "TYPE", FallbackMetadataType: "TYPE"),
        new("SEQUENCE", "SEQUENCE", 20),
        new("TABLE", "TABLE", 30),
        new("SYNONYM", "SYNONYM", 40),
        new("TYPE BODY", "TYPE_BODY", 50, IsPlSql: true, SourceType: "TYPE BODY"),
        new("VIEW", "VIEW", 60),
        new("MATERIALIZED VIEW", "MATERIALIZED_VIEW", 70),
        new("FUNCTION", "FUNCTION", 80, IsPlSql: true, SourceType: "FUNCTION"),
        new("PROCEDURE", "PROCEDURE", 90, IsPlSql: true, SourceType: "PROCEDURE"),
        new("PACKAGE", "PACKAGE_SPEC", 100, IsPlSql: true, SourceType: "PACKAGE", FallbackMetadataType: "PACKAGE"),
        new("PACKAGE BODY", "PACKAGE_BODY", 110, IsPlSql: true, SourceType: "PACKAGE BODY"),
        new("INDEX", "INDEX", 120),
        new(RefConstraint, "REF_CONSTRAINT", 130),
        new("TRIGGER", "TRIGGER", 140, IsPlSql: true, SourceType: "TRIGGER"),
    ];

    private static readonly Dictionary<string, OracleObjectType> ByName =
        Supported.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<OracleObjectType> All => Supported;

    /// <summary>
    /// Types that are read from <c>ALL_OBJECTS</c>. <see cref="RefConstraint"/> is excluded because
    /// constraints are not schema objects.
    /// </summary>
    public static IEnumerable<string> QueryableNames =>
        Supported.Where(t => !RefConstraint.Equals(t.Name, StringComparison.Ordinal)).Select(t => t.Name);

    public static OracleObjectType? Find(string name)
        => ByName.GetValueOrDefault(name);

    /// <summary>
    /// Ordering rank of a type; unknown types sort last so they never push a known type down.
    /// </summary>
    public static int RankOf(string name)
        => Find(name)?.Rank ?? int.MaxValue;
}
