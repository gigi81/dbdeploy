using System.IO.Abstractions.TestingHelpers;
using System.Text;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Grillisoft.Tools.DatabaseDeploy.Formatting;

namespace Grillisoft.Tools.DatabaseDeploy.Tests.Formatting;

public class EditorConfigSqlOptionsTests
{
    private const string ScriptPath = "/repo/db/TKT001.Deploy.sql";

    private static SqlFormatterOptions Resolve(string? editorConfig, string? detectedNewLine = null)
    {
        var files = new Dictionary<string, MockFileData>
        {
            [ScriptPath] = new("SELECT 1")
        };

        if (editorConfig is not null)
            files["/repo/.editorconfig"] = new("root = true\n\n" + editorConfig);

        var fileSystem = new MockFileSystem(files);

        return new EditorConfigSqlOptions(fileSystem, TestLogger.Instance).For(ScriptPath, detectedNewLine);
    }

    [Test]
    public void For_WhenThereIsNoEditorConfig_ShouldUseTheDefaults()
    {
        var options = Resolve(null);

        options.Should().BeEquivalentTo(SqlFormatterOptions.Default);
    }

    [Test]
    public void For_ShouldTakeTheIndentFromTheCoreProperties()
    {
        Resolve("[*.sql]\nindent_style = space\nindent_size = 2\n").Indent.Should().Be("  ");
        Resolve("[*.sql]\nindent_style = tab\n").Indent.Should().Be("\t");
    }

    [Test]
    [Arguments("lf", "\n")]
    [Arguments("crlf", "\r\n")]
    [Arguments("cr", "\r")]
    public void For_ShouldTakeTheNewLineFromEndOfLine(string setting, string expected)
    {
        Resolve($"[*.sql]\nend_of_line = {setting}\n").NewLine.Should().Be(expected);
    }

    /// <summary>
    /// With nothing configured the file keeps the endings it already has, so formatting does not
    /// rewrite every line of a script it was only meant to lay out.
    /// </summary>
    [Test]
    public void For_WhenEndOfLineIsNotSet_ShouldKeepTheEndingsTheFileAlreadyHas()
    {
        Resolve("[*.sql]\nindent_size = 4\n", detectedNewLine: "\r\n").NewLine.Should().Be("\r\n");
    }

    [Test]
    public void For_ShouldTakeTheEncodingFromCharset()
    {
        Resolve("[*.sql]\ncharset = utf-8-bom\n")
            .Encoding.GetPreamble().Should().NotBeEmpty();

        Resolve("[*.sql]\ncharset = utf-8\n")
            .Encoding.GetPreamble().Should().BeEmpty();
    }

    [Test]
    public void For_ShouldReadTheDbDeployProperties()
    {
        var options = Resolve(
            """
            [*.sql]
            max_line_length = 60
            insert_final_newline = false
            trim_trailing_whitespace = false
            dbdeploy_sql_keyword_case = lower
            dbdeploy_sql_function_case = preserve
            dbdeploy_sql_batch_separator_case = lower
            dbdeploy_sql_blank_lines_between_statements = 2

            """);

        options.MaxLineLength.Should().Be(60);
        options.InsertFinalNewline.Should().BeFalse();
        options.TrimTrailingWhitespace.Should().BeFalse();
        options.KeywordCase.Should().Be(SqlCase.Lower);
        options.FunctionCase.Should().Be(SqlCase.Preserve);
        options.BatchSeparatorCase.Should().Be(SqlCase.Lower);
        options.BlankLinesBetweenStatements.Should().Be(2);
    }

    /// <summary>Data types follow the keyword casing unless they are given one of their own.</summary>
    [Test]
    public void For_WhenOnlyTheKeywordCaseIsSet_ShouldApplyItToDataTypesToo()
    {
        Resolve("[*.sql]\ndbdeploy_sql_keyword_case = lower\n").DataTypeCase.Should().Be(SqlCase.Lower);

        Resolve("[*.sql]\ndbdeploy_sql_keyword_case = lower\ndbdeploy_sql_data_type_case = upper\n")
            .DataTypeCase.Should().Be(SqlCase.Upper);
    }

    /// <summary>The per-glob escape hatch for scripts that must not be touched.</summary>
    [Test]
    public void For_WhenDisabledForTheGlob_ShouldReportItDisabled()
    {
        Resolve("[*.sql]\ndbdeploy_sql_enabled = false\n").Enabled.Should().BeFalse();
    }

    [Test]
    public void For_WhenAValueIsNotUnderstood_ShouldFallBackToTheDefault()
    {
        var options = Resolve(
            "[*.sql]\ndbdeploy_sql_keyword_case = shouty\ndbdeploy_sql_blank_lines_between_statements = -1\n");

        options.KeywordCase.Should().Be(SqlFormatterOptions.Default.KeywordCase);
        options.BlankLinesBetweenStatements.Should().Be(SqlFormatterOptions.Default.BlankLinesBetweenStatements);
    }

    /// <summary>A section that does not match a .sql file must not reach the formatter.</summary>
    [Test]
    public void For_WhenTheSectionIsForAnotherGlob_ShouldIgnoreIt()
    {
        Resolve("[*.cs]\nindent_size = 2\ndbdeploy_sql_keyword_case = lower\n")
            .Should().BeEquivalentTo(SqlFormatterOptions.Default);
    }
}
