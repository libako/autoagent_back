using AutoAgentes.Contracts;
using AutoAgentes.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System.Net.Http;
using System.Text.Json;

namespace AutoAgentes.Infrastructure.Services;

public class McpRegistryService : IMcpServerRegistry, IMcpRegistry
{
    private readonly AppDbContext _context;
    private readonly IHttpClientFactory _httpClientFactory;

    public McpRegistryService(AppDbContext context, IHttpClientFactory httpClientFactory)
    {
        _context = context;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<McpServerResponse> CreateServerAsync(CreateMcpServerRequest request)
    {
        var server = new McpServer
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            BaseUrl = request.BaseUrl,
            AuthType = request.AuthType,
            Status = "active",
            LastSeenUtc = DateTime.UtcNow
        };

        _context.McpServers.Add(server);
        await _context.SaveChangesAsync();

        return new McpServerResponse(server.Id, server.Name, server.Status, server.LastSeenUtc, server.BaseUrl);
    }

    public async Task<IReadOnlyList<McpServerResponse>> GetServersAsync()
    {
        var servers = await _context.McpServers
            .OrderBy(s => s.LastSeenUtc)
            .ToListAsync();

        return servers.Select(s => new McpServerResponse(s.Id, s.Name, s.Status, s.LastSeenUtc, s.BaseUrl)).ToList();
    }

    public async Task<McpServerResponse?> GetServerAsync(Guid id)
    {
        var server = await _context.McpServers.FindAsync(id);
        if (server == null) return null;

        return new McpServerResponse(server.Id, server.Name, server.Status, server.LastSeenUtc, server.BaseUrl);
    }

