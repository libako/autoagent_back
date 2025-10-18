using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using AutoAgentes.Contracts;
using System.Text.Json;
using AutoAgentes.App.Filters;
using AutoAgentes.App.Services;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace AutoAgentes.App;

/// <summary>
/// Marcador para evitar aplicar gobernanza múltiples veces al mismo kernel
/// </summary>
public static class KernelGovernanceMarker
{
    private static readonly ConditionalWeakTable<Kernel, object> _tag = new();
    private static readonly ConditionalWeakTable<Kernel, SemaphoreSlim> _semaphores = new();
    
    public static bool IsAttached(Kernel k) => _tag.TryGetValue(k, out _);
    
    public static void MarkAttached(Kernel k)
    {
        // Si ya existe, no pasa nada; si no, se crea.
        _tag.GetValue(k, _ => new object());
    }
    
    /// <summary>
    /// Obtener semáforo único para un kernel específico
    /// </summary>
    public static SemaphoreSlim GetSemaphore(Kernel k) => _semaphores.GetValue(k, _ => new SemaphoreSlim(1, 1));
}

public interface IKernelFactory
{
    Task<Kernel> CreateAsync(AutoAgentes.Domain.Entities.Agent agent, Guid sessionId, CancellationToken ct, bool minimalKernel = false);
}

public class KernelFactory : IKernelFactory
{
    private readonly IServiceProvider _sp;
    private readonly IMcpRegistry _registry;
    private readonly IMcpCaller _caller;
    private readonly ITraceEmitter _emitter;
    private readonly IConfiguration _configuration;
    private readonly IToolRegistryService _toolRegistry;
    
    // Cache de kernels para reutilización - ConcurrentDictionary para evitar condiciones de carrera
    private readonly ConcurrentDictionary<string, Lazy<Task<Kernel>>> _kernelCache = new();
    
    // Cache auxiliar para tracking de TTL y tamaño
    private readonly ConcurrentDictionary<string, (DateTime Created, int AccessCount)> _cacheMetadata = new();

    public KernelFactory(IServiceProvider sp, IMcpRegistry registry, IMcpCaller caller, ITraceEmitter emitter, IConfiguration configuration, IToolRegistryService toolRegistry)
    { 
        _sp = sp; 
        _registry = registry; 
        _caller = caller; 
        _emitter = emitter; 
        _configuration = configuration;
        _toolRegistry = toolRegistry;
    }

    public async Task<Kernel> CreateAsync(AutoAgentes.Domain.Entities.Agent agent, Guid sessionId, CancellationToken ct, bool minimalKernel = false)
    {
        try
        {
            await _emitter.EmitAsync(sessionId, new TraceEvent("kernel_factory_started", new { agentId = agent.Id, minimalKernel }, DateTimeOffset.UtcNow), ct);

            // Para kernels mínimos (utilitarios), crear sin cache ni gobernanza
            if (minimalKernel)
            {
                var kernel = await CreateNewKernelAsync(agent, sessionId, ct);
                await _emitter.EmitAsync(sessionId, new TraceEvent("kernel_factory_minimal_created", new { agentId = agent.Id }, DateTimeOffset.UtcNow), ct);
                return kernel;
            }

            // Limpiar cache periódicamente
            CleanupCache();
            
            // Intentar reutilizar kernel existente del mismo provider y herramientas
            var cacheKey = await GetKernelCacheKeyWithToolsAsync(agent, ct);
            
            var lazyKernel = _kernelCache.GetOrAdd(cacheKey,
                _ => new Lazy<Task<Kernel>>(() => CreateNewKernelAsync(agent, sessionId, ct)));
            
            // Actualizar metadata de acceso
            _cacheMetadata.AddOrUpdate(cacheKey, 
                (DateTime.UtcNow, 1), 
                (key, existing) => (existing.Created, existing.AccessCount + 1));

            Kernel cachedKernel;
            try
            {
                cachedKernel = await lazyKernel.Value;
            }
            catch
            {
                // Si la Task falla, remover del cache para permitir reintento
                _kernelCache.TryRemove(cacheKey, out _);
                throw;
            }

            // Solo registrar herramientas y aplicar gobernanza si no se ha hecho antes
            // Usar semáforo per-kernel para evitar race conditions y deadlocks
            var semaphore = KernelGovernanceMarker.GetSemaphore(cachedKernel);
            await semaphore.WaitAsync(ct);
            try
            {
                if (!KernelGovernanceMarker.IsAttached(cachedKernel))
                {
                    // Registrar herramientas específicas del agente
                    var skToolTimeout = _configuration.GetValue<int>("Timeouts:SKToolSeconds", 30);
                    await cachedKernel.RegisterAgentToolsAsync(agent.Id, _registry, _caller, _emitter, sessionId, ct, _toolRegistry, skToolTimeout);

                    // Aplicar governance específica del agente
                    Governance.Attach(cachedKernel, agent, sessionId);
                    KernelGovernanceMarker.MarkAttached(cachedKernel);
                    
                    await _emitter.EmitAsync(sessionId, new TraceEvent("kernel_governance_applied", new { agentId = agent.Id }, DateTimeOffset.UtcNow), ct);
                }
            }
            finally
            {
                semaphore.Release();
            }

            await _emitter.EmitAsync(sessionId, new TraceEvent("kernel_factory_completed", new { reused = true }, DateTimeOffset.UtcNow), ct);

            return cachedKernel;
        }
        catch (Exception ex)
        {
            await _emitter.EmitAsync(sessionId, new TraceEvent("kernel_factory_error", new { error = ex.Message }, DateTimeOffset.UtcNow), ct);
            throw;
        }
    }

