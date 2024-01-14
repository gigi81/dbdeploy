using System.Data.Common;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Database;
using MySqlConnector;

namespace Grillisoft.Tools.DatabaseDeploy.MySql;

public class MySqlDatabase : DatabaseBase
{
    private readonly string _getSql;
    private readonly string _addSql;
    private readonly string _removeSql;
    private readonly string _initSql;

    public MySqlDatabase(string name, string connectionString, string migrationTableName, MySqlScriptParser parser)
        : base(name, new MySqlConnection(connectionString), parser)
    {
        _getSql = $"SELECT [name], [deployed_utc], [user], [hash] FROM {migrationTableName}";
        _addSql = $"INSERT INTO {migrationTableName} VALUES(@name, @deployed_utc, @user, @hash)";
        _removeSql = $"DELETE FROM {migrationTableName} WHERE name = @name";
        _initSql = $@"
            IF OBJECT_ID(N'[{migrationTableName}]', N'U') IS NULL
            CREATE TABLE [{migrationTableName}] (
              [name] NVARCHAR(255),
              [deployed_utc] datetime2,
              [user] NVARCHAR(100),
              [hash] char(32),
              CONSTRAINT [PK_{migrationTableName}] PRIMARY KEY CLUSTERED([name] ASC)
            );
        ";
    }

    protected override string InitSql => _initSql;
    protected override string GetSql => _getSql;
    protected override string AddSql => _addSql;
    protected override string RemoveSql => _removeSql;
}