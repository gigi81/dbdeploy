using System.IO.Abstractions;
using System.Text;
using EditorConfig.Core;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.Formatting;

/// <summary>
/// Turns a script's own <c>.editorconfig</c> into formatter options. The chain is resolved from the
/// directory of each script, so the settings come from the repository being deployed rather than
/// from wherever the tool happens to be installed.
/// </summary>
public sealed class EditorConfigSqlOptions
{
    private const string Prefix = "dbdeploy_sql_";

    private readonly EditorConfigParser _parser;
    private readonly ILogger _logger;

    public EditorConfigSqlOptions(IFileSystem fileSystem, ILogger logger)
    {
        // One parser for the whole run: it caches the resolved chain per directory.
        _parser = new EditorConfigParser(fileSystem);
        _logger = logger;
    }

    /// <param name="detectedNewLine">
    /// The line ending the file already uses, applied when <c>end_of_line</c> says nothing. Leaving
    /// a file's endings alone unless asked keeps the diff to the parts that were reformatted.
    /// </param>
    public SqlFormatterOptions For(string path, string? detectedNewLine = null)
    {
        var configuration = _parser.Parse(path);
        var options = SqlFormatterOptions.Default;

        return options with
        {
            Indent = Indent(configuration, options.Indent),
            NewLine = NewLine(configuration) ?? detectedNewLine ?? options.NewLine,
            Encoding = CharacterSet(configuration) ?? options.Encoding,
            MaxLineLength = configuration.MaxLineLength ?? options.MaxLineLength,
            InsertFinalNewline = configuration.InsertFinalNewline ?? options.InsertFinalNewline,
            TrimTrailingWhitespace = configuration.TrimTrailingWhitespace ?? options.TrimTrailingWhitespace,
            Enabled = Flag(configuration, "enabled", options.Enabled),
            KeywordCase = Case(configuration, "keyword_case", options.KeywordCase),
            DataTypeCase = Case(configuration, "data_type_case", Case(configuration, "keyword_case", options.DataTypeCase)),
            FunctionCase = Case(configuration, "function_case", options.FunctionCase),
            BatchSeparatorCase = Case(configuration, "batch_separator_case", options.BatchSeparatorCase),
            BlankLinesBetweenStatements = Number(
                configuration,
                "blank_lines_between_statements",
                options.BlankLinesBetweenStatements)
        };
    }

    private static string Indent(FileConfiguration configuration, string fallback)
    {
        if (configuration.IndentStyle == IndentStyle.Tab)
            return "\t";

        var size = configuration.IndentSize?.NumberOfColumns ?? configuration.TabWidth;

        return size is > 0 ? new string(' ', size.Value) : fallback;
    }

    private static string? NewLine(FileConfiguration configuration) => configuration.EndOfLine switch
    {
        EndOfLine.CRLF => "\r\n",
        EndOfLine.LF => "\n",
        EndOfLine.CR => "\r",
        _ => null
    };

    private static Encoding? CharacterSet(FileConfiguration configuration) => configuration.Charset switch
    {
        Charset.UTF8 => new UTF8Encoding(false),
        Charset.UTF8BOM => new UTF8Encoding(true),
        Charset.Latin1 => Encoding.Latin1,
        Charset.UTF16LE => new UnicodeEncoding(false, true),
        Charset.UTF16BE => new UnicodeEncoding(true, true),
        _ => null
    };

    private SqlCase Case(FileConfiguration configuration, string key, SqlCase fallback)
    {
        var value = Value(configuration, key);
        if (value is null)
            return fallback;

        if (Enum.TryParse<SqlCase>(value, ignoreCase: true, out var parsed))
            return parsed;

        Warn(configuration, key, value, "expected upper, lower or preserve");
        return fallback;
    }

    private bool Flag(FileConfiguration configuration, string key, bool fallback)
    {
        var value = Value(configuration, key);
        if (value is null)
            return fallback;

        if (bool.TryParse(value, out var parsed))
            return parsed;

        Warn(configuration, key, value, "expected true or false");
        return fallback;
    }

    private int Number(FileConfiguration configuration, string key, int fallback)
    {
        var value = Value(configuration, key);
        if (value is null)
            return fallback;

        if (int.TryParse(value, out var parsed) && parsed >= 0)
            return parsed;

        Warn(configuration, key, value, "expected a number of zero or more");
        return fallback;
    }

    private static string? Value(FileConfiguration configuration, string key) =>
        configuration.Properties.TryGetValue(Prefix + key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;

    private void Warn(FileConfiguration configuration, string key, string value, string expectation) =>
        _logger.LogWarning(
            "Ignoring {Key} = {Value} for {Path}: {Expectation}",
            Prefix + key,
            value,
            configuration.FileName,
            expectation);
}
