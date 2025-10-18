using AutoAgentes.Domain.Entities;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AutoAgentes.Application.Abstractions;

/// <summary>
/// Interfaz para crear planes de ejecución
/// </summary>
public interface IPlanCreator
{
    /// <summary>
    /// Crea un plan de ejecución basado en el historial de conversación
    /// </summary>
    /// <param name="kernel">Kernel para el LLM</param>
    /// <param name="conversationHistory">Historial de conversación</param>
    /// <param name="agent">Agente que ejecutará el plan</param>
    /// <param name="sessionId">ID de la sesión</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <returns>Plan con objetivo y pasos</returns>
    Task<(string Goal, IReadOnlyList<PlanStep> Steps, int RegistryVersion)> CreatePlanAsync(
        Microsoft.SemanticKernel.Kernel kernel, 
        ChatHistory conversationHistory, 
        Agent agent, 
        Guid sessionId, 
        CancellationToken cancellationToken);
}

/// <summary>
/// Paso de un plan de ejecución
/// </summary>
/// <param name="Tool">Nombre de la herramienta a ejecutar</param>
/// <param name="Args">Argumentos para la herramienta</param>
public sealed record PlanStep(string Tool, Dictionary<string, object>? Args);