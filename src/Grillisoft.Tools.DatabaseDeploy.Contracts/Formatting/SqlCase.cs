namespace Grillisoft.Tools.DatabaseDeploy.Contracts.Formatting;

/// <summary>
/// How a class of words is cased by the formatter. Only words the dialect recognises are ever
/// re-cased; identifiers, quoted identifiers and string literals are always left alone.
/// </summary>
public enum SqlCase
{
    Preserve,
    Upper,
    Lower
}
