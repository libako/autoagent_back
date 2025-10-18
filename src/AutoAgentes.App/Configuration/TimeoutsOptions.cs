namespace AutoAgentes.App.Configuration;

/// <summary>
/// Opciones de timeouts para diferentes operaciones
/// </summary>
public sealed class TimeoutsOptions
{
    /// <summary>
    /// Timeout para herramientas MCP en segundos
    /// </summary>
    public int MCPToolSeconds { get; init; } = 30;
    
    /// <summary>
    /// Timeout para herramientas de Semantic Kernel en segundos
    /// </summary>
    public int SKToolSeconds { get; init; } = 30;
    
    /// <summary>
    /// Timeout para el planificador en segundos
    /// </summary>
    public int PlannerSeconds { get; init; } = 60;
    
    /// <summary>
    /// Timeout para el resumen final en segundos
    /// </summary>
    public int SummarySeconds { get; init; } = 45;
}

