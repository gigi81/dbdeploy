using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Database;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle.Ddl;

/// <summary>
/// <see cref="DbObjectsGraph"/> using the Oracle script order: types, sequences and tables first,
/// program units in the middle, indexes, foreign keys and triggers last.
/// </summary>
public sealed class OracleObjectsGraph : DbObjectsGraph
{
    public OracleObjectsGraph(
        IEnumerable<DbObject> dbObjects,
        IEnumerable<OracleObjectDependencies> dbObjectsDependencies,
        ILogger? logger = null)
        : base(
            dbObjects,
            dbObjectsDependencies.Select(d => (d.DbObject, d.DbObjectDependency)),
            OracleObjectType.RankOf,
            logger)
    {
    }
}
