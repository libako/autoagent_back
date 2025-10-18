namespace AutoAgentes.App.Constants;

/// <summary>
/// Tipos de mensajes estructurados
/// </summary>
public static class MessageKinds
{
    /// <summary>
    /// Mensaje del usuario
    /// </summary>
    public const string User = "user";
    
    /// <summary>
    /// Mensaje del asistente
    /// </summary>
    public const string Assistant = "assistant";
    
    /// <summary>
    /// Mensaje del sistema
    /// </summary>
    public const string System = "system";
    
    /// <summary>
    /// Observación de una herramienta
    /// </summary>
    public const string Observation = "observation";
    
    /// <summary>
    /// Mensaje de desarrollador (para prompts)
    /// </summary>
    public const string Developer = "developer";
}

