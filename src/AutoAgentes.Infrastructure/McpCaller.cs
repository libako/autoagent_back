using System.Net.Http;
using System.Text;
using AutoAgentes.Contracts;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;
using System.Diagnostics;

namespace AutoAgentes.Infrastructure;

public class McpCaller : IMcpCaller
{
    private readonly IHttpClientFactory _http;
    private readonly ILogger<McpCaller> _log;
    private readonly IMcpRegistry _mcpRegistry;

    public McpCaller(IHttpClientFactory http, ILogger<McpCaller> log, IMcpRegistry mcpRegistry)
    { 
        _http = http; 
        _log = log; 
        _mcpRegistry = mcpRegistry;
    }

    private static readonly ActivitySource Activity = new("AutoAgentes.McpCaller");

    public async Task<string> CallAsync(Guid serverId, string toolName, string argsJson, CancellationToken ct)
    {
        using var activity = Activity.StartActivity("mcp.call", ActivityKind.Client);
        activity?.SetTag("mcp.server_id", serverId);
        activity?.SetTag("mcp.tool", toolName);

        // Obtener la URL del servidor MCP desde la base de datos
        var servers = await _mcpRegistry.GetServersAsync();
        var server = servers.FirstOrDefault(s => s.Id == serverId);
        
        if (server == null)
        {
            throw new InvalidOperationException($"Servidor MCP {serverId} no encontrado");
        }

        // Crear un cliente HTTP con la URL base del servidor MCP
        var client = _http.CreateClient();
        client.BaseAddress = new Uri(server.BaseUrl);

        var policy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(r => (int)r.StatusCode == 429)
            .WaitAndRetryAsync(3, i => TimeSpan.FromMilliseconds(200 * Math.Pow(2, i)));

        var content = new StringContent(argsJson, Encoding.UTF8, "application/json");
        var response = await policy.ExecuteAsync(ct => client.PostAsync($"/tools/{toolName}:invoke", content, ct), ct);

        var resContent = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _log.LogWarning("MCP {Tool} -> {Status}: {Content}", toolName, response.StatusCode, resContent);
            throw new InvalidOperationException($"MCP {toolName} -> {response.StatusCode}: {resContent}");
        }
        return resContent;
    }
}


