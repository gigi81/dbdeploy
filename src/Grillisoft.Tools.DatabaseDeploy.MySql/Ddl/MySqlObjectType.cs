namespace Grillisoft.Tools.DatabaseDeploy.MySql.Ddl;

/// <summary>
/// Maps an object type to the <c>SHOW CREATE</c> statement that scripts it, and to the position the
/// object takes in the generated script when the dependency graph leaves the ordering open.
/// </summary>
/// <remarks>
/// <c>SHOW CREATE ...</c> is the server's own scripting engine, the counterpart of Oracle's
/// <c>DBMS_METADATA</c>. Its result set is not the same shape for every type - a trigger comes back
/// in seven columns and calls its DDL <c>SQL Original Statement</c> - so the column holding the
/// statement is part of the type rather than assumed to be the second one.
/// <para>
/// There is deliberately no <c>INDEX</c> type. MySQL has no standalone index object: an index is a
/// <c>KEY</c> line inside <c>SHOW CREATE TABLE</c> and comes out with its table. Foreign keys are
/// the exception, and are split back out - see <see cref="MySqlDdlRewriter"/>.
/// </para>
/// <para>
/// <see cref="Sequence"/>, <see cref="Package"/> and <see cref="PackageBody"/> exist on MariaDB and
/// not on MySQL. Nothing special is needed for that: the discovery query simply returns no row of
/// that type on a server that has none.
/// </para>
/// </remarks>
internal sealed record MySqlObjectType(string Name, int Rank, string ShowStatement, string DdlColumn)
{
    public const string Sequence = "SEQUENCE";
    public const string Table = "BASE TABLE";
    public const string View = "VIEW";
    public const string Function = "FUNCTION";
    public const string Procedure = "PROCEDURE";
    public const string Package = "PACKAGE";
    public const string PackageBody = "PACKAGE BODY";
    public const string Trigger = "TRIGGER";
    public const string Event = "EVENT";

    /// <summary>
    /// Foreign keys are not objects in their own right anywhere in MySQL, so they are synthesized
    /// out of the <c>CREATE TABLE</c> text under this pseudo type.
    /// </summary>
    public const string ForeignKey = "FOREIGN KEY";

    /// <summary>
    /// Supported types, in the order they are written to the script when nothing else forces a
    /// different order. Sequences and tables first, then the program units, then the views built on
    /// top of them, and last everything that can only exist once its table does.
    /// </summary>
    /// <remarks>
    /// Functions sit after tables rather than before because MySQL does not allow a stored function
    /// in a <c>DEFAULT</c>, a generated column or a <c>CHECK</c>, so a table can never depend on
    /// one; a view can, which is why views come after both.
    /// </remarks>
    private static readonly MySqlObjectType[] Supported =
    [
        new(Sequence, 10, "SHOW CREATE SEQUENCE", "Create Table"),
        new(Table, 20, "SHOW CREATE TABLE", "Create Table"),
        new(Function, 30, "SHOW CREATE FUNCTION", "Create Function"),
        new(Procedure, 40, "SHOW CREATE PROCEDURE", "Create Procedure"),
        new(Package, 45, "SHOW CREATE PACKAGE", "Create Package"),
        new(PackageBody, 50, "SHOW CREATE PACKAGE BODY", "Create Package Body"),
        new(View, 60, "SHOW CREATE VIEW", "Create View"),
        new(ForeignKey, 130, string.Empty, string.Empty),
        new(Trigger, 140, "SHOW CREATE TRIGGER", "SQL Original Statement"),
        new(Event, 150, "SHOW CREATE EVENT", "Create Event"),
    ];

    private static readonly Dictionary<string, MySqlObjectType> ByName =
        Supported.ToDictionary(type => type.Name, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<MySqlObjectType> All => Supported;

    public static MySqlObjectType? Find(string name) => ByName.GetValueOrDefault(name);

    /// <summary>
    /// Ordering rank of a type; unknown types sort last so they never push a known type down.
    /// </summary>
    public static int RankOf(string name) => Find(name)?.Rank ?? int.MaxValue;
}
