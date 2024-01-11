using FluentValidation;

namespace Grillisoft.Tools.DatabaseDeploy.Contracts.Validators;

public class DatabaseMigrationValidator : AbstractValidator<DatabaseMigration>
{
    public DatabaseMigrationValidator()
    {
        RuleFor(m => m.Name)
            .NotEmpty()
            .MinimumLength(2)
            .MaximumLength(255);

        RuleFor(m => m.User)
            .NotEmpty()
            .MinimumLength(2)
            .MaximumLength(100);

        RuleFor(m => m.Hash)
            .Length(32);
    }
}