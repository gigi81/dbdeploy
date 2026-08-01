using Grillisoft.Tools.DatabaseDeploy.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.Database.Formatting;

/// <summary>
/// Lays a script out again from its tokens. Every result is checked by
/// <see cref="SqlFormatVerifier"/> before it is handed back, so a bug here costs a skipped file
/// rather than a corrupted migration.
/// </summary>
public class SqlReflowFormatter : ISqlFormatter
{
    private readonly SqlDialect _dialect;

    public SqlReflowFormatter(SqlDialect dialect)
    {
        _dialect = dialect;
    }

    public string Dialect => _dialect.Name;

    public SqlFormatResult Format(string sql, SqlFormatterOptions options)
    {
        if (!options.Enabled || string.IsNullOrWhiteSpace(sql))
            return new SqlFormatResult(sql);

        var lexer = new SqlLexer(_dialect);
        var source = lexer.Tokenize(sql);
        var formatted = new SqlEmitter(_dialect, options).Emit(source);

        var error = SqlFormatVerifier.Verify(source, lexer.Tokenize(formatted));

        return error is null
            ? new SqlFormatResult(formatted)
            : new SqlFormatResult(sql, error);
    }
}
