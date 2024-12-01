using System.Diagnostics;
using System.IO.Abstractions;
using System.Text;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Options;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace Grillisoft.Tools.DatabaseDeploy.Services;

public class GenerateService : BaseService
{
    private readonly IChatClient _chatClient;
    private readonly IDirectoryInfo _directory;

    public GenerateService(
        GenerateOptions options,
        IChatClient chatClient,
        IDatabasesCollection databases,
        IFileSystem fileSystem,
        IOptions<GlobalSettings> globalSettings,
        ILogger<GenerateService> logger)
        : base(databases, fileSystem, globalSettings, logger)
    {
        _chatClient = chatClient;
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

        if (missingRollbacks.Length <= 0)
        {
            _logger.LogWarning("No missing rollback scripts found on path {Path}", _directory.FullName);
            return 0;
        }
        
        foreach (var deployFile in missingRollbacks)
        {
            var rollbackFile = GetRollbackFile(deployFile);
            _logger.LogInformation("Generating rollback script {Path}", rollbackFile.FullName);

            try
            {
                var database = await GetDatabase(deployFile.Directory?.Name ?? "", cancellationToken);
                var deployScriptText = await deployFile.ReadAllTextAsync(cancellationToken);
                var rollbackScriptText = await GenerateRollback(deployScriptText, database.Dialect, cancellationToken);
                await rollbackFile.WriteAllTextAsync(rollbackScriptText, cancellationToken);
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

    private const string RollbackPrompt = "Can you please create a rollback SQL script for the below {0} script. Remember to invert the order of the operations in the rollback script. \n\n{1}";
    
    private async Task<string> GenerateRollback(string script, string dialect, CancellationToken cancellationToken)
    {
        var prompt = string.Format(RollbackPrompt, dialect, script);
        var completion = await _chatClient.CompleteAsync(prompt, cancellationToken: cancellationToken);

        using var reader = new StringReader(completion.Message.Text ?? string.Empty);
        var line = await reader.ReadLineAsync(cancellationToken);
        var builder = new StringBuilder();
        var sql = false;
        
        while (line != null)
        {
            if (!sql && line.StartsWith("```sql"))
                sql = true;
            else if (sql && line.StartsWith("```"))
                sql = false;
            else if (sql)
                builder.AppendLine(line);
            
            line = await reader.ReadLineAsync();
        }

        return builder.ToString();
    }
}