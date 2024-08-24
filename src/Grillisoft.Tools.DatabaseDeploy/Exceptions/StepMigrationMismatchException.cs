using Grillisoft.Tools.DatabaseDeploy.Contracts;

namespace Grillisoft.Tools.DatabaseDeploy.Exceptions;

public class StepMigrationMismatchException : Exception
{
    private readonly Step _step;
    private readonly DatabaseMigration _migration;

    public StepMigrationMismatchException(Step step, DatabaseMigration migration)
    {
        _step = step;
        _migration = migration;
    }

    public override string Message => $"Expected step {_step.Name} on database {_step.Database} but found {_migration.Name}";
}