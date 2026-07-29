using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer;

/// <summary>
/// One object to be scripted, with everything needed to find it back in SMO.
/// </summary>
/// <remarks>
/// <see cref="DbObject"/> only carries a name and a type, which is all the dependency graph needs.
/// Indexes and triggers, however, are addressed through their table, and every object needs its
/// schema, so the two are kept side by side and looked up through <see cref="DbObject"/>.
/// </remarks>
internal sealed class SqlServerObject
{
    public SqlServerObject(
        SqlServerObjectType type,
        string schema,
        string name,
        string? parentSchema = null,
        string? parentName = null)
    {
        Type = type;
        Schema = schema;
        Name = name;
        ParentSchema = parentSchema;
        ParentName = parentName;

        // An index name is only unique within its table, so the parent is part of the identity.
        DbObject = new DbObject(
            parentName is null
                ? name.Qualify(schema)
                : parentName.Qualify(parentSchema) + "." + name.Quote(),
            type.Name);
    }

    public SqlServerObjectType Type { get; }

    /// <summary>Schema the object lives in; empty for objects that are not schema scoped.</summary>
    public string Schema { get; }

    public string Name { get; }

    /// <summary>Schema of the table an index, a trigger or a foreign key belongs to.</summary>
    public string? ParentSchema { get; }

    /// <summary>Table an index, a trigger or a foreign key belongs to.</summary>
    public string? ParentName { get; }

    public DbObject DbObject { get; }

    /// <summary>Name used in logs and in the generated script's comments.</summary>
    public string QualifiedName => DbObject.Name;

    public string Key => DbObject.Key;

    public override string ToString() => $"{Type.Name} {QualifiedName}";
}
