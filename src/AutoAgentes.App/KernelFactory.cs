using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using AutoAgentes.Contracts;

namespace AutoAgentes.App;

public interface IKernelFactory
{
    Task<Kernel> CreateAsync(AutoAgentes.Domain.Entities.Agent agent, Guid sessionId, CancellationToken ct);
}

public class KernelFactory : IKernelFactory
{
    private readonly IServiceProvider _sp;
    private readonly IMcpRegistry _registry;
    private readonly IMcpCaller _caller;
    private readonly ITraceEmitter _emitter;

    public KernelFactory(IServiceProvider sp, IMcpRegistry registry, IMcpCaller caller, ITraceEmitter emitter)
    { _sp = sp; _registry = registry; _caller = caller; _emitter = emitter; }

    public async Task<Kernel> CreateAsync(AutoAgentes.Domain.Entities.Agent agent, Guid sessionId, CancellationToken ct)
    {
        try
        {
            await _emitter.EmitAsync(sessionId, new TraceEvent("kernel_factory_started", new { agentId = agent.Id }, DateTimeOffset.UtcNow), ct);

            var builder = Kernel.CreateBuilder();

            var cfg = _sp.GetRequiredService<IConfiguration>();
            await _emitter.EmitAsync(sessionId, new TraceEvent("kernel_config", new { 
                deployment = cfg["OpenAI:Deployment"] ?? "gpt-4o",
                endpoint = cfg["OpenAI:Endpoint"],
                hasApiKey = !string.IsNullOrEmpty(cfg["OpenAI:ApiKey"])
            }, DateTimeOffset.UtcNow), ct);

            builder.AddAzureOpenAIChatCompletion(
                deploymentName: cfg["OpenAI:Deployment"] ?? "gpt-4o",
                endpoint: cfg["OpenAI:Endpoint"]!,
                apiKey:   cfg["OpenAI:ApiKey"]!
            );

            var kernel = builder.Build();
            await _emitter.EmitAsync(sessionId, new TraceEvent("kernel_built", new { }, DateTimeOffset.UtcNow), ct);

            await kernel.RegisterAgentToolsAsync(agent.Id, _registry, _caller, _emitter, sessionId, ct);

            Governance.Attach(kernel, agent, sessionId);

            await _emitter.EmitAsync(sessionId, new TraceEvent("kernel_factory_completed", new { }, DateTimeOffset.UtcNow), ct);

            return kernel;
        }
        catch (Exception ex)
        {
            await _emitter.EmitAsync(sessionId, new TraceEvent("kernel_factory_error", new { error = ex.Message }, DateTimeOffset.UtcNow), ct);
            throw;
        }
    }
}

public static class KernelMcpExtensions
{
    private static string SanitizeFunctionName(string input)
    {
        // Reemplazar puntos con guiones bajos
        var sanitized = input.Replace(".", "_");
        
        // Remover caracteres especiales y espacios, convertir a ASCII
        sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"[^a-zA-Z0-9_\s]", "");
        sanitized = sanitized.Replace(" ", "_");
        sanitized = sanitized.ToLowerInvariant();

        // Asegurar que empiece con letra
        if (sanitized.Length > 0 && !char.IsLetter(sanitized[0]))
        {
            sanitized = "tool_" + sanitized;
        }

        // Limitar longitud
        if (sanitized.Length > 50)
        {
            sanitized = sanitized.Substring(0, 50);
        }

        return sanitized;
    }

    public static async Task RegisterAgentToolsAsync(this Kernel kernel, Guid agentId, IMcpRegistry registry, IMcpCaller caller, ITraceEmitter emitter, Guid sessionId, CancellationToken ct)
    {
        await emitter.EmitAsync(sessionId, new TraceEvent("tools_registration_started", new { agentId }, DateTimeOffset.UtcNow), ct);
        
        var tools = await registry.ListBoundToolsAsync(agentId, ct);
        await emitter.EmitAsync(sessionId, new TraceEvent("tools_found", new { count = tools.Count }, DateTimeOffset.UtcNow), ct);
        
        foreach (var t in tools)
        {
            // Sanitizar el nombre de la función para Semantic Kernel
            var sanitizedName = SanitizeFunctionName(t.Name);
            
            kernel.CreateFunctionFromMethod(
                method: async (string argsJson, Kernel k, CancellationToken ctk) =>
                    await caller.CallAsync(t.McpServerId, t.Name, argsJson, ctk),
                functionName: sanitizedName,
                description: t.Description ?? $"MCP tool {t.Name}"
            );
            await emitter.EmitAsync(sessionId, new TraceEvent("tool_registered", new { toolName = t.Name, sanitizedName = sanitizedName, serverName = t.ServerName }, DateTimeOffset.UtcNow), ct);
        }
        
        await emitter.EmitAsync(sessionId, new TraceEvent("tools_registration_completed", new { }, DateTimeOffset.UtcNow), ct);
    }
}

public static class Governance
{
    public static void Attach(Kernel kernel, AutoAgentes.Domain.Entities.Agent agent, Guid sessionId)
    {
        // Hook: budgets, token measurement, input validation handled in IMcpCaller, ISessionsService
    }
}


