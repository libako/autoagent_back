using System.Text.Json;

namespace AutoAgentes.Shared.Utilities;

/// <summary>
/// Utilidades para manipulación de JSON
/// </summary>
public static class JsonUtilities
{
    /// <summary>
    /// Intenta reparar JSON malformado
    /// </summary>
    /// <param name="content">Contenido JSON potencialmente malformado</param>
    /// <returns>JSON reparado o fallback</returns>
    public static string TryRepairJson(string content)
    {
        try
        {
            // Buscar el primer { y último }
            var startIndex = content.IndexOf('{');
            var endIndex = content.LastIndexOf('}');
            
            if (startIndex >= 0 && endIndex > startIndex)
            {
                var jsonContent = content.Substring(startIndex, endIndex - startIndex + 1);
                
                // Intentar parsear para validar
                JsonDocument.Parse(jsonContent);
                return jsonContent;
            }
            
            // Si no hay llaves, intentar envolver
            if (!content.Contains('{') && !content.Contains('}'))
            {
                return "{\"goal\": \"Procesar mensaje\", \"steps\": []}";
            }
            
            return content;
        }
        catch
        {
            // Si todo falla, devolver JSON por defecto
            return "{\"goal\": \"Procesar mensaje\", \"steps\": []}";
        }
    }

    /// <summary>
    /// Deserializa JSON de forma segura con fallback
    /// </summary>
    /// <typeparam name="T">Tipo a deserializar</typeparam>
    /// <param name="json">JSON a deserializar</param>
    /// <param name="fallback">Valor de fallback</param>
    /// <returns>Objeto deserializado o fallback</returns>
    public static T SafeDeserialize<T>(string json, T fallback) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }
}

