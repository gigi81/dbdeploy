namespace Grillisoft.Tools.DatabaseDeploy.Exceptions;

public class DatabasesNotFoundException : Exception
{
    private readonly IReadOnlyCollection<string> _missingDatabases;

    public DatabasesNotFoundException(IReadOnlyCollection<string> missingDatabases)
    {
        _missingDatabases = missingDatabases;
    }

    public override string Message => $"Databases not found: {string.Join(", ", _missingDatabases)}";
}