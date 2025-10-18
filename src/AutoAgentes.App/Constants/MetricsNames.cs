namespace AutoAgentes.App.Constants;

/// <summary>
/// Nombres de métricas estructurados
/// </summary>
public static class MetricsNames
{
    /// <summary>
    /// Contador total de llamadas a herramientas
    /// </summary>
    public const string ToolCallsTotal = "tool_calls_total";
    
    /// <summary>
    /// Contador total de tokens consumidos
    /// </summary>
    public const string TokensConsumed = "tokens_consumed";
    
    /// <summary>
    /// Contador de funciones invocadas
    /// </summary>
    public const string FunctionInvokingTotal = "function_invoking_total";
    
    /// <summary>
    /// Contador de funciones ejecutadas
    /// </summary>
    public const string FunctionInvokedTotal = "function_invoked_total";
    
    /// <summary>
    /// Contador de timeouts de herramientas
    /// </summary>
    public const string ToolTimeoutsTotal = "tool_timeouts_total";
    
    /// <summary>
    /// Contador de errores de herramientas
    /// </summary>
    public const string ToolErrorsTotal = "tool_errors_total";
}

