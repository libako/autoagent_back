using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using AutoAgentes.Contracts;

namespace AutoAgentes.App;

public class Planner : IPlanner
{
    private readonly IMcpRegistry _mcpRegistry;
    private readonly ITraceEmitter _traceEmitter;

    public Planner(IMcpRegistry mcpRegistry, ITraceEmitter traceEmitter)
    {
        _mcpRegistry = mcpRegistry;
        _traceEmitter = traceEmitter;
    }

    public async Task<(string Goal, IReadOnlyList<KernelFunction> Steps)> CreatePlanAsync(Kernel kernel, string userMessage, Guid agentId, CancellationToken ct)
    {
        var chat = kernel.GetRequiredService<IChatCompletionService>();
        
        // Obtener las herramientas vinculadas al agente dinámicamente
        var availableTools = new List<string>();
        var toolMappings = new Dictionary<string, string>(); // Mapeo de nombres originales a sanitizados
        try
        {
            // Obtener las herramientas vinculadas al agente específico
            var boundTools = await _mcpRegistry.ListBoundToolsAsync(agentId, ct);
            
            foreach (var tool in boundTools)
            {
                var originalName = tool.Name;
                var sanitizedName = SanitizeFunctionName(originalName);
                availableTools.Add(sanitizedName);
                toolMappings[sanitizedName] = originalName; // Guardar el mapeo
                
                // Debug: imprimir las herramientas encontradas
                Console.WriteLine($"Planner: Herramienta original: {originalName} -> sanitizada: {sanitizedName}");
            }
            
            Console.WriteLine($"Planner: Encontradas {availableTools.Count} herramientas para agente {agentId}");
        }
        catch (Exception ex)
        {
            // Si falla, usar herramientas por defecto
            Console.WriteLine($"Planner: Error obteniendo herramientas: {ex.Message}");
            availableTools = new List<string> { "meme_generate", "kb_search", "kb_summarize" };
        }
        
        var availableToolsString = string.Join(", ", availableTools);
        
        var planPrompt = $@"
Eres un planificador que debe crear un plan usando SOLO las herramientas disponibles.

Mensaje del usuario: {userMessage}

Herramientas disponibles: {availableToolsString}

IMPORTANTE: Usa ÚNICAMENTE los nombres exactos de las herramientas listadas arriba. NO combines nombres con descripciones.

Responde ÚNICAMENTE con este formato:
Goal: [objetivo claro]
Steps:
- [nombre exacto de la herramienta]
- [nombre exacto de la herramienta si es necesario]";

        // Emitir el prompt para debugging
        await _traceEmitter.EmitAsync(Guid.Empty, new TraceEvent("planner_prompt", new { prompt = planPrompt, availableTools = availableToolsString }, DateTimeOffset.UtcNow), ct);

        var response = await chat.GetChatMessageContentAsync(
            chatHistory: new ChatHistory(planPrompt),
            executionSettings: null,
            kernel: kernel,
            cancellationToken: ct);

        var content = response.Content ?? "No se pudo generar un plan";
        
        // Emitir la respuesta del LLM para debugging
        await _traceEmitter.EmitAsync(Guid.Empty, new TraceEvent("planner_response", new { response = content }, DateTimeOffset.UtcNow), ct);
        
        // Parsear la respuesta
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var goal = lines.FirstOrDefault(l => l.StartsWith("Goal:"))?.Replace("Goal:", "").Trim() ?? "Procesar mensaje";
        
        var steps = new List<KernelFunction>();
        
        // Crear pasos simples basados en el plan del LLM
        foreach (var line in lines.Where(l => l.StartsWith("-")))
        {
            var stepName = line.Replace("-", "").Trim();
            
            // Verificar si la herramienta está disponible
            var sanitizedStepName = SanitizeFunctionName(stepName);
            var isToolAvailable = availableTools.Any(t => 
                string.Equals(t, sanitizedStepName, StringComparison.OrdinalIgnoreCase));
            
            if (isToolAvailable)
            {
                // Crear un paso que represente la herramienta real
                var function = kernel.CreateFunctionFromMethod(
                    method: async (Kernel k, CancellationToken ctk) => 
                    {
                        // Este método será reemplazado por la ejecución real en el Orchestrator
                        return $"Paso planificado: {sanitizedStepName}";
                    },
                    functionName: sanitizedStepName,
                    description: $"Herramienta MCP: {sanitizedStepName}"
                );
                steps.Add(function);
            }
            else
            {
                // Si no encuentra la herramienta, crear un paso genérico
                var functionName = SanitizeFunctionName(stepName);
                var function = kernel.CreateFunctionFromMethod(
                    method: async (Kernel k, CancellationToken ctk) => $"Paso no disponible: {stepName}",
                    functionName: functionName,
                    description: $"Paso genérico: {stepName}"
                );
                steps.Add(function);
            }
        }

        // Si no hay pasos, crear uno por defecto
        if (!steps.Any())
        {
            var defaultFunction = kernel.CreateFunctionFromMethod(
                method: async (Kernel k, CancellationToken ctk) => "Procesando tu solicitud",
                functionName: "process_request",
                description: "Procesar la solicitud del usuario"
            );
            steps.Add(defaultFunction);
        }

        return (goal, steps);
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
}
