using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle;

public static class Extensions
{
    public static IServiceCollection AddOracle(this IServiceCollection services)
    {
        services.AddSingleton<OracleScriptParser>();
        services.AddSingleton<IDatabaseFactory, OracleDatabaseFactory>();

        return services;
    }
}