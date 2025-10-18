namespace AutoAgentes.App.Configuration;

/// <summary>
/// Opciones de configuración para el planificador
/// </summary>
public sealed class PlannerOptions
{
    /// <summary>
    /// Temperatura para el LLM del planificador (0.0 = determinista)
    /// </summary>
    public double Temperature { get; init; } = 0.0;
    
    /// <summary>
    /// TopP para el LLM del planificador
    /// </summary>
    public double TopP { get; init; } = 0.1;
    
    /// <summary>
    /// Máximo número de tokens para el planificador
    /// </summary>
    public int MaxTokens { get; init; } = 2000;
    
    /// <summary>
    /// Máximo número de pasos en un plan
    /// </summary>
    public int MaxSteps { get; init; } = 6;
    
    /// <summary>
    /// Límite de pasos del planificador
    /// </summary>
    public int StepLimit { get; init; } = 8;
    
    /// <summary>
    /// Tiempo mínimo de iteración en milisegundos
    /// </summary>
    public int MinIterationMs { get; init; } = 0;
}

