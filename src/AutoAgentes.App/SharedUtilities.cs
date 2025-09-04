namespace AutoAgentes.App;

/// <summary>
/// Utilidades compartidas entre diferentes clases del proyecto
/// </summary>
internal static class SharedUtilities
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
        
        // Remover caracteres especiales excepto guiones bajos
        sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"[^a-zA-Z0-9_]", "");
        
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
}
