using System.Diagnostics;
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
            // Obtener las herramientas vinculadas al agente
            var boundTools = await _mcpRegistry.ListBoundToolsAsync(agentId, ct);
            
            // Buscar la herramienta por nombre sanitizado
            var sanitizedFunctionName = SanitizeFunctionName(functionName);
            var tool = boundTools.FirstOrDefault(t => 
                SanitizeFunctionName(t.Name).Equals(sanitizedFunctionName, StringComparison.OrdinalIgnoreCase));
            
            if (tool == default)
            {
                return $"Herramienta '{functionName}' no encontrada para el agente";
            }
            
            // Emitir evento de ejecución de herramienta MCP
            await Emit(Guid.Empty, "mcp_tool_execution", new { 
                toolName = tool.Name, 
                sanitizedName = sanitizedFunctionName,
                userMessage 
            }, ct);
            
            // Llamar al servidor MCP real
            var mcpCaller = _serviceProvider.GetRequiredService<IMcpCaller>();
            
            // Crear argumentos para la herramienta MCP
            var args = new Dictionary<string, object>
            {
                ["prompt"] = userMessage
            };
            
            // Convertir argumentos a JSON
            var argsJson = System.Text.Json.JsonSerializer.Serialize(args);
            
            // Llamar a la herramienta MCP
            var result = await mcpCaller.CallAsync(tool.McpServerId, tool.Name, argsJson, ct);
            
            return result ?? "No se recibió respuesta del servidor MCP";
        }
        catch (Exception ex)
        {
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
}
