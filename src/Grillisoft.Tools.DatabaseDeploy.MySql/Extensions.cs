

using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.MySql;
// ReSharper disable once CheckNamespace
using Microsoft.Extensions.DependencyInjection;

namespace Grillisoft.Tools.DatabaseDeploy;

public static class Extensions
{
    public static IServiceCollection AddMySql(this IServiceCollection services)
    {
        services.AddSingleton<MySqlScriptParser>();
        services.AddSingleton<IDatabaseFactory, MySqlDatabaseFactory>();
        
        return services;
    }
}