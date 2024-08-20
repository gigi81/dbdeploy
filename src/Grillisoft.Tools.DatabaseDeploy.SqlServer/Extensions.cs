using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.SqlServer;
using Microsoft.Extensions.DependencyInjection;

// ReSharper disable once CheckNamespace
namespace Grillisoft.Tools.DatabaseDeploy;

public static class Extensions
{
    public static IServiceCollection AddSqlServer(this IServiceCollection services)
    {
        services.AddSingleton<SqlServerScriptParser>();
        services.AddSingleton<IDatabaseFactory, SqlServerDatabaseFactory>();

        return services;
    }
}