using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy.PostgreSql.Ddl;

/// <summary>
/// One object to be scripted, with everything needed to ask the catalog about it again.
/// </summary>
/// <remarks>
/// <see cref="DbObject"/> only carries a name and a type, which is all the dependency graph needs.
/// Everything here needs more: an oid to pass to the <c>pg_get_..._def</c> functions, and, for the
/// things that only exist as part of another object, the parent they hang off.
/// <para>
/// The name is built to be the object's SQL identity rather than just its <c>relname</c>, because
/// nothing else is unique: two schemas can hold a table of the same name, two functions can share a
/// name and differ only in their arguments, and a constraint name is only unique within its table.
/// </para>
/// </remarks>
internal sealed class PostgreSqlObject
{
    public PostgreSqlObject(
        string type,
        uint oid,
        string schema,
        string name,
        string? arguments = null,
        string? parentSchema = null,
        string? parentName = null,
        string? detail = null)
    {
        Type = type;
        Oid = oid;
        Schema = schema;
        Name = name;
        Arguments = arguments;
        ParentSchema = parentSchema;
        ParentName = parentName;
        Detail = detail;

        DbObject = new DbObject(BuildIdentity(), type);
    }

    public string Type { get; }

    /// <summary>The catalog oid, or zero for the objects that are not catalog rows.</summary>
    public uint Oid { get; }

    public string Schema { get; }

    public string Name { get; }

    /// <summary>The argument list that tells two routines of the same name apart.</summary>
    public string? Arguments { get; }

    /// <summary>Schema of the relation a constraint, index, trigger or rule belongs to.</summary>
    public string? ParentSchema { get; }

    /// <summary>The relation a constraint, index, trigger or rule belongs to.</summary>
    public string? ParentName { get; }

    /// <summary>
    /// Whatever else the statement needs and one query already had to hand: the bound of a
    /// partition, the column a sequence is owned by, the persistence of a table.
    /// </summary>
    public string? Detail { get; }

    public DbObject DbObject { get; }

    /// <summary>The object's own name, schema qualified and quoted.</summary>
    public string QualifiedName => Name.Qualify(Schema);

    /// <summary>The relation this hangs off, schema qualified and quoted.</summary>
    public string QualifiedParent =>
        ParentName is null ? string.Empty : ParentName.Qualify(ParentSchema);

    public string Key => DbObject.Key;

    private string BuildIdentity()
    {
        if (Type == PostgreSqlObjectType.Schema)
            return Schema.Quote();

        if (Arguments is not null)
            return $"{QualifiedName}({Arguments})";

        return ParentName is null ? QualifiedName : $"{QualifiedParent}.{Name.Quote()}";
    }

    public override string ToString() => $"{Type} {DbObject.Name}";
}
