namespace Grillisoft.Tools.DatabaseDeploy.MySql.Tests;

/// <summary>
/// Server options the test containers are started with.
/// </summary>
internal static class MySqlServerOptions
{
    /// <summary>
    /// Lets a non <c>SUPER</c> user create a stored function while binary logging is on, which both
    /// images have on by default. Without it the schema the DDL tests script cannot be built in the
    /// first place - and a stored function is not optional there, since it is what the generated
    /// script has to wrap in a delimiter of its own.
    /// </summary>
    public const string TrustFunctionCreators = "--log-bin-trust-function-creators=1";
}
