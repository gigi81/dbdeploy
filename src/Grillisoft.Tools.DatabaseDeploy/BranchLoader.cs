using System;
using System.IO.Abstractions;
using System.Linq;
using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy;

public static class BranchLoader
{
    public static Branch Load(IFileInfo _file)
    {
        _file.ThrowIfNotFound();

        var steps = _file.EnumerateLines()
            .Select((l, i) => ReadStep(l, i + 1))
            .Where(s => s != null)
            .ToArray();

        return new Branch(_file.Name, steps);
    }

    private static Step? ReadStep(string line, int count)
    {
        if (String.IsNullOrWhiteSpace(line))
            return null;

        if (line.Trim().StartsWith('#'))
            return null;

        //TODO: add support for @include

        var split = line.Split(',');
        if (split.Length != 2)
            throw new ArgumentException($"Invalid format '{line}' on line {count}");

        return new Step(split[0], split[1]);
    }
}
