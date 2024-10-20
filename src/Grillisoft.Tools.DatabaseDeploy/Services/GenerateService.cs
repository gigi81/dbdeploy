using System.Diagnostics;
using System.IO.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace Grillisoft.Tools.DatabaseDeploy.Services;

public class GenerateService : BaseService
{
    private readonly IDirectoryInfo _directory;

    public GenerateService(
        GenerateOptions options,
        IDatabasesCollection databases,
        IFileSystem fileSystem,
        IOptions<GlobalSettings> globalSettings,
        ILogger<GenerateService> logger)
        : base(databases, fileSystem, globalSettings, logger)
    {
        _directory = fileSystem.DirectoryInfo.New(options.Path);
    }

    public async override Task<int> Execute(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Searching for missing rollback scripts on path {Path}", _directory.FullName);

        var errors = 0;
        var stopwatch = Stopwatch.StartNew();
        var missingRollbacks = _directory.GetFiles("*.Deploy.sql", SearchOption.AllDirectories)
            .Where(file => !GetRollbackFile(file).Exists)
            .ToArray();

        foreach (var deployFile in missingRollbacks)
        {
            var rollbackFile = GetRollbackFile(deployFile);
            _logger.LogInformation("Generating rollback script {Path}", rollbackFile.FullName);

            try
            {
                var rollbackScript = await GenerateRollback(await deployFile.ReadAllTextAsync(cancellationToken));
                await rollbackFile.WriteAllTextAsync(rollbackScript, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate rollback script {Path}", rollbackFile.FullName);
                errors++;
            }
        }
        
        _logger.LogInformation("Generated {Count} rollback scripts in {Elapsed}", missingRollbacks.Length, stopwatch.Elapsed);
        return errors;
    }

    private static IFileInfo GetRollbackFile(IFileInfo deployFile)
    {
        return deployFile.Directory.File(deployFile.Name.Replace(".Deploy.sql", ".Rollback.sql"));
    }

    private const string RollbackPrompt = "Can you please generate a rollback SQL script for the following SQL script:\n";
    
    private static async Task<string> GenerateRollback(string script)
    {
        var client = new ChatClient(model: "davinci-002", apiKey: Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
        var completion = await client.CompleteChatAsync(RollbackPrompt + script);

        return completion.Value.Content[0].Text;
    }
}