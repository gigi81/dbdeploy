using Grillisoft.Tools.DatabaseDeploy.Database.Ddl;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle.Ddl;

/// <summary>
/// Thrown when the generated script is incomplete because one or more objects of a schema could not
/// be scripted. The script is written out in full before this is raised, with the failures repeated
/// in it as comments, so it can be inspected and fixed by hand.
/// </summary>
public class OracleDdlGenerationException(string schema, IEnumerable<(string Object, string Error)> failures)
    : DdlGenerationException(schema, "schema", failures);
