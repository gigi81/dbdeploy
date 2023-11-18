using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy;

public static class BranchLoader
{
    private const string IncludeKeyword = "@include ";

    public static IEnumerable<Branch> LoadAll(IDirectoryInfo directory)
    {
        return directory.EnumerateFiles("*.csv", SearchOption.TopDirectoryOnly)
                        .Select(Load);
    }
    
    public static Branch Load(IFileInfo file)
    {
        return LoadInternal(file, new Stack<string>());
    }

    public static IFileInfo GetBranchFile(string branchName, IDirectoryInfo directory)
    {
        branchName = branchName.Replace('/', '_');
        return directory.File($"{branchName}.csv");
    }

    private static Branch LoadInternal(IFileInfo file, Stack<string> files)
    {
        CheckForCircularIncludes(file, files);

        var directory = file.Directory ?? file.FileSystem.CurrentDirectory();
        var steps = new List<Step>();
        var count = 1;

        foreach (var l in file.EnumerateLines())
        {
            if (string.IsNullOrWhiteSpace(l))
            {
                continue; //empty line
            }

            var line = l.Trim();
            if (line.StartsWith('#'))
            {
                continue; //comment
            }

            if (line.StartsWith(IncludeKeyword))
            {
                var includeFile = GetBranchFile(line.Substring(IncludeKeyword.Length + 1), directory);
                var includeBranch = LoadInternal(includeFile, files);
                steps.AddRange(includeBranch.Steps);
                continue;
            }

            steps.Add(ReadStep(line, count++, directory));
        }

        return new Branch(GetBranchName(file), steps);
    }

    private static string GetBranchName(IFileInfo file)
    {
        var name = file.Name;
        var index = name.IndexOf('.');
        if (index >= 0)
            name = name.Substring(0, index);

        name = name.Replace('_', '/');
        return name;
    }

    private static void CheckForCircularIncludes(IFileInfo file, Stack<string> files)
    {
        file.ThrowIfNotFound();
        files.Push(file.FullName);

        //check for duplicate files in the stack (circular include)
        var duplicateFile = files
            .GroupBy(f => f)
            .FirstOrDefault(g => g.Count() > 1)
            ?.FirstOrDefault();

        if (!string.IsNullOrEmpty(duplicateFile))
            throw new Exception($"Circular include detected for file {duplicateFile}");
    }

    private static Step ReadStep(string line, int count, IDirectoryInfo directory)
    {
        var split = line.Split(',');
        if (split.Length != 2)
            throw new ArgumentException($"Invalid format '{line}' on line {count}");

        return new Step(split[0], split[1], directory);
    }
}
