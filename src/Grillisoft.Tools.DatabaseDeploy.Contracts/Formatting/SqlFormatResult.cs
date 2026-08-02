namespace Grillisoft.Tools.DatabaseDeploy.Contracts.Formatting;

/// <summary>
/// The outcome of formatting one script.
/// </summary>
/// <param name="Sql">
/// The formatted SQL, or the untouched input when <paramref name="VerificationError"/> is set.
/// </param>
/// <param name="VerificationError">
/// Set when re-lexing the output did not produce the same significant tokens as the input. A
/// re-flow formatter that loses or invents a token has corrupted the script, so the caller must
/// leave the file alone and report the failure rather than write the result.
/// </param>
public sealed record SqlFormatResult(string Sql, string? VerificationError = null)
{
    public bool Verified => VerificationError is null;
}
