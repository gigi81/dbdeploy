using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy.Abstractions;

public interface IDatabase
{
    Task<List<DatabaseMigration>> GetMigrations();
    Task Deploy(Step step);
}