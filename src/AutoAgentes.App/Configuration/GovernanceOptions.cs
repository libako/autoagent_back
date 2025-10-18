namespace AutoAgentes.App.Configuration;

/// <summary>
/// Opciones de gobernanza y seguridad
/// </summary>
public sealed class GovernanceOptions
{
    /// <summary>
    /// Nivel de seguridad (High, Medium, Low)
    /// </summary>
    public string SecurityLevel { get; init; } = "High";
    
    /// <summary>
    /// Patrones de funciones peligrosas (regex)
    /// </summary>
    public string[] DangerousFunctionPatterns { get; init; } = new[]
    {
        "^delete($|_)",
        "^remove($|_)",
        "^drop($|_)",
        "^truncate($|_)",
        "^wipe($|_)",
        "^destroy($|_)"
    };
}

