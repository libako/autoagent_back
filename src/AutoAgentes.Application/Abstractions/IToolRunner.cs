namespace AutoAgentes.Application.Abstractions;

/// <summary>
/// Interfaz para ejecutar herramientas MCP
/// </summary>
public interface IToolRunner
{
    /// <summary>
    /// Ejecuta una herramienta MCP
    /// </summary>
    /// <param name="agentId">ID del agente</param>
    /// <param name="functionName">Nombre de la función</param>
    /// <param name="args">Argumentos para la función</param>
    /// <param name="userMessage">Mensaje del usuario para contexto</param>
    /// <param name="sessionId">ID de la sesión</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <returns>Resultado de la ejecución</returns>
    Task<string> ExecuteToolAsync(
        Guid agentId, 
        string functionName, 
        Dictionary<string, object>? args, 
        string userMessage, 
        Guid sessionId, 
        CancellationToken cancellationToken);
}