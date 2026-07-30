using AwesomeAssertions;
using Grillisoft.Tools.DatabaseDeploy.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Grillisoft.Tools.DatabaseDeploy.Tests.Formatting;

/// <summary>
/// Finds the example scripts checked into the repository, so the formatter is exercised against
/// real migrations rather than only against hand-written cases.
/// </summary>
public static class CorpusFiles
{
    /// <summary>
    /// Bulk data dumps are left out of the corpus. <c>examples/mysql-01/employees</c> alone holds
    /// 175 MB of <c>INSERT ... VALUES</c>; formatting it twice for the idempotency check cost four
    /// minutes on CI and covered nothing that the first few rows do not. Skipping them loses no
    /// coverage - the product never formats an init script in branch mode, and the schema init
    /// scripts stay in, Northwind's megabyte of DDL among them.
    /// </summary>
    private const long MaxBytes = 2 * 1024 * 1024;

    private static readonly string Root = FindRepositoryRoot();

    public static TheoryData<string> For(params string[] examples)
    {
        var data = new TheoryData<string>();

        foreach (var example in examples)
        {
            var directory = new DirectoryInfo(Path.Combine(Root, "examples", example));
            if (!directory.Exists)
                continue;

            foreach (var file in directory.EnumerateFiles("*.sql", SearchOption.AllDirectories))
            {
                if (file.Length <= MaxBytes)
                    data.Add(Path.GetRelativePath(Root, file.FullName));
            }
        }

        return data;
    }

    public static string Resolve(string relativePath) => Path.Combine(Root, relativePath);

    /// <summary>
    /// The two properties every example has to satisfy: the formatted script verifies against its
    /// source, and formatting it again changes nothing.
    /// </summary>
    public static async Task AssertFormatsCleanly(
        ISqlFormatter formatter,
        string relativePath,
        ITestOutputHelper output)
    {
        var sql = await File.ReadAllTextAsync(Resolve(relativePath));
        var options = SqlFormatterOptions.Default with { NewLine = "\n" };

        var first = formatter.Format(sql, options);
        var second = formatter.Format(first.Sql, options);

        // Only when something is wrong: writing every formatted script would put a copy of the
        // whole corpus into the test log.
        if (!first.Verified || !second.Verified || second.Sql != first.Sql)
            output.WriteLine(first.Sql);

        first.VerificationError.Should().BeNull();
        second.VerificationError.Should().BeNull();
        second.Sql.Should().Be(first.Sql, "formatting an already formatted script must change nothing");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "examples")))
            directory = directory.Parent;

        return directory?.FullName
               ?? throw new InvalidOperationException("Could not locate the repository root from the test output directory");
    }
}
