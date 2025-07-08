namespace Grillisoft.Tools.DatabaseDeploy.Contracts;

public sealed class DbObject
{
    public DbObject(string name, string type)
    {
        Name = name;
        Type = type;
        Key = $"{name}---{type}";
    }

    public string Name { get; }

    public string Type { get; }

    public List<DbObject> Dependencies { get; } = [];

    public string Key { get; }

    public override string ToString() => this.Key;

    public override bool Equals(object? obj)
    {
        return obj is DbObject other
               && this.Key.Equals(other.Key);
    }

    public override int GetHashCode() => this.Key.GetHashCode();
}