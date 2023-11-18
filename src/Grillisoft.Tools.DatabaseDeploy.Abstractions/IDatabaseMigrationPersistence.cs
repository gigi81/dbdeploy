using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy.Abstractions;

public interface IDatabaseMigrationPersistence
{
    void Initialize();

    DatabaseMigration[] GetMigrations();

    void AddMigration(DatabaseMigration version);

    void RemoveMigration(DatabaseMigration version);
}
