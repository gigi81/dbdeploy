namespace Grillisoft.Tools.DatabaseDeploy.Exceptions;

public class DatabaseConfigNotFoundException : Exception
{
    private readonly string _databaseName;

    public DatabaseConfigNotFoundException(string databaseName)
    {
        _databaseName = databaseName;
    }

    public override string Message => $"Database configuration for '{_databaseName}' was not found.";
}