using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy.Exceptions;

public class HookScriptNotFoundException : Exception
{
    private readonly HookScript _script;

    public HookScriptNotFoundException(HookScript script)
    {
        _script = script;
    }

    public override string Message => _script.NotFoundMessage;
}
