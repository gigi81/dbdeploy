using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Oracle;
using Microsoft.Extensions.DependencyInjection;

// ReSharper disable once CheckNamespace
namespace Grillisoft.Tools.DatabaseDeploy;

public static class Extensions
{
    public static IServiceCollection AddOracle(this IServiceCollection services)
    {
        services.AddSingleton<OracleScriptParser>();
        services.AddSingleton<IDatabaseFactory, OracleDatabaseFactory>();

        return services;
    }
}