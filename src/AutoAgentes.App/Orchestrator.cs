using System.Diagnostics;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using AutoAgentes.Contracts;
using AutoAgentes.Infrastructure.Services;

namespace AutoAgentes.App;

public class Orchestrator
{
    private readonly ITraceEmitter _emitter;
    private readonly ISessionsService _sessions;
    private readonly IPlanner _planner;
    private readonly IKernelFactory _kernelFactory;

    public Orchestrator(ITraceEmitter emitter, ISessionsService sessions, IPlanner planner, IKernelFactory kernelFactory)
    { _emitter = emitter; _sessions = sessions; _planner = planner; _kernelFactory = kernelFactory; }

    public async Task RunAsync(Guid sessionId, AutoAgentes.Domain.Entities.Agent agent, string userMessage, CancellationToken ct)
    {
        using var activity = Telemetry.OrchestratorSource.StartActivity("run", ActivityKind.Server);
        activity?.SetTag("agent.id", agent.Id);
        activity?.SetTag("session.id", sessionId);

        // Crear kernel para el agente
        var kernel = await _kernelFactory.CreateAsync(agent, sessionId, ct);

        // Crear plan usando el planner
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
                var result = await step.InvokeAsync(kernel, new KernelArguments(), ct);
                output = result?.ToString();
                Telemetry.ToolCallsTotal.Add(1);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                await Emit(sessionId, "error", new { where = "tool", message = ex.Message }, ct);
            }

            await Emit(sessionId, "observation", new { idx = stepIdx, plugin = step.PluginName, function = step.Name, output, error, elapsedMs = (DateTime.UtcNow - started).TotalMilliseconds }, ct);
        }

        // Generar resumen usando el chat
        var chat = kernel.GetRequiredService<IChatCompletionService>();
        var summary = await chat.GetChatMessageContentAsync(
            chatHistory: new ChatHistory("Resume la solución anterior en 4-5 líneas, sin revelar razonamientos internos."),
            executionSettings: null,
            kernel: kernel,
            cancellationToken: ct);

        // await _sessions.AddAssistantMessageAsync(sessionId, summary.Content ?? string.Empty, ct);
        await Emit(sessionId, "summary", new { content = summary.Content }, ct);
    }

    private Task Emit(Guid sessionId, string kind, object payload, CancellationToken ct)
        => _emitter.EmitAsync(sessionId, new TraceEvent(kind, payload, DateTimeOffset.UtcNow), ct);
}


