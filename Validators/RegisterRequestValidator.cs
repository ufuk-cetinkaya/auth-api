namespace AuthService.Validators;

using AuthService.Models;
using FluentValidation;

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().EmailAddress()
            .MaximumLength(256);

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(12)
            .Matches("[A-Z]").WithMessage("En az bir büyük harf içermeli.")
            .Matches("[a-z]").WithMessage("En az bir küçük harf içermeli.")
            .Matches("[0-9]").WithMessage("En az bir rakam içermeli.")
            .Matches("[^a-zA-Z0-9]").WithMessage("En az bir özel karakter içermeli.");

        RuleFor(x => x.FirstName)
            .NotEmpty().MaximumLength(100);

        RuleFor(x => x.LastName)
            .NotEmpty().MaximumLength(100);
    }
}
