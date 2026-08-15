namespace Grillisoft.Tools.DatabaseDeploy.PostgreSql.Ddl;

/// <summary>
/// The object types that are scripted, and the position each takes when the dependency graph leaves
/// the ordering open.
/// </summary>
/// <remarks>
/// The ranks are only a tie break. Almost all of the real ordering comes out of <c>pg_depend</c>,
/// which is what puts a function returning <c>SETOF customer</c> after the customer table even
/// though functions rank ahead of tables.
/// <para>
/// Three of these are not catalog objects at all. <see cref="Partition"/> and
/// <see cref="SequenceOwner"/> are the <c>ALTER TABLE ... ATTACH PARTITION</c> and
/// <c>ALTER SEQUENCE ... OWNED BY</c> that have to follow the two objects they tie together, and
/// <see cref="ForeignKey"/> is separated from the other constraints only so it lands last.
/// </para>
/// </remarks>
internal static class PostgreSqlObjectType
{
    public const string Schema = "SCHEMA";
    public const string Type = "TYPE";
    public const string Domain = "DOMAIN";
    public const string Sequence = "SEQUENCE";
    public const string Function = "FUNCTION";
    public const string Procedure = "PROCEDURE";
    public const string Aggregate = "AGGREGATE";
    public const string Table = "TABLE";
    public const string Partition = "PARTITION";
    public const string SequenceOwner = "SEQUENCE OWNER";
    public const string View = "VIEW";
    public const string MaterializedView = "MATERIALIZED VIEW";
    public const string Constraint = "CONSTRAINT";
    public const string Index = "INDEX";
    public const string ForeignKey = "FOREIGN KEY";
    public const string Trigger = "TRIGGER";
    public const string Rule = "RULE";

    private static readonly Dictionary<string, int> Ranks = new(StringComparer.OrdinalIgnoreCase)
    {
        [Schema] = 0,
        [Type] = 12,
        [Domain] = 14,
        [Sequence] = 20,
        [Function] = 25,
        [Procedure] = 25,
        [Aggregate] = 27,
        [Table] = 40,
        [Partition] = 45,
        [SequenceOwner] = 50,
        [View] = 60,
        [MaterializedView] = 70,
        [Constraint] = 80,
        [Index] = 120,
        [ForeignKey] = 130,
        [Trigger] = 140,
        [Rule] = 145,
    };

    public static IReadOnlyCollection<string> All => Ranks.Keys;

    /// <summary>
    /// Ordering rank of a type; unknown types sort last so they never push a known type down.
    /// </summary>
    public static int RankOf(string name) => Ranks.GetValueOrDefault(name, int.MaxValue);

    /// <summary>Maps a <c>pg_class.relkind</c> to the type it is scripted as.</summary>
    public static string? FromRelKind(char relKind) => relKind switch
    {
        'r' or 'p' => Table,
        'v' => View,
        'm' => MaterializedView,
        'S' => Sequence,
        'i' or 'I' => Index,
        _ => null,
    };

    /// <summary>Maps a <c>pg_type.typtype</c> to the type it is scripted as.</summary>
    public static string? FromTypType(char typType) => typType switch
    {
        'e' or 'c' or 'r' => Type,
        'd' => Domain,
        _ => null,
    };

    /// <summary>Maps a <c>pg_proc.prokind</c> to the type it is scripted as.</summary>
    public static string? FromProKind(char proKind) => proKind switch
    {
        'f' or 'w' => Function,
        'p' => Procedure,
        'a' => Aggregate,
        _ => null,
    };
}
