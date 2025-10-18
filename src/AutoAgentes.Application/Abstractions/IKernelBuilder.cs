using AutoAgentes.Domain.Entities;

namespace AutoAgentes.Application.Abstractions;

/// <summary>
/// Interfaz para construir kernels de Semantic Kernel
/// </summary>
public interface IKernelBuilder
{
    /// <summary>
    /// Crea un kernel básico con el proveedor configurado
    /// </summary>
    /// <param name="agent">Agente con configuración del proveedor</param>
    /// <param name="sessionId">ID de la sesión</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <returns>Kernel configurado</returns>
    Task<Microsoft.SemanticKernel.Kernel> CreateKernelAsync(Agent agent, Guid sessionId, CancellationToken cancellationToken);
}