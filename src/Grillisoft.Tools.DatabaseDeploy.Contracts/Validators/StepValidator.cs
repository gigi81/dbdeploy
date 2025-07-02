using FluentValidation;

namespace Grillisoft.Tools.DatabaseDeploy.Contracts.Validators;

public class StepValidator : AbstractValidator<Step>
{
    public StepValidator()
    {
        RuleFor(m => m.Name)
            .NotEmpty()
            .MinimumLength(2)
            .MaximumLength(255);

        RuleFor(m => m.Database)
            .NotEmpty()
            .MinimumLength(2)
            .MaximumLength(255);
    }
}