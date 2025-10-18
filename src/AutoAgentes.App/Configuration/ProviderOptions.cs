namespace AutoAgentes.App.Configuration;

/// <summary>
/// Opciones de configuración para proveedores de LLM
/// </summary>
public sealed class ProviderOptions
{
    /// <summary>
    /// Configuración de OpenAI
    /// </summary>
    public OpenAIProviderOptions OpenAI { get; init; } = new();
    
    /// <summary>
    /// Configuración de Azure OpenAI
    /// </summary>
    public AzureOpenAIProviderOptions AzureOpenAI { get; init; } = new();
    
    /// <summary>
    /// Configuración de Anthropic
    /// </summary>
    public AnthropicProviderOptions Anthropic { get; init; } = new();
}

/// <summary>
/// Opciones específicas para OpenAI
/// </summary>
public sealed class OpenAIProviderOptions
{
    /// <summary>
    /// Modelo a utilizar
    /// </summary>
    public string Model { get; init; } = "gpt-4o";
    
    /// <summary>
    /// API Key (se puede usar formato env:VARIABLE_NAME)
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;
}

/// <summary>
/// Opciones específicas para Azure OpenAI
/// </summary>
public sealed class AzureOpenAIProviderOptions
{
    /// <summary>
    /// Endpoint de Azure OpenAI
    /// </summary>
    public string Endpoint { get; init; } = string.Empty;
    
    /// <summary>
    /// Nombre del deployment
    /// </summary>
    public string Deployment { get; init; } = "gpt-4o";
    
    /// <summary>
    /// API Key (se puede usar formato env:VARIABLE_NAME)
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;
}

/// <summary>
/// Opciones específicas para Anthropic
/// </summary>
public sealed class AnthropicProviderOptions
{
    /// <summary>
    /// Endpoint del proxy OpenAI-compatible
    /// </summary>
    public string Endpoint { get; init; } = string.Empty;
    
    /// <summary>
    /// Modelo a utilizar
    /// </summary>
    public string Model { get; init; } = "claude-3-5-sonnet-latest";
    
    /// <summary>
    /// API Key (se puede usar formato env:VARIABLE_NAME)
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;
}

