using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy.Exceptions;

public class DbObjectNotFoundException : Exception
{
    private readonly DbObject _obj;

    public DbObjectNotFoundException(DbObject obj)
    {
        _obj = obj;
    }

    public override string Message => $"Database Object {_obj.Name} of type {_obj.Type} not found";
}