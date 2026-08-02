using System.IO.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Microsoft.Extensions.Options;

namespace Grillisoft.Tools.DatabaseDeploy.Services;

/// <summary>
/// What every service needs whatever it does: the databases, the disk, the settings and the two
/// runners. They travel together so that a service constructor is about what makes that service
/// different - its options, its progress, its logger - and adding one to <see cref="BaseService"/>
/// does not touch every service.
/// </summary>
public class ServiceDependencies
{
    public ServiceDependencies(
        IDatabasesCollection databases,
        IOptions<GlobalSettings> globalSettings,
        IDatabaseLoggerFactory databaseLoggers,
        IScriptsRunner scripts,
        IDirectoryInfo rootDirectory)
    {
        this.Databases = databases;
        this.GlobalSettings = globalSettings;
        this.DatabaseLoggers = databaseLoggers;
        this.Scripts = scripts;
        this.RootDirectory = rootDirectory;
    }

    public IDatabasesCollection Databases { get; }

    public IOptions<GlobalSettings> GlobalSettings { get; }

    public IDatabaseLoggerFactory DatabaseLoggers { get; }

    public IScriptsRunner Scripts { get; }

    public IDirectoryInfo RootDirectory { get; }
}
