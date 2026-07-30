namespace Grillisoft.Tools.DatabaseDeploy.Exceptions;

/// <summary>
/// Thrown when formatting a folder of scripts that sits outside any known database layout and no
/// dialect was given to fall back on.
/// </summary>
public class SqlDialectNotFoundException : Exception
{
    private readonly string _provider;
    private readonly string _known;

    public SqlDialectNotFoundException(string provider, IEnumerable<string> known)
    {
        _provider = provider;
        _known = string.Join(", ", known);
    }

    public override string Message =>
        string.IsNullOrWhiteSpace(_provider)
            ? $"Could not tell which SQL dialect to format with. Pass --provider with one of: {_known}"
            : $"Unknown provider '{_provider}'. Known providers are: {_known}";
}
