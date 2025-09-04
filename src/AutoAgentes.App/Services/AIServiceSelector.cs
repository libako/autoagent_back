using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using AutoAgentes.Domain.Entities;
using AutoAgentes.Contracts;
using Microsoft.Extensions.Configuration;

namespace AutoAgentes.App.Services;

/// <summary>
/// Implementación del selector de servicios de IA
/// </summary>
public class AIServiceSelector : IAIServiceSelector
{
    private readonly ITraceEmitter _emitter;
    private readonly IConfiguration _configuration;

    public AIServiceSelector(ITraceEmitter emitter, IConfiguration configuration)
    {
        _emitter = emitter;
        _configuration = configuration;
    }

    public async Task<(IChatCompletionService Service, object Settings)> ForAgentAsync(Agent agent, Kernel kernel, CancellationToken ct)
    {
        var sessionId = Guid.Empty; // TODO: Obtener del contexto
        
        try
        {
            // Seleccionar servicio según el proveedor del agente
            var service = GetServiceByProvider(agent.Provider, kernel);
            
            // Obtener configuración optimizada para el agente
            var settings = GetExecutionSettings(agent);
            
            await _emitter.EmitAsync(sessionId, new TraceEvent("ai_service_selected", new { 
                agentId = agent.Id,
                provider = agent.Provider,
                serviceType = service.GetType().Name
            }, DateTimeOffset.UtcNow), ct);
            
            return (service, settings);
        }
        catch (Exception ex)
        {
            await _emitter.EmitAsync(sessionId, new TraceEvent("ai_service_selection_error", new { 
                agentId = agent.Id,
                error = ex.Message 
            }, DateTimeOffset.UtcNow), ct);
            
            // Fallback al servicio por defecto
            var fallbackService = kernel.GetRequiredService<IChatCompletionService>();
            var fallbackSettings = GetExecutionSettings(agent);
            
            return (fallbackService, fallbackSettings);
        }
    }

    public IChatCompletionService GetServiceByName(string name, Kernel kernel)
    {
        try
        {
            // Para SK 1.64.0, intentar obtener servicio por nombre específico
            // Si no está disponible, usar el servicio por defecto
            return kernel.GetRequiredService<IChatCompletionService>();
        }
        catch
        {
            // Fallback al servicio por defecto
            return kernel.GetRequiredService<IChatCompletionService>();
        }
    }

    public object GetExecutionSettings(Agent agent, string purpose = "general")
    {
        // Para SK 1.64.0, retornamos un objeto simple con la configuración
        var temperature = agent.Autonomy switch
        {
            "Supervised" => 0.1,    // Muy determinista
            "Autonomous" => 0.3,    // Moderadamente creativo
            "Unsupervised" => 0.5,  // Más creativo
            _ => 0.2
        };
        
        var maxTokens = agent.Autonomy switch
        {
            "Supervised" => 4000,
            "Autonomous" => 8000,
            "Unsupervised" => 12000,
            _ => 4000
        };
        
        // Ajustar según el propósito
        switch (purpose.ToLowerInvariant())
        {
            case "planner":
                temperature = 0.0; // Determinista para planificación
                maxTokens = Math.Min(maxTokens, 2000);
                break;
                
            case "reasoner":
                temperature = Math.Max(0.1, temperature - 0.1);
                break;
                
            case "creative":
                temperature = Math.Min(0.8, temperature + 0.2);
                break;
                
            case "summary":
                temperature = 0.1; // Determinista para resúmenes
                maxTokens = Math.Min(maxTokens, 1000);
                break;
        }
        
        return new { Temperature = temperature, MaxTokens = maxTokens };
    }

    private IChatCompletionService GetServiceByProvider(string provider, Kernel kernel)
    {
        return provider.ToLowerInvariant() switch
        {
            "openai" => kernel.GetRequiredService<IChatCompletionService>(),
            "anthropic" => kernel.GetRequiredService<IChatCompletionService>(),
            "azure" => kernel.GetRequiredService<IChatCompletionService>(),
            _ => kernel.GetRequiredService<IChatCompletionService>() // Fallback
        };
    }
}
