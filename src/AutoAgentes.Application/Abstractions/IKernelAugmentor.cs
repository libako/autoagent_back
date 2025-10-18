using AutoAgentes.Domain.Entities;

namespace AutoAgentes.Application.Abstractions;

/// <summary>
/// Interfaz para registrar herramientas y aplicar gobernanza a kernels
/// </summary>
public interface IKernelAugmentor
{
    /// <summary>
    /// Registra herramientas MCP en el kernel
    /// </summary>
    /// <param name="kernel">Kernel a configurar</param>
    /// <param name="agent">Agente con herramientas</param>
    /// <param name="sessionId">ID de la sesión</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    Task RegisterToolsAsync(Microsoft.SemanticKernel.Kernel kernel, Agent agent, Guid sessionId, CancellationToken cancellationToken);
    
    /// <summary>
    /// Aplica políticas de gobernanza al kernel
    /// </summary>
    /// <param name="kernel">Kernel a configurar</param>
    /// <param name="agent">Agente con configuración de gobernanza</param>
    /// <param name="sessionId">ID de la sesión</param>
    void ApplyGovernance(Microsoft.SemanticKernel.Kernel kernel, Agent agent, Guid sessionId);
}