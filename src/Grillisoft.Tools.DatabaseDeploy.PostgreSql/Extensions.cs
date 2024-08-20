using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.PostgreSql;
using Microsoft.Extensions.DependencyInjection;

// ReSharper disable once CheckNamespace
namespace Grillisoft.Tools.DatabaseDeploy;

public static class Extensions
{
    public static IServiceCollection AddPostgreSql(this IServiceCollection services)
    {
        services.AddSingleton<PostgreSqlScriptParser>();
        services.AddSingleton<IDatabaseFactory, PostgreSqlDatabaseFactory>();

        return services;
    }
}