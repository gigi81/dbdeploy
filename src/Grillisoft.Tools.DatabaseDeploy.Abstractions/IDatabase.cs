using System.IO.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy.Abstractions;

public interface IDatabase
{
    IScriptParser ScriptParser { get; }
    Task RunScript(string script);
    Task InitializeMigrations();
    Task<DatabaseMigration[]> GetMigrations();
    Task AddMigration(DatabaseMigration migration);
    Task RemoveMigration(DatabaseMigration migration);
}