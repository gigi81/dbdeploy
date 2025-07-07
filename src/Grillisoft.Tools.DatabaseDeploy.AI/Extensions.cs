using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OllamaSharp;

// ReSharper disable once CheckNamespace
namespace Grillisoft.Tools.DatabaseDeploy;

public static class Extensions
{
    public static IServiceCollection AddAIGenerator(this IServiceCollection services)
    {
        services.AddSingleton<IChatClient>((_) => new OllamaApiClient(new Uri("http://localhost:11434/"), "llama3.1"));
        services.AddSingleton<IGenerator, Generator>();
        return services;
    }
}