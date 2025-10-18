namespace AutoAgentes.App.Configuration;

/// <summary>
/// Opciones de configuración para el kernel de Semantic Kernel
/// </summary>
public sealed class KernelOptions
{
    /// <summary>
    /// Timeout en segundos para operaciones del kernel
    /// </summary>
    public int TimeoutSeconds { get; init; } = 30;
    
    /// <summary>
    /// Proveedor por defecto para el kernel
    /// </summary>
    public string Provider { get; init; } = "openai";
    
    /// <summary>
    /// Tamaño máximo del cache de kernels
    /// </summary>
    public int MaxCacheSize { get; init; } = 50;
    
    /// <summary>
    /// TTL del cache en minutos
    /// </summary>
    public int CacheTtlMinutes { get; init; } = 60;
}

