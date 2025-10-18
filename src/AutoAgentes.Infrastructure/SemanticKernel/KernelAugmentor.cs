using AutoAgentes.Application.Abstractions;
using AutoAgentes.App.Configuration;
using AutoAgentes.App.Constants;
using AutoAgentes.Contracts;
using AutoAgentes.Domain.Entities;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace AutoAgentes.Infrastructure.SemanticKernel;

/// <summary>
/// Implementación de IKernelAugmentor para registrar herramientas y aplicar gobernanza
/// </summary>
public class KernelAugmentor : IKernelAugmentor
{
    private readonly ILogger<KernelAugmentor> _logger;
    private readonly IMcpRegistry _mcpRegistry;
    private readonly IMcpCaller _mcpCaller;
    private readonly IToolRegistryService _toolRegistry;
    private readonly IGovernancePolicy _governancePolicy;
    private readonly IOptionsSnapshot<TimeoutsOptions> _timeoutsOptions;
    private readonly ITraceEmitter _traceEmitter;

    public KernelAugmentor(
        ILogger<KernelAugmentor> logger,
        IMcpRegistry mcpRegistry,
        IMcpCaller mcpCaller,
        IToolRegistryService toolRegistry,
        IGovernancePolicy governancePolicy,
        IOptionsSnapshot<TimeoutsOptions> timeoutsOptions,
        ITraceEmitter traceEmitter)
    {
        _logger = logger;
        _mcpRegistry = mcpRegistry;
        _mcpCaller = mcpCaller;
        _toolRegistry = toolRegistry;
        _governancePolicy = governancePolicy;
        _timeoutsOptions = timeoutsOptions;
        _traceEmitter = traceEmitter;
    }

    public async Task RegisterToolsAsync(Microsoft.SemanticKernel.Kernel kernel, Agent agent, Guid sessionId, CancellationToken cancellationToken)
    {
        _logger.LogInformation(EventIds.ToolsRegistrationStarted, "Registering tools for agent {AgentId}", agent.Id);

        await _traceEmitter.EmitAsync(sessionId, new TraceEvent(TraceEventNames.ToolsRegistrationStarted, 
            new { agentId = agent.Id }, DateTimeOffset.UtcNow), cancellationToken);

        var tools = await _mcpRegistry.ListBoundToolsAsync(agent.Id, cancellationToken);
        await _traceEmitter.EmitAsync(sessionId, new TraceEvent(TraceEventNames.ToolsFound, 
            new { count = tools.Count }, DateTimeOffset.UtcNow), cancellationToken);

        // Un plugin por agente para evitar conflictos
        var pluginName = $"mcp_{agent.Id:N}";
        var functions = new List<Microsoft.SemanticKernel.KernelFunction>();
        var toolMappings = new Dictionary<string, (string? Scope, string Name, Guid ServerId, string? Schema)>();

        foreach (var tool in tools)
        {
            var canonical = string.IsNullOrWhiteSpace(tool.Scope) ? tool.Name : $"{tool.Scope}.{tool.Name}";
            var sanitizedName = SharedUtilities.SanitizeFunctionName(canonical);

            // Verificar si la función ya existe en el plugin
            if (kernel.Plugins.TryGetFunction(pluginName, sanitizedName, out _))
            {
                await _traceEmitter.EmitAsync(sessionId, new TraceEvent(TraceEventNames.ToolAlreadyRegistered, 
                    new { agentId = agent.Id, toolName = canonical, sanitizedName }, DateTimeOffset.UtcNow), cancellationToken);
                continue;
            }

            // Guardar mapping para ejecución posterior
            toolMappings[sanitizedName] = (tool.Scope, tool.Name, tool.McpServerId, tool.InputSchemaJson);

            // Crear parámetros desde JSON Schema si está disponible
            var parameters = BuildParametersFromJsonSchema(tool.InputSchemaJson);

            // Crear función con parámetros tipados
            var function = kernel.CreateFunctionFromMethod(
                method: async (Microsoft.SemanticKernel.Kernel k, Microsoft.SemanticKernel.KernelArguments args, CancellationToken ctk) =>
                {
                    try
                    {
                        // Validar argumentos contra el schema si está disponible
                        if (!string.IsNullOrEmpty(tool.InputSchemaJson))
                        {
                            ValidateArgsAgainstSchema(tool.InputSchemaJson, args);
                        }

                        // Convertir argumentos a JSON canónico
                        var payload = ArgsToCanonicalJson(parameters, args);

                        // Llamar a la herramienta MCP con timeout configurable
                        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctk);
                        var skTimeout = TimeSpan.FromSeconds(_timeoutsOptions.Value.SKToolSeconds);
                        cts.CancelAfter(skTimeout);

                        // Usar nombre canónico para la llamada MCP
                        return await _mcpCaller.CallAsync(tool.McpServerId, canonical, payload, cts.Token);
                    }
                    catch (OperationCanceledException oce) when (!ctk.IsCancellationRequested)
                    {
                        _logger.LogWarning(EventIds.ToolTimeout, "Tool {ToolName} timed out", tool.Name);
                        return "Error: Timeout ejecutando la herramienta";
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(EventIds.ToolError, ex, "Error executing tool {ToolName}", tool.Name);
                        return $"Error ejecutando herramienta: {ex.Message}";
                    }
                },
                functionName: sanitizedName,
                description: tool.Description ?? $"MCP tool {canonical}",
                parameters: parameters
            );

            functions.Add(function);
        }

        // Crear y registrar el plugin si hay funciones
        if (functions.Count > 0)
        {
            var plugin = Microsoft.SemanticKernel.KernelPluginFactory.CreateFromFunctions(pluginName, functions);
            kernel.Plugins.Add(plugin);

            // Guardar el mapping en el servicio global de registro
            _toolRegistry.RegisterTools(agent.Id, toolMappings);
        }

