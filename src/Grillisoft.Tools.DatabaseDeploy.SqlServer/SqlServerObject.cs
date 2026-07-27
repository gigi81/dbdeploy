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

        var owner = parentName is null
            ? Qualify(schema, name)
            : Qualify(parentSchema, parentName) + "." + Quote(name);

        DbObject = new DbObject(owner, type.Name);
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

    public static string Qualify(string? schema, string name)
        => string.IsNullOrEmpty(schema) ? Quote(name) : Quote(schema) + "." + Quote(name);

    public static string Quote(string name)
        => "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";
}
