namespace Grillisoft.Tools.DatabaseDeploy.Contracts;

public sealed class DbObject
{
    public string Name { get; }
    
    public string Type { get; }
    
    public List<DbObject> Dependencies { get; } = new List<DbObject>();

    public DbObject(string name, string type)
    {
        Name = name;
        Type = type;
    }

    public override string ToString() => $"{Type}: {Name}";
}