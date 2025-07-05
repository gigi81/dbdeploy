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
}