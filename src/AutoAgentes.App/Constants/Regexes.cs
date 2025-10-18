using System.Text.RegularExpressions;

namespace AutoAgentes.App.Constants;

/// <summary>
/// Expresiones regulares compiladas y centralizadas
/// </summary>
internal static class Regexes
{
    /// <summary>
    /// Regex para plantillas como "${key}" o "${key.subkey}"
    /// </summary>
    public static readonly Regex Template = new(@"\$\{([^}]+)\}", RegexOptions.Compiled);
    
    /// <summary>
    /// Regex para limpiar nombres de funciones
    /// </summary>
    public static readonly Regex FunctionNameCleaner = new(@"[^a-zA-Z0-9_]", RegexOptions.Compiled);
    
    /// <summary>
    /// Regex para validar nombres de funciones
    /// </summary>
    public static readonly Regex FunctionNameValidator = new(@"^[a-zA-Z][a-zA-Z0-9_]*$", RegexOptions.Compiled);
}

