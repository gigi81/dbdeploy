using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Formatting;
using Microsoft.Extensions.DependencyInjection;

// ReSharper disable once CheckNamespace
namespace Grillisoft.Tools.DatabaseDeploy;

public static class Extensions
{
    public static IServiceCollection AddSqlFormatting(this IServiceCollection services)
    {
        services.AddSingleton<ISqlFormatterFactory, SqlFormatterFactory>();
        return services;
    }
}