using System.Net.Http;
using System.Text;
using AutoAgentes.Contracts;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;
using System.Diagnostics;
using System.Text.Json;

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

        // Crear el mensaje JSON-RPC 2.0 para MCP
        var mcpMessage = new
        {
            Jsonrpc = "2.0",
            id = Guid.NewGuid().ToString(),
            method = "tools/call",
            @params = new
            {
                name = toolName,
                arguments = JsonSerializer.Deserialize<JsonElement>(argsJson)
            }
        };

        var messageJson = JsonSerializer.Serialize(mcpMessage);
        _log.LogDebug("Enviando mensaje MCP: {Message}", messageJson);

        var content = new StringContent(messageJson, Encoding.UTF8, "application/json");
        var response = await policy.ExecuteAsync(ct => client.PostAsync("/", content, ct), ct);

        var resContent = await response.Content.ReadAsStringAsync(ct);
        _log.LogDebug("=== RESPUESTA MCP RECIBIDA ===");
        _log.LogDebug("Tool: {ToolName}", toolName);
        _log.LogDebug("Status Code: {StatusCode}", response.StatusCode);
        _log.LogDebug("Response Headers: {Headers}", string.Join(", ", response.Headers.Select(h => $"{h.Key}={string.Join(";", h.Value)}")));
        _log.LogDebug("Response Content: {Content}", resContent);
        _log.LogDebug("=== FIN RESPUESTA MCP ===");
        
        if (!response.IsSuccessStatusCode)
        {
            _log.LogWarning("MCP {Tool} -> {Status}: {Content}", toolName, response.StatusCode, resContent);
            throw new InvalidOperationException($"MCP {toolName} -> {response.StatusCode}: {resContent}");
        }

        // Parsear la respuesta JSON-RPC
        try
        {
            _log.LogDebug("=== PARSEANDO RESPUESTA JSON-RPC ===");
            var responseJson = JsonSerializer.Deserialize<JsonElement>(resContent);
            _log.LogDebug("JSON parseado exitosamente: {ValueKind}", responseJson.ValueKind);
            
            if (responseJson.TryGetProperty("result", out var resultElement))
            {
                _log.LogDebug("Result element encontrado: {ValueKind}", resultElement.ValueKind);
                _log.LogDebug("Result content: {Content}", resultElement.GetRawText());
                return resultElement.GetRawText();
            }
            else if (responseJson.TryGetProperty("error", out var errorElement) && errorElement.ValueKind != JsonValueKind.Null)
            {
                _log.LogDebug("Error element encontrado: {ValueKind}", errorElement.ValueKind);
                var errorMessage = errorElement.TryGetProperty("message", out var msgElement) 
                    ? msgElement.GetString() 
                    : "Unknown error";
                _log.LogDebug("Error message: {Message}", errorMessage);
                throw new InvalidOperationException($"MCP Error: {errorMessage}");
            }
            else
            {
                _log.LogDebug("No result ni error válido encontrado");
                _log.LogDebug("Response structure: {Structure}", responseJson.GetRawText());
            }
            _log.LogDebug("=== FIN PARSEO JSON-RPC ===");
        }
        catch (JsonException ex)
        {
            // Si no es JSON válido, devolver como está
            _log.LogWarning("Error deserializando respuesta MCP JSON: {Error}", ex.Message);
            _log.LogWarning("Respuesta MCP no es JSON válido: {Content}", resContent);
        }
        catch (Exception ex)
        {
            _log.LogError("Error inesperado parseando respuesta MCP: {Error}", ex.Message);
            _log.LogError("Stack trace: {StackTrace}", ex.StackTrace);
            throw;
        }

        return resContent;
    }
}


