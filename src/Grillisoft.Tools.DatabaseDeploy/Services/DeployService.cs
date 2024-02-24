using System.Diagnostics;
using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Exceptions;
using Grillisoft.Tools.DatabaseDeploy.Options;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.Services;

public class DeployService : BaseService
{
    private readonly DeployOptions _options;
    private readonly IProgress<int> _progress;

    public DeployService(
        DeployOptions options,
        IDatabasesCollection databases,
        IFileSystem fileSystem,
        IProgress<int> progress,
        ILogger<DeployService> logger
    ) : base(databases, fileSystem, logger)
    {
        _options = options;
        _progress = progress;
    }
    
    public async override Task Execute(CancellationToken stoppingToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var count = 0;
        var manager = await LoadBranchesManager(_options.Path);
        
        if (!manager.Branches.TryGetValue(_options.Branch, out var branch))
            throw new BranchNotFoundException(_options.Branch);

        var steps = manager.GetDeploySteps(branch).ToArray();
        var databases = steps.Select(s => s.Database).Distinct().ToArray();

        if (await CheckAndCreateDatabases(databases, stoppingToken) > 0)
            throw new Exception("One or more databases do not exists");

        await CreateMigrationTables(databases, stoppingToken);

        _progress.Report(0);
        foreach (var step in steps)
        {
            await DeployStep(step, stoppingToken);
            _progress.Report(++count * 100 / steps.Length);
        }
        _progress.Report(100);
    }

    private async Task CreateMigrationTables(IEnumerable<string> databases, CancellationToken stoppingToken)
    {
        foreach (var database in databases)
        {
            _logger.LogInformation($"Database {database} Initializing");
            var db = await GetDatabase(database, stoppingToken);
            await db.InitializeMigrations(stoppingToken);
        }
    }

    private async Task DeployStep(Step step, CancellationToken stoppingToken)
    {
        var database = await GetDatabase(step.Database, stoppingToken);
        var migrations = await GetMigrations(step.Database, stoppingToken);
            
        if (migrations.TryDequeue(out var migration))
        {
            if (!migration.Name.EqualsIgnoreCase(step.Name))
                throw new StepMigrationMismatchException(step, migration);
                
            _logger.LogInformation($"Database {step.Database} Step {step.Name} already deployed");
        }
        else
        {
            var hash = await GetHash(step.DeployScript);
            await RunScript(step.DeployScript, database, stoppingToken);
            if(_options.Test)
                await RunScript(step.TestScript, database, stoppingToken);

            _logger.LogInformation($"Database {step.Database} Adding migration {step.Name}");
            var migrationToAdd = new DatabaseMigration(
                step.Name,
                DateTimeOffset.UtcNow,
                Environment.UserName,
                hash);
                
            await database.AddMigration(migrationToAdd, stoppingToken);
        }
    }

    private static async Task<string> GetHash(IFileInfo file)
    {
        using var sha256 = MD5.Create();
        await using var fileStream = file.OpenRead();
        var hashBytes = await sha256.ComputeHashAsync(fileStream);
        var hashBuilder = new StringBuilder();

        foreach (var b in hashBytes)
        {
            hashBuilder.Append(b.ToString("x2")); // Convert to hexadecimal
        }

        return hashBuilder.ToString();
    }
    
    private async Task<int> CheckAndCreateDatabases(IEnumerable<string> databases, CancellationToken stoppingToken)
    {
        var errors = 0;
        
        foreach (var database in databases)
        {
            if (!await CheckAndCreateDatabase(database, stoppingToken))
                errors++;
        }

        return errors;
    }

    private async Task<bool> CheckAndCreateDatabase(string name, CancellationToken stoppingToken)
    {
        var database = await GetDatabase(name, stoppingToken);
        try
        {
            if (await database.Exists(stoppingToken))
                return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Database {name} failed to check if database exists");
            return false;
        }

        if (!_options.Create)
        {
            _logger.LogError($"Database {name} does not exists");
            return false;
        }

        try
        {
            _logger.LogInformation($"Database {name} does not exists");
            await database.Create(stoppingToken);
            _logger.LogInformation($"Database {name} created successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Database {name} failed to create database");
            return false;
        }
    }
}