    private string GetKernelCacheKey(AutoAgentes.Domain.Entities.Agent agent)
    {
        // Cache separado por provider para evitar colisiones
        var provider = (agent.Provider ?? "openai").ToLowerInvariant();
        var autonomy = agent.Autonomy ?? "Supervised";
        
        // Configuración específica por provider
        var provPart = provider switch {
            "azureopenai" => $"{_configuration["AzureOpenAI:Endpoint"]}|{_configuration["AzureOpenAI:Deployment"]}",
            "openai"      => _configuration["OpenAI:Model"],
            "anthropic"   => $"{_configuration["Anthropic:Endpoint"]}|{_configuration["Anthropic:Model"]}",
            _             => "openai_default"
        };
        
        // Hash de la configuración para evitar keys muy largas
        var configHash = System.Text.RegularExpressions.Regex.Replace(
            $"{provider}|{provPart}|{autonomy}", 
            @"[^a-zA-Z0-9_]", "_");
        
        return $"kernel_{configHash}";
    }
    
    /// <summary>
    /// Obtener versión del registry para evicción inteligente del cache
    /// </summary>
    private async Task<int> GetRegistryVersionAsync(Guid agentId, CancellationToken ct)
    {
        try
        {
            // Por ahora retornamos 1, pero en el futuro esto vendría del registry
            // cuando implementes versionado de herramientas
            return 1;
        }
        catch
        {
            return 1; // Fallback
        }
    }
    
