using System.IO.Abstractions;
using System.Text;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.AI;

public class Generator : IGenerator
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<Generator> _logger;

    public Generator(IChatClient chatClient, ILogger<Generator> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task GenerateRollback(IFileInfo deployFile, IFileInfo rollbackFile, string dialect, CancellationToken cancellationToken)
    {
        var deployScript = await deployFile.ReadAllTextAsync(cancellationToken);
        var rollbackScript = await GenerateRollback(deployScript, dialect, cancellationToken);
        await rollbackFile.WriteAllTextAsync(rollbackScript, cancellationToken);
        _logger.LogWarning("*** AI Generation is EXPERIMENTAL. Remember to review to generated rollback script ***");
    }

    private const string RollbackPrompt = "Can you please create a rollback SQL script for the below {0} script. Remember to invert the order of the operations in the rollback script. \n\n{1}";

    private async Task<string> GenerateRollback(string script, string dialect, CancellationToken cancellationToken)
    {
        var prompt = string.Format(RollbackPrompt, dialect, script);
        var completion = await _chatClient.GetResponseAsync(prompt, cancellationToken: cancellationToken);

        using var reader = new StringReader(completion.Text);
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

            line = await reader.ReadLineAsync(cancellationToken);
        }

        return builder.ToString();
    }
}
