namespace AutoAgentes.Application.Abstractions;

/// <summary>
/// Interfaz para sintetizar respuestas usando LLM
/// </summary>
public interface IResponseSynthesizer
{
    /// <summary>
    /// Procesa la respuesta de una herramienta MCP usando LLM
    /// </summary>
    /// <param name="toolName">Nombre de la herramienta</param>
    /// <param name="response">Respuesta cruda de la herramienta</param>
    /// <param name="inputSchemaJson">Schema de entrada de la herramienta</param>
    /// <param name="toolDescription">Descripción de la herramienta</param>
    /// <param name="userMessage">Mensaje del usuario</param>
    /// <param name="sessionId">ID de la sesión</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <returns>Respuesta procesada</returns>
    Task<string> ProcessResponseAsync(
        string toolName, 
        string? response, 
        string? inputSchemaJson, 
        string? toolDescription, 
        string userMessage, 
        Guid sessionId, 
        CancellationToken cancellationToken);
}