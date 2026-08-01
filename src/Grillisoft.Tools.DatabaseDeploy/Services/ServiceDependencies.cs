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
        IFileSystem fileSystem,
        IOptions<GlobalSettings> globalSettings,
        IDatabaseLoggerFactory databaseLoggers,
        IScriptsRunner scripts)
    {
        this.Databases = databases;
        this.FileSystem = fileSystem;
        this.GlobalSettings = globalSettings;
        this.DatabaseLoggers = databaseLoggers;
        this.Scripts = scripts;
    }

    public IDatabasesCollection Databases { get; }

    public IFileSystem FileSystem { get; }

    public IOptions<GlobalSettings> GlobalSettings { get; }

    public IDatabaseLoggerFactory DatabaseLoggers { get; }

    public IScriptsRunner Scripts { get; }
}
