using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy.Oracle;

public class OracleObjectsGraph
{
    private readonly ICollection<OracleObjectDependencies> _dbObjectsDependencies;
    private readonly HashSet<DbObject> _dbObjects;

    public OracleObjectsGraph(ICollection<DbObject> dbObjects, ICollection<OracleObjectDependencies> dbObjectsDependencies)
    {
        _dbObjectsDependencies = dbObjectsDependencies;
        _dbObjects = dbObjects.ToHashSet();
    }

    public ICollection<DbObject> GetGraph()
    {
        MatchDependencies(_dbObjectsDependencies);
        
        var result = new List<DbObject>();

        while (result.Count != _dbObjects.Count)
        {
            var count = 0;
            
            foreach (var dbObjectType in OracleDatabase.OracleObjectTypes)
            {
                var objects = _dbObjects
                    .Where(o => o.Type == dbObjectType && o.Dependencies.Count == 0)
                    .ToHashSet();
                
                count += objects.Count;
                result.AddRange(objects.OrderBy(o => o.Name));
                RemoveDependencies(objects);
            }
            
            if (count == 0)
            {
                var list = String.Join(",", _dbObjects.Except(result).Select(o => o.Name));
                throw new Exception("Circular dependency detected: " + list);
            }
        }
        
        return result;
    }

    private void RemoveDependencies(HashSet<DbObject> objects)
    {
        foreach (var dbObject in _dbObjects)
        {
            dbObject.Dependencies.RemoveAll(objects.Contains);
        }
    }

    private void MatchDependencies(ICollection<OracleObjectDependencies> dbObjectsDependencies)
    {
        foreach (var dependency in dbObjectsDependencies)
        {
            if(!_dbObjects.TryGetValue(dependency.DbObject, out var dbObject))
                throw new Exception($"DB object not found: {dependency.DbObject}");
            
            if(!_dbObjects.TryGetValue(dependency.DbObjectDependency, out var dbObjectDependency))
                throw new Exception($"DB object not found: {dependency.DbObjectDependency}");
            
            dbObject.Dependencies.Add(dbObjectDependency);
        }
    }
}