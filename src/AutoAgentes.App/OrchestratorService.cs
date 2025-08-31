using System.Diagnostics;
using System.Text.Json;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using AutoAgentes.Contracts;
using AutoAgentes.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AutoAgentes.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AutoAgentes.App;

public class OrchestratorService : IOrchestrator
{
    private readonly ITraceEmitter _emitter;
    private readonly IPlanner _planner;
    private readonly IKernelFactory _kernelFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly IMcpRegistry _mcpRegistry;

    public OrchestratorService(
        ITraceEmitter emitter,
        IPlanner planner,
        IKernelFactory kernelFactory,
        IServiceProvider serviceProvider,
        IMcpRegistry mcpRegistry)
    {
        _emitter = emitter;
        _planner = planner;
        _kernelFactory = kernelFactory;
        _serviceProvider = serviceProvider;
        _mcpRegistry = mcpRegistry;
    }

    public async Task RunAsync(Guid sessionId, Agent agent, string userMessage, CancellationToken ct)
    {
        try
        {
            using var activity = Telemetry.OrchestratorSource.StartActivity("run", ActivityKind.Server);
            activity?.SetTag("agent.id", agent.Id);
            activity?.SetTag("session.id", sessionId);

            // Emitir evento de inicio
            await Emit(sessionId, "orchestrator_started", new { agentId = agent.Id, userMessage }, ct);

            // Crear kernel para el agente
            var kernel = await _kernelFactory.CreateAsync(agent, sessionId, ct);
            await Emit(sessionId, "kernel_created", new { agentId = agent.Id }, ct);

            // Crear plan usando el planner
            await Emit(sessionId, "planning_started", new { userMessage }, ct);
            var plan = await _planner.CreatePlanAsync(kernel, userMessage, agent.Id, ct);
            await Emit(sessionId, "plan", new { plan.Goal, Steps = plan.Steps.Select(s => s.Name) }, ct);

        // Ejecutar pasos del plan
        var stepIdx = 0;
        foreach (var step in plan.Steps)
        {
            stepIdx++;
            await Emit(sessionId, "tool_call", new { idx = stepIdx, plugin = step.PluginName, function = step.Name }, ct);

            var started = DateTime.UtcNow;
            string? output = null; string? error = null;
            try
            {
                // Intentar ejecutar la herramienta MCP real
                output = await ExecuteMcpToolAsync(agent.Id, step.Name, userMessage, ct);
                Telemetry.ToolCallsTotal.Add(1);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                await Emit(sessionId, "error", new { where = "tool", message = ex.Message }, ct);
            }

            await Emit(sessionId, "observation", new { idx = stepIdx, plugin = step.PluginName, function = step.Name, output, error, elapsedMs = (DateTime.UtcNow - started).TotalMilliseconds }, ct);
        }

                // Generar respuesta final al usuario
        await Emit(sessionId, "summary_started", new { }, ct);
        var chat = kernel.GetRequiredService<IChatCompletionService>();
        
        // Construir contexto con el mensaje del usuario y las observaciones
        var observations = new List<string>();
        for (int i = 1; i <= stepIdx; i++)
        {
            observations.Add($"Paso {i}: Completado");
        }
        
        var finalPrompt = $@"Eres un asistente útil. El usuario te dijo: ""{userMessage}""

Basándote en tu personalidad y capacidades, responde de manera natural y útil al usuario.

Responde directamente al usuario, no hagas un resumen de lo que hiciste.";

        var finalResponse = await chat.GetChatMessageContentAsync(
            chatHistory: new ChatHistory(finalPrompt),
            executionSettings: null,
            kernel: kernel,
            cancellationToken: ct);

        // Guardar mensaje del asistente
        await AddAssistantMessageAsync(sessionId, finalResponse.Content ?? string.Empty, ct);
        await Emit(sessionId, "summary", new { content = finalResponse.Content }, ct);
        await Emit(sessionId, "orchestrator_completed", new { }, ct);
        }
        catch (Exception ex)
        {
            await Emit(sessionId, "orchestrator_error", new { error = ex.Message, stackTrace = ex.StackTrace }, ct);
            throw;
        }
    }

    private async Task AddAssistantMessageAsync(Guid sessionId, string content, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        var message = new Message
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            Role = "assistant",
            Content = content,
            CreatedUtc = DateTime.UtcNow
        };

