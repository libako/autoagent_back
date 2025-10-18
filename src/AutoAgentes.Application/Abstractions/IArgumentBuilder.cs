namespace AutoAgentes.Application.Abstractions;

/// <summary>
/// Interfaz para construir argumentos usando LLM
/// </summary>
public interface IArgumentBuilder
{
    /// <summary>
    /// Construye argumentos para una herramienta basándose en su schema y el mensaje del usuario
    /// </summary>
    /// <param name="inputSchemaJson">Schema JSON de la herramienta</param>
    /// <param name="userMessage">Mensaje del usuario</param>
    /// <param name="toolName">Nombre de la herramienta</param>
    /// <param name="toolDescription">Descripción de la herramienta</param>
    /// <param name="sessionId">ID de la sesión</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <returns>Argumentos construidos</returns>
    Task<Dictionary<string, object>> BuildArgumentsAsync(
        string? inputSchemaJson, 
        string userMessage, 
        string toolName, 
        string? toolDescription, 
        Guid sessionId, 
        CancellationToken cancellationToken);
}