using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using AutoAgentes.Domain.Entities;

namespace AutoAgentes.App.Services;

/// <summary>
/// Interfaz para seleccionar servicios de IA según el agente
/// </summary>
public interface IAIServiceSelector
{
    /// <summary>
    /// Obtiene el servicio de chat y configuración para un agente
    /// </summary>
    Task<(IChatCompletionService Service, object Settings)> ForAgentAsync(Agent agent, Kernel kernel, CancellationToken ct);
    
    /// <summary>
    /// Obtiene el servicio de chat por nombre
    /// </summary>
    IChatCompletionService GetServiceByName(string name, Kernel kernel);
    
    /// <summary>
    /// Obtiene la configuración de ejecución para un propósito específico
    /// </summary>
    object GetExecutionSettings(Agent agent, string purpose = "general");
}
