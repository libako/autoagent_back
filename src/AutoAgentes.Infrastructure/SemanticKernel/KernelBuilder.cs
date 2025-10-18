using AutoAgentes.Application.Abstractions;
using AutoAgentes.App.Configuration;
using AutoAgentes.App.Constants;
using AutoAgentes.Contracts;
using AutoAgentes.Domain.Entities;
using Microsoft.Extensions.Options;

namespace AutoAgentes.Infrastructure.SemanticKernel;

/// <summary>
/// Implementación de IKernelBuilder para crear kernels de Semantic Kernel
/// </summary>
public class KernelBuilder : IKernelBuilder
{
    private readonly ILogger<KernelBuilder> _logger;
    private readonly IOptionsSnapshot<ProviderOptions> _providerOptions;
    private readonly ITraceEmitter _traceEmitter;

    public KernelBuilder(
        ILogger<KernelBuilder> logger,
        IOptionsSnapshot<ProviderOptions> providerOptions,
        ITraceEmitter traceEmitter)
    {
        _logger = logger;
        _providerOptions = providerOptions;
        _traceEmitter = traceEmitter;
    }

    public async Task<Microsoft.SemanticKernel.Kernel> CreateKernelAsync(Agent agent, Guid sessionId, CancellationToken cancellationToken)
    {
        _logger.LogInformation(EventIds.KernelCreated, "Creating kernel for agent {AgentId} with provider {Provider}", 
            agent.Id, agent.Provider);

        await _traceEmitter.EmitAsync(sessionId, new TraceEvent(TraceEventNames.KernelCreationStarted, 
            new { agentId = agent.Id }, DateTimeOffset.UtcNow), cancellationToken);

        var builder = Microsoft.SemanticKernel.Kernel.CreateBuilder();
        var providerOptions = _providerOptions.Value;

        // Configurar el provider según el agente
        var configInfo = await ConfigureProviderAsync(builder, agent, providerOptions, sessionId, cancellationToken);

        await _traceEmitter.EmitAsync(sessionId, new TraceEvent(TraceEventNames.KernelConfig, 
            configInfo, DateTimeOffset.UtcNow), cancellationToken);

        var kernel = builder.Build();

        await _traceEmitter.EmitAsync(sessionId, new TraceEvent(TraceEventNames.KernelBuilt, 
            new { provider = agent.Provider }, DateTimeOffset.UtcNow), cancellationToken);

        return kernel;
    }

    private async Task<object> ConfigureProviderAsync(
        Microsoft.SemanticKernel.KernelBuilder builder, 
        Agent agent, 
        ProviderOptions providerOptions, 
        Guid sessionId, 
        CancellationToken cancellationToken)
    {
        return agent.Provider.ToLowerInvariant() switch
        {
            "azureopenai" => await ConfigureAzureOpenAIAsync(builder, agent, providerOptions.AzureOpenAI, sessionId, cancellationToken),
            "openai" => await ConfigureOpenAIAsync(builder, agent, providerOptions.OpenAI, sessionId, cancellationToken),
            "anthropic" => await ConfigureAnthropicAsync(builder, agent, providerOptions.Anthropic, sessionId, cancellationToken),
            _ => await ConfigureOpenAIAsync(builder, agent, providerOptions.OpenAI, sessionId, cancellationToken) // Fallback
        };
    }

    private async Task<object> ConfigureAzureOpenAIAsync(
        Microsoft.SemanticKernel.KernelBuilder builder, 
        Agent agent, 
        AzureOpenAIProviderOptions options, 
        Guid sessionId, 
        CancellationToken cancellationToken)
    {
        var apiKey = ResolveApiKey(options.ApiKey);
        
        builder.AddAzureOpenAIChatCompletion(
            deploymentName: options.Deployment,
            endpoint: options.Endpoint,
            apiKey: apiKey
        );

        return new
        {
            deployment = options.Deployment,
            endpoint = options.Endpoint,
            hasApiKey = !string.IsNullOrEmpty(apiKey),
            provider = agent.Provider
        };
    }

    private async Task<object> ConfigureOpenAIAsync(
        Microsoft.SemanticKernel.KernelBuilder builder, 
        Agent agent, 
        OpenAIProviderOptions options, 
        Guid sessionId, 
        CancellationToken cancellationToken)
    {
        var apiKey = ResolveApiKey(options.ApiKey);
        
        builder.AddOpenAIChatCompletion(
            modelId: options.Model,
            apiKey: apiKey
        );

        return new
        {
            model = options.Model,
            hasApiKey = !string.IsNullOrEmpty(apiKey),
            provider = agent.Provider
        };
    }

    private async Task<object> ConfigureAnthropicAsync(
        Microsoft.SemanticKernel.KernelBuilder builder, 
        Agent agent, 
        AnthropicProviderOptions options, 
        Guid sessionId, 
        CancellationToken cancellationToken)
    {
        var apiKey = ResolveApiKey(options.ApiKey);
        
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("Anthropic API key no configurada");
        }
        
        if (string.IsNullOrEmpty(options.Endpoint))
        {
            throw new InvalidOperationException("Anthropic Endpoint no configurado - necesitas un proxy OpenAI-compatible");
        }
        
        try
        {
            builder.AddOpenAIChatCompletion(
                modelId: options.Model,
                apiKey: apiKey,
                endpoint: new Uri(options.Endpoint)
            );

            return new
            {
                model = options.Model,
                endpoint = options.Endpoint,
                hasApiKey = !string.IsNullOrEmpty(apiKey),
                provider = agent.Provider
            };
        }
        catch (Exception ex)
        {
            await _traceEmitter.EmitAsync(sessionId, new TraceEvent(TraceEventNames.AnthropicConfigFailed, 
                new { error = ex.Message, endpoint = options.Endpoint }, DateTimeOffset.UtcNow), cancellationToken);
            
            throw new InvalidOperationException($"Configuración de Anthropic falló: {ex.Message}. Verifica que tengas un proxy OpenAI-compatible para Claude.");
        }
    }

    private static string ResolveApiKey(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
            return string.Empty;

        // Si empieza con "env:", resolver desde variables de entorno
        if (apiKey.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
        {
            var envVar = apiKey.Substring(4);
            return Environment.GetEnvironmentVariable(envVar) ?? string.Empty;
        }

        return apiKey;
    }
}
