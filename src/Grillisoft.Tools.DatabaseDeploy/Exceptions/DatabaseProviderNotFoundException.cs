namespace Grillisoft.Tools.DatabaseDeploy.Exceptions;

public class DatabaseProviderNotFoundException : Exception
{
    private readonly string _providerName;
    private readonly string _databaseName;

    public DatabaseProviderNotFoundException(string providerName, string databaseName)
    {
        _providerName = providerName;
        _databaseName = databaseName;
    }

    public override string Message => $"Could not find database factory of type '{_providerName}' for database '{_databaseName}'";
}