        await _traceEmitter.EmitAsync(sessionId, new TraceEvent(TraceEventNames.ToolsRegistrationCompleted, 
            new { totalTools = tools.Count, newTools = functions.Count, pluginName = pluginName, hasMappings = toolMappings.Count > 0 }, 
            DateTimeOffset.UtcNow), cancellationToken);
    }

    public void ApplyGovernance(Microsoft.SemanticKernel.Kernel kernel, Agent agent, Guid sessionId)
    {
        _logger.LogInformation(EventIds.KernelGovernanceApplied, "Applying governance for agent {AgentId}", agent.Id);

        var securityLevel = _governancePolicy.GetSecurityLevel(agent);
        var (maxTokens, temperature) = _governancePolicy.GetTokenLimits(agent);

        // Aplicar filtros de seguridad usando eventos
        if (securityLevel.Equals("High", StringComparison.OrdinalIgnoreCase))
        {
            kernel.FunctionInvoking += (sender, args) =>
            {
                if (_governancePolicy.IsDangerousFunction(args.Function.Name, agent))
                {
                    _logger.LogWarning("Blocking dangerous function {FunctionName} for agent {AgentId}", 
                        args.Function.Name, agent.Id);
                    args.Cancel = true;
                }
            };
        }

        // Configurar logging de todas las invocaciones con métricas
        kernel.FunctionInvoking += (sender, args) =>
        {
            _logger.LogDebug("Function {FunctionName} invoking for agent {AgentId}", 
                args.Function.Name, agent.Id);
        };

        kernel.FunctionInvoked += (sender, args) =>
        {
            _logger.LogDebug("Function {FunctionName} invoked for agent {AgentId}", 
                args.Function.Name, agent.Id);
        };
    }

    private static IReadOnlyList<Microsoft.SemanticKernel.KernelParameterMetadata> BuildParametersFromJsonSchema(string? inputSchemaJson)
    {
        if (string.IsNullOrEmpty(inputSchemaJson))
        {
            return new[]
            {
                new Microsoft.SemanticKernel.KernelParameterMetadata("args")
                {
                    Description = "Argumentos de la herramienta en formato JSON",
                    IsRequired = false,
                    DefaultValue = "{}"
                }
            };
        }

        try
        {
            var schema = JsonDocument.Parse(inputSchemaJson);
            var parameters = new List<Microsoft.SemanticKernel.KernelParameterMetadata>();

            if (schema.RootElement.TryGetProperty("properties", out var properties))
            {
                foreach (var prop in properties.EnumerateObject())
                {
                    var param = new Microsoft.SemanticKernel.KernelParameterMetadata(prop.Name)
                    {
                        Description = GetPropertyDescription(prop.Value),
                        IsRequired = IsPropertyRequired(prop.Name, schema.RootElement),
                        DefaultValue = GetPropertyDefault(prop.Value)
                    };
                    parameters.Add(param);
                }
            }

            return parameters.Count > 0 ? parameters : new[]
            {
                new Microsoft.SemanticKernel.KernelParameterMetadata("args")
                {
                    Description = "Argumentos de la herramienta en formato JSON",
                    IsRequired = false,
                    DefaultValue = "{}"
                }
            };
        }
        catch
        {
            return new[]
            {
                new Microsoft.SemanticKernel.KernelParameterMetadata("args")
                {
                    Description = "Argumentos de la herramienta en formato JSON",
                    IsRequired = false,
                    DefaultValue = "{}"
                }
            };
        }
    }

    private static string GetPropertyDescription(JsonElement property)
    {
        if (property.TryGetProperty("description", out var desc))
        {
            return desc.GetString() ?? "Sin descripción";
        }
        return "Sin descripción";
    }

    private static bool IsPropertyRequired(string propertyName, JsonElement schema)
    {
        if (schema.TryGetProperty("required", out var required))
        {
            return required.EnumerateArray().Any(r => r.GetString() == propertyName);
        }
        return false;
    }

    private static object? GetPropertyDefault(JsonElement property)
    {
        if (property.TryGetProperty("default", out var defaultValue))
        {
            return defaultValue.ValueKind switch
            {
                JsonValueKind.String => defaultValue.GetString(),
                JsonValueKind.Number => defaultValue.TryGetDouble(out var doubleVal) ? doubleVal : defaultValue.TryGetInt64(out var longVal) ? longVal : defaultValue.GetDecimal(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            };
        }
        return null;
    }

    private static void ValidateArgsAgainstSchema(string schemaJson, Microsoft.SemanticKernel.KernelArguments args)
    {
        try
        {
            var schema = JsonDocument.Parse(schemaJson);

            // Validación básica: verificar propiedades requeridas
            if (schema.RootElement.TryGetProperty("required", out var required))
            {
                foreach (var reqProp in required.EnumerateArray())
                {
                    var propName = reqProp.GetString();
                    if (!string.IsNullOrEmpty(propName) && !args.ContainsKey(propName))
                    {
                        throw new ArgumentException($"Propiedad requerida '{propName}' no encontrada en los argumentos");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Error validando argumentos contra schema: {ex.Message}");
        }
    }

    private static string ArgsToCanonicalJson(IReadOnlyList<Microsoft.SemanticKernel.KernelParameterMetadata> parameters, Microsoft.SemanticKernel.KernelArguments args)
    {
        try
        {
            var jsonArgs = new Dictionary<string, object>();

            foreach (var param in parameters)
            {
                if (args.TryGetValue(param.Name, out var value))
                {
                    jsonArgs[param.Name] = value ?? "";
                }
            }

            return JsonSerializer.Serialize(jsonArgs);
        }
        catch
        {
            return "{}";
        }
    }
}
