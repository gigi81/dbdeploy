namespace Grillisoft.Tools.DatabaseDeploy.SqlServer;

/// <summary>
/// The object types the schema generator knows how to script, and the position each one takes in
/// the generated script when the dependency graph leaves the ordering open.
/// </summary>
/// <remarks>
/// The names are dbdeploy's own, not the one letter codes of <c>sys.objects.type</c>: several of
/// these types do not live in <c>sys.objects</c> at all (schemas, user defined types, XML schema
/// collections, partition functions and schemes) and indexes and foreign keys are scripted as
/// objects of their own even though SQL Server considers them part of a table.
/// </remarks>
internal sealed record SqlServerObjectType(string Name, int Rank)
{
    public const string Schema = "SCHEMA";
    public const string PartitionFunction = "PARTITION FUNCTION";
    public const string PartitionScheme = "PARTITION SCHEME";
    public const string XmlSchemaCollection = "XML SCHEMA COLLECTION";
    public const string Type = "TYPE";
    public const string TableType = "TABLE TYPE";
    public const string Sequence = "SEQUENCE";
    public const string Table = "TABLE";
    public const string Synonym = "SYNONYM";
    public const string View = "VIEW";
    public const string Function = "FUNCTION";
    public const string Procedure = "PROCEDURE";
    public const string Index = "INDEX";
    public const string ForeignKey = "FOREIGN KEY";
    public const string Trigger = "TRIGGER";

    /// <summary>
    /// Supported types, in the order they are written to the script when nothing else forces a
    /// different order. Everything a table can be built on comes first, then the tables, then
    /// everything that can only be created on top of a table.
    /// </summary>
    private static readonly SqlServerObjectType[] Supported =
    [
        new(Schema, 0),
        new(PartitionFunction, 5),
        new(PartitionScheme, 6),
        new(XmlSchemaCollection, 8),
        new(Type, 10),
        new(TableType, 12),
        new(Sequence, 20),
        new(Table, 30),
        new(Synonym, 40),
        new(View, 60),
        new(Function, 80),
        new(Procedure, 90),
        new(Index, 120),
        new(ForeignKey, 130),
        new(Trigger, 140),
    ];

    private static readonly Dictionary<string, SqlServerObjectType> ByName =
        Supported.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// <c>sys.objects.type</c> codes this tool scripts. The CLR flavours (<c>PC</c>, <c>FS</c>,
    /// <c>FT</c>, <c>TA</c>) are deliberately absent: they cannot be created without their assembly,
    /// which is not something a text script can carry.
    /// </summary>
    private static readonly Dictionary<string, SqlServerObjectType> BySysType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["U"] = ByName[Table],
        ["V"] = ByName[View],
        ["P"] = ByName[Procedure],
        ["FN"] = ByName[Function],
        ["IF"] = ByName[Function],
        ["TF"] = ByName[Function],
        ["SN"] = ByName[Synonym],
        ["SO"] = ByName[Sequence],
    };

    public static IReadOnlyList<SqlServerObjectType> All => Supported;

    /// <summary>The <c>sys.objects.type</c> codes read by the object discovery query.</summary>
    public static IEnumerable<string> QueryableSysTypes => BySysType.Keys;

    /// <summary>Type behind a <c>sys.objects.type</c> code, or null when it cannot be scripted.</summary>
    public static SqlServerObjectType? FromSysType(string sysType)
        => BySysType.GetValueOrDefault(sysType.Trim());

    public static SqlServerObjectType? Find(string name)
        => ByName.GetValueOrDefault(name);

    /// <summary>
    /// Ordering rank of a type; unknown types sort last so they never push a known type down.
    /// </summary>
    public static int RankOf(string name)
        => Find(name)?.Rank ?? int.MaxValue;
}
