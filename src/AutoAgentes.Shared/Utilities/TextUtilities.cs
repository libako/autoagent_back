using AutoAgentes.App.Constants;

namespace AutoAgentes.Shared.Utilities;

/// <summary>
/// Utilidades para manipulación de texto
/// </summary>
public static class TextUtilities
{
    /// <summary>
    /// Sanitiza el nombre de una función para que sea válido como nombre de método C#
    /// </summary>
    /// <param name="input">Nombre original de la función</param>
    /// <returns>Nombre sanitizado y seguro</returns>
    public static string SanitizeFunctionName(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "unnamed_tool";

        // Reemplazar puntos con guiones bajos
        var sanitized = input.Replace(".", "_");
        
        // Usar regex compilada para mejor rendimiento
        sanitized = Regexes.FunctionNameCleaner.Replace(sanitized, "");
        
        // Convertir a minúsculas
        sanitized = sanitized.ToLowerInvariant();
        
        // Asegurar que empiece con letra
        if (sanitized.Length > 0 && !char.IsLetter(sanitized[0]))
        {
            sanitized = "tool_" + sanitized;
        }
        
        // Limitar longitud
        if (sanitized.Length > 50)
        {
            sanitized = sanitized.Substring(0, 50);
        }
        
        return sanitized;
    }

    /// <summary>
    /// Resuelve plantillas en un string usando un diccionario de valores
    /// </summary>
    /// <param name="input">String con plantillas</param>
    /// <param name="values">Diccionario de valores</param>
    /// <returns>String con plantillas resueltas</returns>
    public static string ResolveTemplates(string input, Dictionary<string, object> values)
    {
        if (string.IsNullOrEmpty(input) || values == null || values.Count == 0)
            return input;

        return Regexes.Template.Replace(input, match =>
        {
            var key = match.Groups[1].Value;
            
            if (values.TryGetValue(key, out var value))
            {
                return value?.ToString() ?? "";
            }
            
            // Si no se encuentra, mantener la plantilla original
            return match.Value;
        });
    }

    /// <summary>
    /// Corta un string de forma segura a una longitud máxima
    /// </summary>
    /// <param name="input">String de entrada</param>
    /// <param name="maxLength">Longitud máxima</param>
    /// <returns>String cortado</returns>
    public static string SafeSlice(string? input, int maxLength)
    {
        if (string.IsNullOrEmpty(input) || input.Length <= maxLength)
            return input ?? "";
        
        return input.Substring(0, maxLength) + "...";
    }
}
