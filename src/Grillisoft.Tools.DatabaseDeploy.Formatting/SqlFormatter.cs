using System.IO.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using SqlParser;
using SqlParser.Dialects;

namespace Grillisoft.Tools.DatabaseDeploy.Formatting;

public class SqlFormatter : ISqlFormatter
{
    public async Task FormatSql(IFileInfo sqlFile, CancellationToken cancellationToken)
    {
        var sql = await sqlFile.ReadAllTextAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(sql))
            return;
        
        var statements =  new Parser().ParseSql(sql, new MsSqlDialect());

        await using var stream = sqlFile.Open(FileMode.Truncate, FileAccess.Write, FileShare.None);
        await using var writer = new StreamWriter(stream);
        foreach (var statement in statements)
        {
            await writer.WriteAsync(statement.ToSql());
        }
    }
}
