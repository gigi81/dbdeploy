using System.IO.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Grillisoft.Tools.DatabaseDeploy;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// The folder of the run, the one given with <c>--path</c>: the branch files, the scripts and
    /// the hook scripts are all looked up in it, and every service takes it from here rather than
    /// from its own options. Resolved from the registered <see cref="IFileSystem"/>, so the order
    /// the two are registered in does not matter.
    /// </summary>
    public static IServiceCollection AddRootDirectory(this IServiceCollection services, string path) =>
        services.AddSingleton<IDirectoryInfo>(sp => sp.GetRequiredService<IFileSystem>().DirectoryInfo.New(path));
}
