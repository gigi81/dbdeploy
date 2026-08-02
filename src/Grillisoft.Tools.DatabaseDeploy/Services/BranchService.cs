using Grillisoft.Tools.DatabaseDeploy.Options;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy.Services;

/// <summary>
/// A service that works on a branch, and so knows which branch it is about and what runs around
/// the work it does.
/// </summary>
public abstract class BranchService : BaseService
{
    private readonly BranchOptions _branchOptions;
    private readonly Lazy<DatabaseHooksRunner> _hooks;

    protected BranchService(BranchOptions options, ServiceDependencies dependencies, ILogger logger)
        : base(dependencies, logger)
    {
        _branchOptions = options;
        _hooks = new Lazy<DatabaseHooksRunner>(CreateHooksRunner);
    }

    /// <summary>
    /// The branch that was asked for, or the default one when the command line said nothing.
    /// </summary>
    protected string Branch => !string.IsNullOrWhiteSpace(_branchOptions.Branch)
        ? _branchOptions.Branch
        : _globalSettings.Value.DefaultBranch;

    /// <summary>
    /// The scripts that run around the work of the service. Built on first use, so a service that
    /// runs none never builds one.
    /// </summary>
    protected DatabaseHooksRunner Hooks => _hooks.Value;

    private DatabaseHooksRunner CreateHooksRunner()
    {
        return new DatabaseHooksRunner(
            _databases,
            _branchOptions.DryRun,
            _scripts,
            _dbl);
    }
}
