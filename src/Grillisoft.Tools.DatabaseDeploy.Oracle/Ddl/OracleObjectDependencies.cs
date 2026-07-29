using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle.Ddl;

public record OracleObjectDependencies(DbObject DbObject, DbObject DbObjectDependency);