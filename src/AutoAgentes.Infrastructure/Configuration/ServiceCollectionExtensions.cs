using AutoAgentes.Application.Abstractions;
using AutoAgentes.App.Configuration;
using AutoAgentes.Infrastructure.SemanticKernel;
using Microsoft.Extensions.DependencyInjection;

namespace AutoAgentes.Infrastructure.Configuration;

/// <summary>
/// Extensiones para registrar servicios de infraestructura
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra las opciones tipadas de configuración
    /// </summary>
    public static IServiceCollection AddTypedOptions(this IServiceCollection services)
    {
        services.AddOptions<KernelOptions>()
            .BindConfiguration("Kernel")
            .ValidateOnStart();
            
        services.AddOptions<PlannerOptions>()
            .BindConfiguration("Planner")
            .ValidateOnStart();
            
        services.AddOptions<TimeoutsOptions>()
            .BindConfiguration("Timeouts")
            .ValidateOnStart();
            
        services.AddOptions<GovernanceOptions>()
            .BindConfiguration("Governance")
            .ValidateOnStart();
            
        services.AddOptions<ProviderOptions>()
            .BindConfiguration("Providers")
            .ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Registra los servicios de Semantic Kernel
    /// </summary>
    public static IServiceCollection AddSemanticKernelServices(this IServiceCollection services)
    {
        services.AddScoped<IKernelBuilder, KernelBuilder>();
        services.AddScoped<IKernelAugmentor, KernelAugmentor>();
        services.AddScoped<IGovernancePolicy, GovernancePolicy>();
        
        // TODO: Implementar IKernelCache cuando sea necesario
        // services.AddScoped<IKernelCache, KernelCache>();

        return services;
    }
}
