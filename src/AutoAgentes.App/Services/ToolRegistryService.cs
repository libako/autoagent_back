using AutoAgentes.Contracts;

namespace AutoAgentes.App.Services;

/// <summary>
/// Servicio global para mantener el registro de herramientas MCP
/// </summary>
public interface IToolRegistryService
{
    /// <summary>
    /// Registrar herramientas para un agente
    /// </summary>
    void RegisterTools(Guid agentId, Dictionary<string, (string? Scope, string Name, Guid ServerId, string? Schema)> toolMappings);
    
    /// <summary>
    /// Obtener herramientas registradas para un agente
    /// </summary>
    Dictionary<string, (string? Scope, string Name, Guid ServerId, string? Schema)>? GetTools(Guid agentId);
    
    /// <summary>
    /// Verificar si una herramienta está registrada para un agente
    /// </summary>
    bool IsToolRegistered(Guid agentId, string toolName);
    
    /// <summary>
    /// Obtener información de una herramienta específica
    /// </summary>
    (string? Scope, string Name, Guid ServerId, string? Schema)? GetToolInfo(Guid agentId, string toolName);
    
    /// <summary>
    /// Limpiar herramientas de un agente
    /// </summary>
    void ClearTools(Guid agentId);
}

/// <summary>
/// Implementación del servicio de registro de herramientas
/// </summary>
public class ToolRegistryService : IToolRegistryService
{
    private readonly Dictionary<Guid, Dictionary<string, (string? Scope, string Name, Guid ServerId, string? Schema)>> _agentTools = new();
    private readonly object _lock = new object();
    
    public void RegisterTools(Guid agentId, Dictionary<string, (string? Scope, string Name, Guid ServerId, string? Schema)> toolMappings)
    {
        lock (_lock)
        {
            _agentTools[agentId] = toolMappings;
        }
    }
    
    public Dictionary<string, (string? Scope, string Name, Guid ServerId, string? Schema)>? GetTools(Guid agentId)
    {
        lock (_lock)
        {
            return _agentTools.TryGetValue(agentId, out var tools) ? tools : null;
        }
    }
    
    public bool IsToolRegistered(Guid agentId, string toolName)
    {
        lock (_lock)
        {
            return _agentTools.TryGetValue(agentId, out var tools) && tools.ContainsKey(toolName);
        }
    }
    
    public (string? Scope, string Name, Guid ServerId, string? Schema)? GetToolInfo(Guid agentId, string toolName)
    {
        lock (_lock)
        {
            if (_agentTools.TryGetValue(agentId, out var tools) && tools.TryGetValue(toolName, out var toolInfo))
            {
                return toolInfo;
            }
            return null;
        }
    }
    
    public void ClearTools(Guid agentId)
    {
        lock (_lock)
        {
            _agentTools.Remove(agentId);
        }
    }
}

