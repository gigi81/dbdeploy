using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle;

public record OracleObjectDependencies(DbObject DbObject, DbObject DbObjectDependency);