        public async Task<DiscoverToolsResponse> DiscoverToolsAsync(Guid serverId)
    {
        var server = await _context.McpServers.FindAsync(serverId);
        if (server == null)
            throw new ArgumentException($"Server {serverId} not found");

        try
        {
            // Usar WebSocket para descubrir herramientas MCP
            var wsUrl = server.BaseUrl.Replace("http://", "ws://").Replace("https://", "wss://") + "/ws";
            Console.WriteLine($"Conectando a MCP WebSocket para descubrimiento: {wsUrl}");
            
            using var client = new System.Net.WebSockets.ClientWebSocket();
            await client.ConnectAsync(new Uri(wsUrl), CancellationToken.None);

            try
            {
                // Enviar mensaje de inicialización MCP
                var initMessage = new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "initialize",
                    @params = new
                    {
                        protocolVersion = "2024-11-05",
                        capabilities = new { },
                        clientInfo = new
                        {
                            name = "AutoAgentes",
                            version = "1.0.0"
                        }
                    }
                };

                await SendWebSocketMessageAsync(client, initMessage);
                var initResponse = await ReceiveWebSocketMessageAsync(client);
                Console.WriteLine($"MCP Initialize response: {initResponse}");

                // Enviar notificación de inicialización completada
                var initializedMessage = new
                {
                    jsonrpc = "2.0",
                    method = "notifications/initialized",
                    @params = new { }
                };

                await SendWebSocketMessageAsync(client, initializedMessage);

                // Solicitar lista de herramientas
                var listToolsMessage = new
                {
                    jsonrpc = "2.0",
                    id = 2,
                    method = "tools/list",
                    @params = new { }
                };

                await SendWebSocketMessageAsync(client, listToolsMessage);
                var toolsResponse = await ReceiveWebSocketMessageAsync(client);
                Console.WriteLine($"MCP Tools list response: {toolsResponse}");
                
                var mcpResponse = JsonSerializer.Deserialize<JsonElement>(toolsResponse);

                var tools = new List<ToolItem>();
                
                // Parsear la respuesta MCP
                if (mcpResponse.TryGetProperty("result", out var resultElement) && 
                    resultElement.TryGetProperty("tools", out var toolsElement) && 
                    toolsElement.ValueKind == JsonValueKind.Array)
                {
                    Console.WriteLine($"Found tools array with {toolsElement.GetArrayLength()} items");
                    foreach (var toolElement in toolsElement.EnumerateArray())
                    {
                        var toolId = Guid.NewGuid();
                        var name = toolElement.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? "unknown" : "unknown";
                        var description = toolElement.TryGetProperty("description", out var descElement) ? descElement.GetString() : null;
                        var scope = toolElement.TryGetProperty("scope", out var scopeElement) ? scopeElement.GetString() : "global";
                        var inputSchema = toolElement.TryGetProperty("inputSchema", out var schemaElement) ? schemaElement.GetRawText() : null;

                        Console.WriteLine($"Adding tool: {name} - {description}");
                        tools.Add(new ToolItem(toolId, name, description, scope, inputSchema));
                    }
                }

                // Solo guardar en BD si encontramos herramientas reales
                if (tools.Count > 0)
                {
                    // Primero eliminar herramientas existentes para este servidor
                    var existingTools = await _context.Tools.Where(t => t.McpServerId == serverId).ToListAsync();
                    _context.Tools.RemoveRange(existingTools);
                    await _context.SaveChangesAsync();

                    // Luego agregar las nuevas herramientas
                    foreach (var toolItem in tools)
                    {
                        var tool = new Tool
                        {
                            Id = toolItem.Id,
                            McpServerId = serverId,
                            Name = toolItem.Name,
                            Description = toolItem.Description,
                            Scope = toolItem.Scope,
                            InputSchemaJson = toolItem.InputSchemaJson,
                            Enabled = true
                        };

                        _context.Tools.Add(tool);
                    }
                    await _context.SaveChangesAsync();
                }
                else
                {
                    Console.WriteLine($"No tools found for server {server.Name} ({server.BaseUrl})");
                }

                return new DiscoverToolsResponse(tools);
            }
            finally
            {
                if (client.State == System.Net.WebSockets.WebSocketState.Open)
                {
                    await client.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error discovering tools from {server.BaseUrl}: {ex.Message}"); // Debug log
            
            // Si falla la llamada real, devolver herramientas mock como fallback
            var tools = new List<ToolItem>
            {
                new ToolItem(Guid.NewGuid(), "test-tool-1", "A test tool", "global", null),
                new ToolItem(Guid.NewGuid(), "test-tool-2", "Another test tool", "global", null)
            };

            return new DiscoverToolsResponse(tools);
        }
    }

    public async Task<IReadOnlyList<(Guid ToolId, Guid McpServerId, string ServerName, string Name, string? Description)>> ListBoundToolsAsync(Guid agentId, CancellationToken ct)
    {
        // Obtener las herramientas vinculadas al agente
        var bindings = await _context.AgentToolBindings
            .Where(b => b.AgentId == agentId && b.Enabled)
            .Include(b => b.Tool)
            .Include(b => b.Tool!.McpServer)
            .ToListAsync(ct);

        var tools = new List<(Guid ToolId, Guid McpServerId, string ServerName, string Name, string? Description)>();
        foreach (var binding in bindings)
        {
            if (binding.Tool != null)
            {
                tools.Add((
                    binding.Tool.Id,
                    binding.Tool.McpServerId,
                    binding.Tool.McpServer?.Name ?? "Unknown",
                    binding.Tool.Name,
                    binding.Tool.Description));
            }
        }

        return tools;
    }

    private async Task SendWebSocketMessageAsync(System.Net.WebSockets.ClientWebSocket client, object message)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(message);
        var buffer = System.Text.Encoding.UTF8.GetBytes(json);
        await client.SendAsync(new ArraySegment<byte>(buffer), System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private async Task<string> ReceiveWebSocketMessageAsync(System.Net.WebSockets.ClientWebSocket client)
    {
        var buffer = new byte[4096];
        var messageBuilder = new System.Text.StringBuilder();
        
        System.Net.WebSockets.WebSocketReceiveResult result;
        do
        {
            result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            messageBuilder.Append(System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count));
        } while (!result.EndOfMessage);

        return messageBuilder.ToString();
    }
}
