using Microsoft.Extensions.DependencyInjection;

namespace Grillisoft.Tools.DatabaseDeploy.SqlServer;

public static class Extensions
{
    public static IServiceCollection AddSqlServer(this IServiceCollection services)
    {
        services.AddSingleton<SqlServerScriptParser>();
        services.AddSingleton<SqlServerDatabaseFactory>();
        
        return services;
    }
}