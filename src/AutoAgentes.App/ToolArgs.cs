using System.Text.Json;

namespace AutoAgentes.App;

/// <summary>
/// Argumentos tipados para herramientas MCP
/// </summary>
public class ToolArgs
{
    /// <summary>
    /// ID del servidor MCP
    /// </summary>
    public string? ServerId { get; set; }
    
    /// <summary>
    /// Scope de la herramienta
    /// </summary>
    public string? Scope { get; set; }
    
    /// <summary>
    /// Nombre de la herramienta
    /// </summary>
    public string? Tool { get; set; }
    
    /// <summary>
    /// Argumentos específicos de la herramienta
    /// </summary>
    public JsonElement? Arguments { get; set; }
    
    /// <summary>
    /// Argumentos como diccionario dinámico
    /// </summary>
    public Dictionary<string, object>? DynamicArgs { get; set; }
    
    /// <summary>
    /// Convertir a JSON string
    /// </summary>
    public string ToJson()
    {
        if (DynamicArgs != null)
        {
            return JsonSerializer.Serialize(DynamicArgs);
        }
        
        if (Arguments.HasValue)
        {
            return Arguments.Value.GetRawText();
        }
        
        return "{}";
    }
    
    /// <summary>
    /// Crear desde JSON string
    /// </summary>
    public static ToolArgs FromJson(string json)
    {
        try
        {
            var args = new ToolArgs();
            
            if (!string.IsNullOrEmpty(json))
            {
                var doc = JsonDocument.Parse(json);
                args.Arguments = doc.RootElement;
                
                // Intentar parsear como diccionario dinámico
                try
                {
                    args.DynamicArgs = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                }
                catch
                {
                    // Si falla, mantener solo el JsonElement
                }
            }
            
            return args;
        }
        catch
        {
            return new ToolArgs();
        }
    }
}
