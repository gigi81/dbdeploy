using Grillisoft.Tools.DatabaseDeploy.Database.Ddl;

namespace Grillisoft.Tools.DatabaseDeploy.MySql.Ddl;

/// <summary>
/// Thrown when the generated script is incomplete because one or more objects of a database could
/// not be scripted. The script is written out in full before this is raised, with the failures
/// repeated in it as comments, so it can be inspected and fixed by hand.
/// </summary>
public class MySqlDdlGenerationException(string database, IEnumerable<(string Object, string Error)> failures)
    : DdlGenerationException(database, "database", failures);
