using AutoAgentes.Contracts;
using FluentValidation;

namespace AutoAgentes.Api;

public class CreateMcpServerRequestValidator : AbstractValidator<CreateMcpServerRequest>
{
    public CreateMcpServerRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.BaseUrl).NotEmpty();
        RuleFor(x => x.AuthType).Must(a => new[] { "none", "apikey", "oauth" }.Contains(a)).WithMessage("Invalid authType");
    }
}

public class CreateAgentRequestValidator : AbstractValidator<CreateAgentRequest>
{
    public CreateAgentRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.SystemPrompt).NotEmpty();
        RuleFor(x => x.Autonomy).Must(a => new[] { "Manual", "Supervised", "Auto" }.Contains(a)).WithMessage("Invalid autonomy");
    }
}

public static class ValidationRegistration
{
    public static IServiceCollection AddRequestValidation(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<CreateMcpServerRequestValidator>();
        return services;
    }
}


