using AutoAgentes.Domain.Entities;

namespace AutoAgentes.Application.Abstractions;

/// <summary>
/// Interfaz para políticas de gobernanza
/// </summary>
public interface IGovernancePolicy
{
    /// <summary>
    /// Verifica si una función es peligrosa según las políticas
    /// </summary>
    /// <param name="functionName">Nombre de la función</param>
    /// <param name="agent">Agente que ejecuta la función</param>
    /// <returns>True si la función es peligrosa</returns>
    bool IsDangerousFunction(string functionName, Agent agent);
    
    /// <summary>
    /// Obtiene el nivel de seguridad para un agente
    /// </summary>
    /// <param name="agent">Agente</param>
    /// <returns>Nivel de seguridad</returns>
    string GetSecurityLevel(Agent agent);
    
    /// <summary>
    /// Obtiene los límites de tokens para un agente
    /// </summary>
    /// <param name="agent">Agente</param>
    /// <returns>Límites de tokens</returns>
    (int MaxTokens, double Temperature) GetTokenLimits(Agent agent);
}