        context.Messages.Add(message);
        await context.SaveChangesAsync(ct);
    }

    private async Task<string> ExecuteMcpToolAsync(Guid agentId, string functionName, string userMessage, CancellationToken ct)
    {
        try
        {
            Console.WriteLine($"=== EJECUTANDO HERRAMIENTA MCP ===");
            Console.WriteLine($"Agent ID: {agentId}");
            Console.WriteLine($"Function Name: {functionName}");
            Console.WriteLine($"User Message: {userMessage}");
            
            // Obtener las herramientas vinculadas al agente
            var boundTools = await _mcpRegistry.ListBoundToolsAsync(agentId, ct);
            Console.WriteLine($"Herramientas vinculadas encontradas: {boundTools.Count}");
            
            // Buscar la herramienta por nombre sanitizado
            var sanitizedFunctionName = SanitizeFunctionName(functionName);
            Console.WriteLine($"Nombre sanitizado: {sanitizedFunctionName}");
            
            var tool = boundTools.FirstOrDefault(t => 
                SanitizeFunctionName(t.Name).Equals(sanitizedFunctionName, StringComparison.OrdinalIgnoreCase));
            
            if (tool == default)
            {
                Console.WriteLine($"Herramienta '{functionName}' no encontrada para el agente");
                return $"Herramienta '{functionName}' no encontrada para el agente";
            }
            
            Console.WriteLine($"Herramienta encontrada: {tool.Name} en servidor {tool.McpServerId}");
            Console.WriteLine($"Input Schema JSON: {tool.InputSchemaJson}");
            Console.WriteLine($"Input Schema es null/empty: {string.IsNullOrEmpty(tool.InputSchemaJson)}");
            
            // Emitir evento de ejecución de herramienta MCP
            await Emit(Guid.Empty, "mcp_tool_execution", new { 
                toolName = tool.Name, 
                sanitizedName = sanitizedFunctionName,
                userMessage 
            }, ct);
            
            // Llamar al servidor MCP real
            var mcpCaller = _serviceProvider.GetRequiredService<IMcpCaller>();
            
            // Crear argumentos para la herramienta MCP usando el LLM para que sea completamente dinámico
            var args = await BuildArgumentsFromSchemaWithLLMAsync(tool.InputSchemaJson, userMessage, tool.Name, tool.Description, ct);
            
            // Convertir argumentos a JSON
            var argsJson = System.Text.Json.JsonSerializer.Serialize(args);
            Console.WriteLine($"Argumentos JSON: {argsJson}");
            
            // Construir el nombre completo de la herramienta (namespace.tool)
            var fullToolName = $"{tool.Scope}.{tool.Name}";
            Console.WriteLine($"Nombre completo de herramienta: {fullToolName}");
            Console.WriteLine($"Llamando a mcpCaller.CallAsync con serverId={tool.McpServerId}, toolName={fullToolName}");
            
            // Llamar a la herramienta MCP
            var result = await mcpCaller.CallAsync(tool.McpServerId, fullToolName, argsJson, ct);
            
            Console.WriteLine($"Resultado recibido: {result}");
            Console.WriteLine($"=== HERRAMIENTA MCP EJECUTADA EXITOSAMENTE ===");
            
            // Procesar la respuesta usando el modelo LLM para que sea completamente dinámico
            var processedResult = await ProcessMcpResponseWithLLMAsync(tool.Name, result, tool.InputSchemaJson, tool.Description, userMessage, ct);
            
            return processedResult ?? "No se recibió respuesta del servidor MCP";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"=== ERROR EN EJECUCIÓN MCP ===");
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            Console.WriteLine($"=== FIN ERROR MCP ===");
            return $"Error ejecutando herramienta MCP: {ex.Message}";
        }
    }
    
    private static string SanitizeFunctionName(string input)
    {
        // Reemplazar puntos con guiones bajos
        var sanitized = input.Replace(".", "_");
        
        // Remover caracteres especiales excepto guiones bajos
        sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"[^a-zA-Z0-9_]", "");
        
        // Convertir a minúsculas
        sanitized = sanitized.ToLowerInvariant();
        
        // Asegurar que empiece con letra
        if (sanitized.Length > 0 && !char.IsLetter(sanitized[0]))
        {
            sanitized = "step_" + sanitized;
        }
        
        // Limitar longitud
        if (sanitized.Length > 50)
        {
            sanitized = sanitized.Substring(0, 50);
        }
        
        return sanitized;
    }

    private Task Emit(Guid sessionId, string kind, object payload, CancellationToken ct)
        => _emitter.EmitAsync(sessionId, new TraceEvent(kind, payload, DateTimeOffset.UtcNow), ct);

    private async Task<Dictionary<string, object>> BuildArgumentsFromSchemaWithLLMAsync(string? inputSchemaJson, string userMessage, string toolName, string? toolDescription, CancellationToken ct)
    {
        var args = new Dictionary<string, object>();
        
        if (string.IsNullOrEmpty(inputSchemaJson))
        {
            // Si no hay esquema, usar el genérico
            args["prompt"] = userMessage;
            return args;
        }

        try
        {
            Console.WriteLine($"Construyendo argumentos con LLM para herramienta: {toolName}");
            Console.WriteLine($"Esquema JSON: {inputSchemaJson}");
            
            // Crear un prompt inteligente para que el LLM construya los argumentos
            var prompt = $@"Eres un experto en construir argumentos para herramientas MCP basándote en su esquema JSON.

**Herramienta:** {toolName}
**Descripción:** {toolDescription ?? "Sin descripción"}
**Mensaje del usuario:** {userMessage}

**Esquema JSON de la herramienta:**
{inputSchemaJson}

**Instrucciones:**
1. Analiza el esquema JSON de la herramienta
2. Identifica las propiedades requeridas (required)
3. Para cada propiedad requerida, sugiere un valor apropiado basándote en:
   - El mensaje del usuario
   - El tipo de dato esperado
   - El contexto de la herramienta
   - Valores por defecto sensatos
4. Devuelve SOLO un JSON válido con los argumentos, sin explicaciones adicionales

**Formato de respuesta esperado:**
{{
  ""propiedad1"": ""valor1"",
  ""propiedad2"": ""valor2""
}}

**Ejemplo:**
Si el esquema requiere ""topText"" y ""template"", y el usuario dice ""hazme un meme de gatos"", podrías devolver:
{{
  ""topText"": ""hazme un meme de gatos"",
  ""template"": ""random""
}}

**Argumentos construidos:**";

            // Usar el kernel para construir argumentos con el LLM
            var kernel = await _kernelFactory.CreateAsync(new AutoAgentes.Domain.Entities.Agent 
            { 
                Id = Guid.Empty, 
                Name = "ArgumentBuilder",
                Provider = "openai",
                Autonomy = "Supervised"
            }, Guid.Empty, ct);
            
            var chat = kernel.GetRequiredService<IChatCompletionService>();
            
            var llmResponse = await chat.GetChatMessageContentAsync(
                chatHistory: new ChatHistory(prompt),
                executionSettings: null,
                kernel: kernel,
                cancellationToken: ct);

            var responseContent = llmResponse.Content ?? "{}";
            Console.WriteLine($"Respuesta del LLM para argumentos: {responseContent}");

            // Intentar parsear la respuesta JSON del LLM
            try
            {
                var parsedArgs = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(responseContent);
                if (parsedArgs != null)
                {
                    args = parsedArgs;
                    Console.WriteLine($"Argumentos construidos por LLM: {string.Join(", ", args.Select(kvp => $"{kvp.Key}={kvp.Value}"))}");
                }
            }
            catch (Exception parseEx)
            {
                Console.WriteLine($"Error parseando respuesta JSON del LLM: {parseEx.Message}");
                // Fallback: usar el esquema genérico
                args["prompt"] = userMessage;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error construyendo argumentos con LLM: {ex.Message}");
            // Fallback al esquema genérico
            args["prompt"] = userMessage;
        }
        
        return args;
    }

    private async Task<string> ProcessMcpResponseWithLLMAsync(string toolName, string? response, string? inputSchemaJson, string? toolDescription, string userMessage, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(response))
            return "No se recibió respuesta del servidor MCP";

        try
        {
            // Crear un prompt inteligente para que el LLM procese la respuesta
            var prompt = $@"Eres un asistente experto en procesar respuestas de herramientas MCP.

**Herramienta:** {toolName}
**Descripción:** {toolDescription ?? "Sin descripción"}
**Mensaje del usuario:** {userMessage}

**Respuesta de la herramienta MCP:**
{response}

**Instrucciones:**
1. Analiza la respuesta de la herramienta MCP
2. Extrae la información más relevante y útil
3. Formatea la respuesta de manera clara y natural
4. Responde directamente al usuario, no hagas un resumen técnico
5. Si hay URLs, imágenes, o contenido especial, inclúyelo de manera útil
6. Mantén el contexto de lo que pidió el usuario

**Respuesta procesada:**";

            // Usar el kernel para procesar con el LLM
            var kernel = await _kernelFactory.CreateAsync(new AutoAgentes.Domain.Entities.Agent 
            { 
                Id = Guid.Empty, 
                Name = "ResponseProcessor",
                Provider = "openai",
                Autonomy = "Supervised"
            }, Guid.Empty, ct);
            
            var chat = kernel.GetRequiredService<IChatCompletionService>();
            
            var llmResponse = await chat.GetChatMessageContentAsync(
                chatHistory: new ChatHistory(prompt),
                executionSettings: null,
                kernel: kernel,
                cancellationToken: ct);

            return llmResponse.Content ?? "Error procesando la respuesta con el LLM";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error procesando respuesta MCP con LLM: {ex.Message}");
            // Fallback: devolver la respuesta tal como está
            return response;
        }
    }
}
