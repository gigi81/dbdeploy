using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Grillisoft.Tools.DatabaseDeploy.Exceptions;
using Microsoft.Extensions.Logging;

namespace Grillisoft.Tools.DatabaseDeploy;

public class Strategy
{
    private readonly Step[] _steps;
    private readonly IDictionary<string, DatabaseMigration[]> _migrations;
    private readonly ILogger _logger;

    public Strategy(
        Step[] steps,
        IDictionary<string, DatabaseMigration[]> migrations,
        ILogger logger)
    {
        _steps = steps;
        _migrations = migrations;
        _logger = logger;
    }

    public IEnumerable<Step> GetDeploySteps(string branch)
    {
        var migrations = GetMigrationsQueues();
        
        foreach (var step in _steps)
        {
            if (!IsStepDeployed(step, migrations[step.Database], out _))
            {
                yield return step;
            }
            else
            {
                //TODO: check hash and log warning if different
                
                if(step.Branch.EqualsIgnoreCase(branch))
                    _logger.LogInformation($"Database {step.Database} Step {step.Name} already deployed");
            }
        }
    }
    
    public IEnumerable<(Step, DatabaseMigration)> GetRollbackSteps(string branch)
    {
        foreach (var (step, migration) in GetRollbackStepsInternal().Reverse())
        {
            if (!step.Branch.EqualsIgnoreCase(branch))
                yield break;
            
            yield return (step, migration);
        }
    }

    private IEnumerable<(Step, DatabaseMigration)> GetRollbackStepsInternal()
    {
        var migrations = GetMigrationsQueues();
        
        foreach (var step in _steps)
        {
            if (!IsStepDeployed(step, migrations[step.Database], out var migration) || migration == null)
            {
                _logger.LogInformation($"Database {step.Database} Step {step.Name} was not deployed. Skipping rollback");
                continue;
            }
            
            //we do not rollback the init step
            if(step.IsInit)
                continue;

            yield return (step, migration);
        }
    }

    private IDictionary<string, Queue<DatabaseMigration>> GetMigrationsQueues()
    {
        return _migrations.ToDictionary(
            i => i.Key,
            i => new Queue<DatabaseMigration>(i.Value),
            StringComparer.InvariantCultureIgnoreCase);
    }

    private static bool IsStepDeployed(Step step, Queue<DatabaseMigration> migrations, out DatabaseMigration? migration)
    {
        if (!migrations.TryDequeue(out migration))
            return false;

        if (!migration.Name.EqualsIgnoreCase(step.Name))
            throw new StepMigrationMismatchException(step, migration);
            
        return true;
    }
}