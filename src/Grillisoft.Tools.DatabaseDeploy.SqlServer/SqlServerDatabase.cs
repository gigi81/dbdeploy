using Grillisoft.Tools.DatabaseDeploy.Database;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer;

public class SqlServerDatabase : DatabaseBase
{
    internal SqlServerDatabase(
        string name,
        string connectionString,
        string migrationTableName,
        SqlServerScriptParser parser,
        ILogger<SqlServerDatabase> logger
    ) : base(name, new SqlConnection(connectionString), new SqlServerScripts(migrationTableName), parser, logger)
    {
    }
}