using Xunit;

namespace Grillisoft.Tools.DatabaseDeploy.Tests.Formatting;

/// <summary>
/// Finds the example scripts checked into the repository, so the formatter is exercised against
/// real migrations rather than only against hand-written cases.
/// </summary>
public static class CorpusFiles
{
    private static readonly string Root = FindRepositoryRoot();

    public static TheoryData<string> For(params string[] examples)
    {
        var data = new TheoryData<string>();

        foreach (var example in examples)
        {
            var directory = Path.Combine(Root, "examples", example);
            if (!Directory.Exists(directory))
                continue;

            foreach (var file in Directory.EnumerateFiles(directory, "*.sql", SearchOption.AllDirectories))
                data.Add(Path.GetRelativePath(Root, file));
        }

        return data;
    }

    public static string Resolve(string relativePath) => Path.Combine(Root, relativePath);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "examples")))
            directory = directory.Parent;

        return directory?.FullName
               ?? throw new InvalidOperationException("Could not locate the repository root from the test output directory");
    }
}
