using AutoAgentes.Domain.Entities;

namespace AutoAgentes.Application.Abstractions;

/// <summary>
/// Interfaz para cache de kernels
/// </summary>
public interface IKernelCache
{
    /// <summary>
    /// Obtiene un kernel del cache o lo crea si no existe
    /// </summary>
    /// <param name="agent">Agente para el kernel</param>
    /// <param name="sessionId">ID de la sesión</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <returns>Kernel del cache</returns>
    Task<Microsoft.SemanticKernel.Kernel> GetOrCreateAsync(Agent agent, Guid sessionId, CancellationToken cancellationToken);
    
    /// <summary>
    /// Invalida el cache para un agente específico
    /// </summary>
    /// <param name="agentId">ID del agente</param>
    void InvalidateAgent(Guid agentId);
    
    /// <summary>
    /// Limpia todo el cache
    /// </summary>
    void ClearCache();
    
    /// <summary>
    /// Obtiene estadísticas del cache
    /// </summary>
    /// <returns>Estadísticas del cache</returns>
    Dictionary<string, object> GetCacheStats();
}

