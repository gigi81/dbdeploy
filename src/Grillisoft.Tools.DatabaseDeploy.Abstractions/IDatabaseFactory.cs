namespace Grillisoft.Tools.DatabaseDeploy.Abstractions;

public interface IDatabaseFactory
{
    IDatabase? GetDatabase(string name);
}
