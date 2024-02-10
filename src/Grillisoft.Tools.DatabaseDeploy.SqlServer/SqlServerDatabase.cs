using Grillisoft.Tools.DatabaseDeploy.Database;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer;

public class SqlServerDatabase : DatabaseBase
{
    private readonly string _migrationTableName;

    internal SqlServerDatabase(
        string name,
        string connectionString,
        string migrationTableName,
        SqlServerScriptParser parser,
        ILogger<SqlServerDatabase> logger
    ) : base(name, new SqlConnection(connectionString), parser, logger)
    {
        _migrationTableName = migrationTableName;
    }

    protected override ISqlScripts CreateSqlScripts()
    {
        return new SqlServerScripts(this.DatabaseName, _migrationTableName);
    }
}