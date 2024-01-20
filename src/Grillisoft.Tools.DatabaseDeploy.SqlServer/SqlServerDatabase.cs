using Grillisoft.Tools.DatabaseDeploy.Database;
using Microsoft.Data.SqlClient;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer;

public class SqlServerDatabase : DatabaseBase
{
    private readonly string _getSql;
    private readonly string _addSql;
    private readonly string _removeSql;
    private readonly string _initSql;
    private readonly string _clearSql;

    internal SqlServerDatabase(string name, string connectionString, string migrationTableName, SqlServerScriptParser parser)
        : base(name, new SqlConnection(connectionString), parser)
    {
        _getSql = $"SELECT [name], [deployed_utc], [user], [hash] FROM {migrationTableName} ORDER BY [id] ASC";
        _addSql = $"INSERT INTO {migrationTableName} VALUES(@name, @deployed_utc, @user, @hash)";
        _removeSql = $"DELETE FROM {migrationTableName} WHERE name = @name";
        _initSql = $@"
            IF OBJECT_ID(N'[{migrationTableName}]', N'U') IS NULL
            CREATE TABLE [{migrationTableName}] (
              [id] int NOT NULL AUTO_INCREMENT
              [name] NVARCHAR(255),
              [deployed_utc] datetime2,
              [user] NVARCHAR(100),
              [hash] char(32),
              CONSTRAINT [PK_{migrationTableName}] PRIMARY KEY CLUSTERED([id] ASC),
              CONSTRAINT [AK_{migrationTableName}_name] UNIQUE(name) 
            );
        ";
        _clearSql = $"DROP TABLE IF EXISTS `{migrationTableName}`";
    }
    
    protected override string InitSql => _initSql;
    protected override string GetSql => _getSql;
    protected override string AddSql => _addSql;
    protected override string RemoveSql => _removeSql;
    protected override string ClearSql => _clearSql;
}