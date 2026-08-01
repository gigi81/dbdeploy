namespace Grillisoft.Tools.DatabaseDeploy.Contracts;

/// <summary>
/// The points around a deploy or a rollback where an optional script can be run.
/// </summary>
public enum DatabaseHook
{
    PreDeploy,
    PostDeploy,
    PreRollback,
    PostRollback
}