    /// <summary>
    /// Crear clave de cache que incluya hash de herramientas y governance
    /// </summary>
    private async Task<string> GetKernelCacheKeyWithToolsAsync(AutoAgentes.Domain.Entities.Agent agent, CancellationToken ct)
    {
        try
        {
            // Obtener herramientas del agente
            var tools = await _registry.ListBoundToolsAsync(agent.Id, ct);
            
            // Crear hash de las herramientas usando formato canónico
            var toolsHash = string.Join("|",
                tools.Select(t => string.IsNullOrWhiteSpace(t.Scope) ? t.Name : $"{t.Scope}.{t.Name}")
                     .OrderBy(s => s));
            using var sha = System.Security.Cryptography.SHA256.Create();
            var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(toolsHash));
            var toolsHashString = Convert.ToHexString(hash)[..12]; // Primeros 12 caracteres hex
            
            // Incluir versión de governance y registry para evicción inteligente
            var govVersion = "v1"; // En el futuro esto vendría del agente
            var registryVersion = await GetRegistryVersionAsync(agent.Id, ct);
            
            var baseKey = GetKernelCacheKey(agent);
            return $"{baseKey}_agent_{agent.Id:N}_tools_{toolsHashString}_gov_{govVersion}_reg_{registryVersion}";
        }
        catch
        {
            // Fallback a la clave básica
            return GetKernelCacheKey(agent);
        }
    }

    private async Task<Kernel> CreateNewKernelAsync(AutoAgentes.Domain.Entities.Agent agent, Guid sessionId, CancellationToken ct)
    {
        await _emitter.EmitAsync(sessionId, new TraceEvent("kernel_creation_started", new { agentId = agent.Id }, DateTimeOffset.UtcNow), ct);

        var builder = Kernel.CreateBuilder();


        // Log de configuración según el provider del agente
        object configInfo = agent.Provider.ToLowerInvariant() switch
        {
            "azureopenai" => new { 
                deployment = _configuration["AzureOpenAI:Deployment"] ?? "gpt-4o",
                endpoint = _configuration["AzureOpenAI:Endpoint"],
                hasApiKey = !string.IsNullOrEmpty(_configuration["AzureOpenAI:ApiKey"]),
                provider = agent.Provider
            },
            "openai" => new { 
                model = _configuration["OpenAI:Model"] ?? "gpt-4o",
                hasApiKey = !string.IsNullOrEmpty(_configuration["OpenAI:ApiKey"]),
                provider = agent.Provider
            },
            "anthropic" => new { 
                model = _configuration["Anthropic:Model"] ?? "claude-3-5-sonnet-latest",
                endpoint = _configuration["Anthropic:Endpoint"],
                hasApiKey = !string.IsNullOrEmpty(_configuration["Anthropic:ApiKey"]),
                provider = agent.Provider
            },
            _ => new { 
                provider = agent.Provider,
                fallback = "openai"
            }
        };
        
        await _emitter.EmitAsync(sessionId, new TraceEvent("kernel_config", configInfo, DateTimeOffset.UtcNow), ct);

        // Configurar el provider según el agente - separar OpenAI vs Azure OpenAI

        if (agent.Provider.Equals("azureopenai", StringComparison.OrdinalIgnoreCase))
        {
            builder.AddAzureOpenAIChatCompletion(
                deploymentName: _configuration["AzureOpenAI:Deployment"] ?? "gpt-4o",
                endpoint: _configuration["AzureOpenAI:Endpoint"]!,
                apiKey: _configuration["AzureOpenAI:ApiKey"]!
            );
        }
        else if (agent.Provider.Equals("openai", StringComparison.OrdinalIgnoreCase))
        {
            builder.AddOpenAIChatCompletion(
                modelId: _configuration["OpenAI:Model"] ?? "gpt-4o",
                apiKey: _configuration["OpenAI:ApiKey"]!
            );
        }
        else if (agent.Provider.Equals("anthropic", StringComparison.OrdinalIgnoreCase))
        {
            // Solo si tienes un proxy OpenAI-compatible delante de Claude
            var anthropicModel = _configuration["Anthropic:Model"] ?? "claude-3-5-sonnet-latest";
            var anthropicApiKey = _configuration["Anthropic:ApiKey"];
            var anthropicEndpoint = _configuration["Anthropic:Endpoint"];
            
            if (string.IsNullOrEmpty(anthropicApiKey))
            {
                throw new InvalidOperationException("Anthropic API key no configurada");
            }
            
            if (string.IsNullOrEmpty(anthropicEndpoint))
            {
                throw new InvalidOperationException("Anthropic Endpoint no configurado - necesitas un proxy OpenAI-compatible");
            }
            
            try
            {
                builder.AddOpenAIChatCompletion(
                    modelId: anthropicModel,
                    apiKey: anthropicApiKey,
                    endpoint: new Uri(anthropicEndpoint)
                );
            }
            catch (Exception ex)
            {
                await _emitter.EmitAsync(sessionId, new TraceEvent("anthropic_config_failed", new { 
                    error = ex.Message, 
                    endpoint = anthropicEndpoint 
                }, DateTimeOffset.UtcNow), ct);
                
                throw new InvalidOperationException($"Configuración de Anthropic falló: {ex.Message}. Verifica que tengas un proxy OpenAI-compatible para Claude.");
            }
        }
        else
        {
            // Fallback sensato - usar OpenAI por defecto
            builder.AddOpenAIChatCompletion(
                modelId: _configuration["OpenAI:Model"] ?? "gpt-4o",
                apiKey: _configuration["OpenAI:ApiKey"]!
            );
        }

        var kernel = builder.Build();
        
        // Configurar timeout consistente para todas las operaciones
        var timeoutSeconds = _configuration.GetValue<int>("Kernel:TimeoutSeconds", 30);
        
        await _emitter.EmitAsync(sessionId, new TraceEvent("kernel_built", new { 
            provider = agent.Provider, 
            timeoutSeconds = timeoutSeconds 
        }, DateTimeOffset.UtcNow), ct);

        return kernel;
    }

    // Método para limpiar cache si es necesario
    public void ClearCache()
    {
        foreach (var kvp in _kernelCache)
        {
            try
            {
                // En SK 1.64.0, Kernel no implementa IDisposable
                // Solo limpiar referencias del cache
            }
            catch
            {
                // Ignorar errores
            }
        }
        _kernelCache.Clear();
    }

    // Método para remover un kernel específico del cache
    public bool RemoveFromCache(string key)
    {
        if (_kernelCache.TryRemove(key, out var lazyKernel))
        {
            try
            {
                // En SK 1.64.0, Kernel no implementa IDisposable
                // Solo limpiar referencias del cache
                return true;
            }
            catch
            {
                // Ignorar errores
            }
        }
        return false;
    }

    // Método para obtener estadísticas del cache
    public Dictionary<string, object> GetCacheStats()
    {
        return new Dictionary<string, object>
        {
            ["cache_size"] = _kernelCache.Count,
            ["cached_providers"] = _kernelCache.Keys.ToList(),
            ["memory_usage_mb"] = GC.GetTotalMemory(false) / (1024 * 1024)
        };
    }

    /// <summary>
    /// Invalidar cache por cambios de registry para un agente específico
    /// </summary>
    public void InvalidateAgent(Guid agentId)
    {
        // Incluir agentId en la búsqueda de cache keys
        var agentIdString = agentId.ToString("N");
        var keysToRemove = _kernelCache.Keys
            .Where(k => k.Contains(agentIdString) || k.Contains($"agent_{agentIdString}"))
            .ToList();
            
        foreach (var key in keysToRemove)
        {
            _kernelCache.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Obtener timeout configurado para una sección específica
    /// </summary>
    private int DefaultTimeout(string section) => section.ToLowerInvariant() switch
    {
        "mcptool" => _configuration.GetValue<int>("Timeouts:MCPToolSeconds", 30),
        "sktool" => _configuration.GetValue<int>("Timeouts:SKToolSeconds", 30),
        "planner" => _configuration.GetValue<int>("Timeouts:PlannerSeconds", 60),
        "summary" => _configuration.GetValue<int>("Timeouts:SummarySeconds", 45),
        _ => 30
    };
    
    /// <summary>
    /// Limpiar cache expirado y mantener tamaño máximo
    /// </summary>
    private void CleanupCache()
    {
        try
        {
            var maxCacheSize = _configuration.GetValue<int>("Kernel:MaxCacheSize", 50);
            var cacheTtlMinutes = _configuration.GetValue<int>("Kernel:CacheTtlMinutes", 60);
            var cutoffTime = DateTime.UtcNow.AddMinutes(-cacheTtlMinutes);
            
            // Limpiar entradas expiradas
            var expiredKeys = _cacheMetadata
                .Where(kvp => kvp.Value.Created < cutoffTime)
                .Select(kvp => kvp.Key)
                .ToList();
                
            foreach (var key in expiredKeys)
            {
                _kernelCache.TryRemove(key, out _);
                _cacheMetadata.TryRemove(key, out _);
            }
            
            // Si aún excede el tamaño máximo, eliminar los menos accedidos
            if (_kernelCache.Count > maxCacheSize)
            {
                var keysToRemove = _cacheMetadata
                    .OrderBy(kvp => kvp.Value.AccessCount)
                    .ThenBy(kvp => kvp.Value.Created)
                    .Take(_kernelCache.Count - maxCacheSize)
                    .Select(kvp => kvp.Key)
                    .ToList();
                    
                foreach (var key in keysToRemove)
                {
                    _kernelCache.TryRemove(key, out _);
                    _cacheMetadata.TryRemove(key, out _);
                }
            }
        }
        catch
        {
            // Ignorar errores de limpieza para no afectar funcionalidad principal
        }
    }
    
    /// <summary>
    /// Invalidar cache de un agente específico
    /// </summary>
    public void InvalidateAgent(Guid agentId)
    {
        try
        {
            var keysToRemove = _kernelCache.Keys
                .Where(key => key.Contains($"agent_{agentId:N}"))
                .ToList();
                
            foreach (var key in keysToRemove)
            {
                _kernelCache.TryRemove(key, out _);
                _cacheMetadata.TryRemove(key, out _);
            }
        }
        catch
        {
            // Ignorar errores de invalidación
        }
    }
    
    /// <summary>
    /// Limpiar todo el cache
    /// </summary>
    public void ClearCache()
    {
        _kernelCache.Clear();
        _cacheMetadata.Clear();
    }

    // Método para crear kernels especializados que reutilicen la configuración
    public async Task<Kernel> CreateSpecializedKernelAsync(string purpose, AutoAgentes.Domain.Entities.Agent agent, Guid sessionId, CancellationToken ct)
    {
        // Crear kernel especializado como kernel mínimo (sin cache ni gobernanza)
        var specializedKernel = await CreateAsync(agent, sessionId, ct, minimalKernel: true);
        
        // Aplicar system prompt según el propósito
        var systemPrompt = purpose switch
        {
            "ArgumentBuilder" => "Eres un experto en construir argumentos para herramientas MCP. Analiza esquemas JSON y construye argumentos válidos.",
            "ResponseProcessor" => "Eres un experto en procesar respuestas de herramientas MCP. Extrae información relevante y formatea respuestas útiles.",
            "Planner" => "Eres un planificador experto. Crea planes estructurados usando solo las herramientas disponibles.",
            _ => "Eres un asistente útil especializado en la tarea solicitada."
        };

        // Configurar el kernel especializado
        // Nota: SetSystemPrompt no está disponible en esta versión de SK
        // El system prompt se maneja a nivel de ChatHistory

        await _emitter.EmitAsync(sessionId, new TraceEvent("specialized_kernel_created", new { 
            purpose, 
            provider = agent.Provider,
            reusedBase = false
        }, DateTimeOffset.UtcNow), ct);

        return specializedKernel;
    }
}

public static class KernelMcpExtensions
{
    // SanitizeFunctionName ahora está centralizado en SharedUtilities
    
    /// <summary>
    /// Obtener timeout configurado para una sección específica
    /// </summary>
    private static int DefaultTimeout(string section) => section.ToLowerInvariant() switch
    {
        "mcptool" => 30,
        "sktool" => 30,
        "planner" => 60,
        "summary" => 45,
        _ => 30
    };
    
    /// <summary>
    /// Construir parámetros SK desde JSON Schema
    /// </summary>
    private static IReadOnlyList<KernelParameterMetadata> BuildParametersFromJsonSchema(string? inputSchemaJson)
    {
        if (string.IsNullOrEmpty(inputSchemaJson))
        {
            // Fallback: parámetro genérico
            return new[]
            {
                new KernelParameterMetadata("args")
                {
                    Description = "Argumentos de la herramienta en formato JSON",
                    IsRequired = false,
                    DefaultValue = "{}"
                }
            };
        }
        
        try
        {
            // Parsear JSON Schema básico
            var schema = JsonDocument.Parse(inputSchemaJson);
            var parameters = new List<KernelParameterMetadata>();
            
            if (schema.RootElement.TryGetProperty("properties", out var properties))
            {
                foreach (var prop in properties.EnumerateObject())
                {
                    var param = new KernelParameterMetadata(prop.Name)
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
                new KernelParameterMetadata("args")
                {
                    Description = "Argumentos de la herramienta en formato JSON",
                    IsRequired = false,
                    DefaultValue = "{}"
                }
            };
        }
        catch
        {
            // Fallback en caso de error
            return new[]
            {
                new KernelParameterMetadata("args")
                {
                    Description = "Argumentos de la herramienta en formato JSON",
                    IsRequired = false,
                    DefaultValue = "{}"
                }
            };
        }
    }
    
    /// <summary>
    /// Obtener descripción de una propiedad del schema
    /// </summary>
    private static string GetPropertyDescription(JsonElement property)
    {
        if (property.TryGetProperty("description", out var desc))
        {
            return desc.GetString() ?? "Sin descripción";
        }
        return "Sin descripción";
    }
    
    /// <summary>
    /// Verificar si una propiedad es requerida
    /// </summary>
    private static bool IsPropertyRequired(string propertyName, JsonElement schema)
    {
        if (schema.TryGetProperty("required", out var required))
        {
            return required.EnumerateArray().Any(r => r.GetString() == propertyName);
        }
        return false;
    }
    
    /// <summary>
    /// Obtener valor por defecto de una propiedad
    /// </summary>
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
    
    /// <summary>
    /// Validar argumentos contra JSON Schema
    /// </summary>
    private static void ValidateArgsAgainstSchema(string schemaJson, KernelArguments args)
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
            
            // Validación de tipos y enum si hay propiedades definidas
            if (schema.RootElement.TryGetProperty("properties", out var properties))
            {
                foreach (var prop in properties.EnumerateObject())
                {
                    var propName = prop.Name;
                    if (args.TryGetValue(propName, out var argValue) && argValue != null)
                    {
                        ValidatePropertyValue(propName, argValue, prop.Value);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Error validando argumentos contra schema: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Verificar si un valor es numérico
    /// </summary>
    private static bool IsNumber(object value)
    {
        return value is int || value is long || value is double || value is decimal || 
               value is float || value is JsonElement je && je.ValueKind == JsonValueKind.Number;
    }
    
    /// <summary>
    /// Convertir cualquier valor numérico a double para comparaciones consistentes
    /// </summary>
    private static double ToDouble(object value)
    {
        return value switch
        {
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetDouble(),
            IConvertible c => Convert.ToDouble(c),
            _ => throw new ArgumentException("Valor no numérico")
        };
    }
    
    /// <summary>
    /// Validar valor de una propiedad contra su schema
    /// </summary>
    private static void ValidatePropertyValue(string propName, object argValue, JsonElement propertySchema)
    {
        // Validación de tipo
        if (propertySchema.TryGetProperty("type", out var typeElement))
        {
            var expectedType = typeElement.GetString();
            var actualType = argValue.GetType();
            
            switch (expectedType)
            {
                case "string":
                    if (!(argValue is string) &&
                        !(argValue is JsonElement je && je.ValueKind == JsonValueKind.String))
                    {
                        throw new ArgumentException($"Propiedad '{propName}' debe ser string, pero se recibió {actualType.Name}");
                    }
                    break;
                case "boolean":
                    if (!(argValue is bool) &&
                        !(argValue is JsonElement je2 && 
                          (je2.ValueKind == JsonValueKind.True || je2.ValueKind == JsonValueKind.False)))
                    {
                        throw new ArgumentException($"Propiedad '{propName}' debe ser boolean, pero se recibió {actualType.Name}");
                    }
                    break;
                case "number":
                    if (!IsNumber(argValue))
                    {
                        throw new ArgumentException($"Propiedad '{propName}' debe ser number, pero se recibió {actualType.Name}");
                    }
                    break;
            }
        }
        
        // Validación de enum - case-insensitive para strings
        if (propertySchema.TryGetProperty("enum", out var enumElement))
        {
            var enumValues = new List<object>();
            foreach (var enumVal in enumElement.EnumerateArray())
            {
                enumValues.Add(enumVal.ValueKind switch
                {
                    JsonValueKind.String => enumVal.GetString(),
                    JsonValueKind.Number => enumVal.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => enumVal.GetString()
                });
            }
            
            // Comparación case-insensitive para strings
            var isValid = enumValues.Any(enumVal => 
                enumVal is string enumStr && argValue is string argStr 
                    ? string.Equals(enumStr, argStr, StringComparison.OrdinalIgnoreCase)
                    : enumVal.Equals(argValue));
            
            if (!isValid)
            {
                throw new ArgumentException($"Propiedad '{propName}' debe ser uno de: {string.Join(", ", enumValues)}, pero se recibió: {argValue}");
            }
        }
        
        // Validación de restricciones numéricas - normalizar a double para comparaciones
        if (propertySchema.TryGetProperty("minimum", out var minElement) && IsNumber(argValue))
        {
            var minValue = minElement.GetDouble();
            if (ToDouble(argValue) < minValue)
            {
                throw new ArgumentException($"Propiedad '{propName}' debe ser >= {minValue}, pero se recibió: {argValue}");
            }
        }
        
        if (propertySchema.TryGetProperty("maximum", out var maxElement) && IsNumber(argValue))
        {
            var maxValue = maxElement.GetDouble();
            if (ToDouble(argValue) > maxValue)
            {
                throw new ArgumentException($"Propiedad '{propName}' debe ser <= {maxValue}, pero se recibió: {argValue}");
            }
        }
        
        // Validación de longitud de string
        if (argValue is string stringValue)
        {
            if (propertySchema.TryGetProperty("minLength", out var minLenElement))
            {
                var minLength = minLenElement.GetInt32();
                if (stringValue.Length < minLength)
                {
                    throw new ArgumentException($"Propiedad '{propName}' debe tener al menos {minLength} caracteres, pero tiene {stringValue.Length}");
                }
            }
            
            if (propertySchema.TryGetProperty("maxLength", out var maxLenElement))
            {
                var maxLength = maxLenElement.GetInt32();
                if (stringValue.Length > maxLength)
                {
                    throw new ArgumentException($"Propiedad '{propName}' debe tener máximo {maxLength} caracteres, pero tiene {stringValue.Length}");
                }
            }
        }
    }
    
    /// <summary>
    /// Convertir argumentos SK a JSON canónico
    /// </summary>
    private static string ArgsToCanonicalJson(IReadOnlyList<KernelParameterMetadata> parameters, KernelArguments args)
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

    public static async Task RegisterAgentToolsAsync(this Kernel kernel, Guid agentId, IMcpRegistry registry, IMcpCaller caller, ITraceEmitter emitter, Guid sessionId, CancellationToken ct, IToolRegistryService toolRegistry, int? skToolTimeoutSeconds = null)
    {
        await emitter.EmitAsync(sessionId, new TraceEvent("tools_registration_started", new { agentId }, DateTimeOffset.UtcNow), ct);
        
        var tools = await registry.ListBoundToolsAsync(agentId, ct);
        await emitter.EmitAsync(sessionId, new TraceEvent("tools_found", new { count = tools.Count }, DateTimeOffset.UtcNow), ct);
        
        // Un plugin por agente para evitar conflictos
        var pluginName = $"mcp_{agentId:N}";
        var functions = new List<KernelFunction>();
        var toolMappings = new Dictionary<string, (string? Scope, string Name, Guid ServerId, string? Schema)>();
        
        foreach (var t in tools)
        {
            // Capturar variables locales para evitar closures problemáticos
            var toolLocal = t;
            var canonical = string.IsNullOrWhiteSpace(toolLocal.Scope) ? toolLocal.Name : $"{toolLocal.Scope}.{toolLocal.Name}";
            var canonicalLocal = canonical;
            var schemaLocal = toolLocal.InputSchemaJson;
            var sanitizedName = SharedUtilities.SanitizeFunctionName(canonicalLocal);
            
            // Verificar si la función ya existe en el plugin
            if (kernel.Plugins.TryGetFunction(pluginName, sanitizedName, out _))
            {
                await emitter.EmitAsync(sessionId, new TraceEvent("tool_already_registered", new { 
                    agentId, 
                    toolName = canonicalLocal, 
                    sanitizedName 
                }, DateTimeOffset.UtcNow), ct);
                continue;
            }
            
            // Guardar mapping para ejecución posterior
            toolMappings[sanitizedName] = (toolLocal.Scope, toolLocal.Name, toolLocal.McpServerId, schemaLocal);
            
            // Crear parámetros desde JSON Schema si está disponible
            var parameters = BuildParametersFromJsonSchema(schemaLocal);
            
            // Crear función con parámetros tipados
            var function = kernel.CreateFunctionFromMethod(
                method: async (Kernel k, KernelArguments args, CancellationToken ctk) =>
                {
                    try
                    {
                        // Validar argumentos contra el schema si está disponible
                        if (!string.IsNullOrEmpty(schemaLocal))
                        {
                            ValidateArgsAgainstSchema(schemaLocal, args);
                        }
                        
                        // Convertir argumentos a JSON canónico
                        var payload = ArgsToCanonicalJson(parameters, args);
                        
                        // Llamar a la herramienta MCP con timeout configurable
                        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctk);
                        var skTimeout = TimeSpan.FromSeconds(skToolTimeoutSeconds ?? DefaultTimeout("SKTool"));
                        cts.CancelAfter(skTimeout);
                        
                        // Usar nombre canónico para la llamada MCP
                        return await caller.CallAsync(toolLocal.McpServerId, canonicalLocal, payload, cts.Token);
                    }
                    catch (OperationCanceledException oce) when (!ctk.IsCancellationRequested)
                    {
                        // Distinguir timeouts de cancelaciones normales
                        // TODO: Implementar métrica de timeouts cuando esté disponible
                        return "Error: Timeout ejecutando la herramienta";
                    }
                    catch (Exception ex)
                    {
                        return $"Error ejecutando herramienta: {ex.Message}";
                    }
                },
                functionName: sanitizedName,
                description: toolLocal.Description ?? $"MCP tool {canonicalLocal}",
                parameters: parameters
            );
            
            functions.Add(function);
        }
        
        // Crear y registrar el plugin si hay funciones
        if (functions.Count > 0)
        {
            var plugin = KernelPluginFactory.CreateFromFunctions(pluginName, functions);
            kernel.Plugins.Add(plugin);
            
            // Guardar el mapping en el servicio global de registro
            toolRegistry.RegisterTools(agentId, toolMappings);
        }
        
        await emitter.EmitAsync(sessionId, new TraceEvent("tools_registration_completed", new { 
            totalTools = tools.Count, 
            newTools = functions.Count,
            pluginName = pluginName,
            hasMappings = toolMappings.Count > 0
        }, DateTimeOffset.UtcNow), ct);
    }
}

public static class Governance
{
    public static void Attach(Kernel kernel, AutoAgentes.Domain.Entities.Agent agent, Guid sessionId)
    {
        // Configurar límites y políticas según el agente
        var maxTokens = agent.Autonomy switch
        {
            "Supervised" => 4000,
            "Autonomous" => 8000,
            "Unsupervised" => 12000,
            _ => 4000
        };

        // Configurar políticas de seguridad según el agente
        var securityLevel = agent.Autonomy switch
        {
            "Supervised" => "high",
            "Autonomous" => "medium", 
            "Unsupervised" => "low",
            _ => "high"
        };

        // Aplicar filtros de seguridad usando eventos legacy (compatible con SK 1.64.0)
        if (securityLevel.Equals("high", StringComparison.OrdinalIgnoreCase))
        {
            kernel.FunctionInvoking += (sender, args) =>
            {
                // Validar que no se ejecuten funciones peligrosas usando patrones regex
                var functionName = args.Function.Name;
                var dangerousPatterns = new[] { "^delete($|_)", "^remove($|_)", "^wipe($|_)", "^destroy($|_)", "^drop($|_)", "^truncate($|_)" };
                
                if (dangerousPatterns.Any(pattern => System.Text.RegularExpressions.Regex.IsMatch(functionName, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
                {
                    args.Cancel = true;
                    // Nota: args.Reason no está disponible en esta versión de SK
                }
            };
        }

        // Configurar límites de tokens y validaciones
        kernel.FunctionInvoking += (sender, args) =>
        {
            // Validar límites de tokens por función
            if (args.Function.Name.Contains("generate", StringComparison.OrdinalIgnoreCase))
            {
                // Para funciones de generación, limitar tokens
                // Esto se implementará cuando SK soporte límites por función
            }
        };

        // Configurar logging de todas las invocaciones con métricas
        kernel.FunctionInvoking += (sender, args) =>
        {
            // Métrica de función invocada
            // TODO: Implementar métricas cuando esté disponible
            // Telemetry.FunctionInvokingTotal.Add(1);
        };

        kernel.FunctionInvoked += (sender, args) =>
        {
            // Métrica de función ejecutada
            // TODO: Implementar métricas cuando esté disponible
            // Telemetry.FunctionInvokedTotal.Add(1);
        };
    }
    
    /// <summary>
    /// Crear configuración de ejecución basada en la autonomía del agente
    /// </summary>
    public static object CreateExecutionSettings(AutoAgentes.Domain.Entities.Agent agent)
    {
        // Para SK 1.64.0, retornamos un objeto simple con la configuración
        return new
        {
            Temperature = agent.Autonomy switch
            {
                "Supervised" => 0.1,    // Muy determinista
                "Autonomous" => 0.3,    // Moderadamente creativo
                "Unsupervised" => 0.5,  // Más creativo
                _ => 0.2
            },
            MaxTokens = agent.Autonomy switch
            {
                "Supervised" => 4000,
                "Autonomous" => 8000,
                "Unsupervised" => 12000,
                _ => 4000
            }
        };
    }
}


