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
            // Intentar primero con HTTP (más simple y confiable)
            try
            {
                return await DiscoverToolsViaHttpAsync(server);
            }
            catch (Exception httpEx)
            {
                Console.WriteLine($"HTTP discovery failed for {server.BaseUrl}: {httpEx.Message}");
                
                // Si HTTP falla, intentar con WebSocket
                return await DiscoverToolsViaWebSocketAsync(server);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"All discovery methods failed for {server.BaseUrl}: {ex.Message}");
            
            // No devolver herramientas mock - esto puede causar confusión
            throw new InvalidOperationException($"No se pudieron descubrir herramientas del servidor {server.Name} ({server.BaseUrl}): {ex.Message}");
        }
    }

    private async Task<DiscoverToolsResponse> DiscoverToolsViaHttpAsync(McpServer server)
    {
        Console.WriteLine($"=== INICIANDO DESCUBRIMIENTO HTTP ===");
        Console.WriteLine($"URL del servidor: {server.BaseUrl}");
        Console.WriteLine($"Nombre del servidor: {server.Name}");
        
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        
        // Crear mensaje JSON-RPC 2.0 para listar herramientas
        var listToolsMessage = new
        {
            Jsonrpc = "2.0",
            id = Guid.NewGuid().ToString(),
            method = "tools/list",
            @params = new { }
        };

        var messageJson = System.Text.Json.JsonSerializer.Serialize(listToolsMessage);
        Console.WriteLine($"Mensaje enviado: {messageJson}");
        
        var content = new StringContent(messageJson, System.Text.Encoding.UTF8, "application/json");
        
        Console.WriteLine($"Enviando request a: {server.BaseUrl}");
        var response = await client.PostAsync(server.BaseUrl, content);
        var responseContent = await response.Content.ReadAsStringAsync();
        
        Console.WriteLine($"Status Code: {response.StatusCode}");
        Console.WriteLine($"Response Headers: {string.Join(", ", response.Headers.Select(h => $"{h.Key}={string.Join(";", h.Value)}"))}");
        Console.WriteLine($"Response Content: {responseContent}");
        
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"HTTP request failed: {response.StatusCode} - {responseContent}");
        }

        Console.WriteLine($"=== PARSEANDO RESPUESTA ===");
        
        JsonElement mcpResponse;
        try
        {
            mcpResponse = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(responseContent);
            Console.WriteLine($"Respuesta parseada como JSON: {mcpResponse.ValueKind}");
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Error deserializando respuesta JSON: {ex.Message}");
            Console.WriteLine($"Contenido de respuesta: {responseContent}");
            throw new InvalidOperationException($"Respuesta del servidor MCP no es JSON válido: {ex.Message}");
        }
        
        var tools = ParseToolsFromResponse(mcpResponse);
        Console.WriteLine($"Herramientas encontradas: {tools.Count}");
        
        // Guardar herramientas en BD
        await SaveToolsToDatabaseAsync(server.Id, tools);
        
        Console.WriteLine($"=== DESCUBRIMIENTO HTTP COMPLETADO ===");
        return new DiscoverToolsResponse(tools);
    }

    private async Task<DiscoverToolsResponse> DiscoverToolsViaWebSocketAsync(McpServer server)
    {
        Console.WriteLine($"=== INICIANDO DESCUBRIMIENTO WEBSOCKET ===");
        Console.WriteLine($"URL del servidor: {server.BaseUrl}");
        
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
                Jsonrpc = "2.0",
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
            Console.WriteLine("Mensaje 'initialize' enviado, esperando respuesta...");
            
            string initResponse;
            try
            {
                initResponse = await ReceiveWebSocketMessageAsync(client);
                Console.WriteLine($"MCP Initialize response: {initResponse}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error recibiendo respuesta de initialize: {ex.Message}");
                Console.WriteLine($"Estado del WebSocket: {client.State}");
                throw;
            }

            // Verificar que la inicialización fue exitosa
            var initJson = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(initResponse);
            Console.WriteLine($"Parseando respuesta de initialize: {initJson.ValueKind}");
            
            if (initJson.TryGetProperty("error", out var initError) && initError.ValueKind != JsonValueKind.Null)
            {
                var errorMessage = initError.TryGetProperty("message", out var msgElement) 
                    ? msgElement.GetString() 
                    : "Unknown error";
                throw new InvalidOperationException($"MCP Initialize error: {errorMessage}");
            }
            
            if (initJson.TryGetProperty("result", out var initResult))
            {
                Console.WriteLine($"✅ Inicialización MCP exitosa");
                if (initResult.TryGetProperty("Features", out var features))
                {
                    Console.WriteLine($"Características del servidor: {features}");
                }
            }
            else
            {
                Console.WriteLine("⚠️ No se encontró 'result' en la respuesta de initialize");
            }

            // Esperar un momento para que el servidor procese la inicialización
            Console.WriteLine("Esperando 100ms para estabilizar la conexión...");
            await Task.Delay(100);

            // NOTA: Este servidor MCP no implementa notifications/initialized
            // Vamos directamente a tools/list
            Console.WriteLine("⚠️ Servidor no implementa notifications/initialized, continuando directamente...");
            
            // Esperar un momento antes de solicitar herramientas
            Console.WriteLine("Esperando 100ms antes de solicitar herramientas...");
            await Task.Delay(100);

            // Solicitar lista de herramientas
            var listToolsMessage = new
            {
                Jsonrpc = "2.0",
                id = 2,
                method = "tools/list",
                @params = new { }
            };

            await SendWebSocketMessageAsync(client, listToolsMessage);
            Console.WriteLine("Solicitud 'tools/list' enviada");
            
            // Esperar respuesta de tools/list
            Console.WriteLine("Esperando respuesta de tools/list...");
            var toolsResponse = await ReceiveWebSocketMessageAsync(client);
            Console.WriteLine($"MCP Tools list response: {toolsResponse}");
            
            // Verificar que esta respuesta sea para tools/list, no para initialize
            var responseJson = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(toolsResponse);
            var responseId = GetIdAsString(responseJson);
            Console.WriteLine($"Respuesta recibida con ID: {responseId}");
            
            // Si recibimos una respuesta con ID 1, es del initialize anterior
            if (responseJson.TryGetProperty("id", out var idElement) && 
                (idElement.ValueKind == JsonValueKind.Number && idElement.GetInt32() == 1) ||
                (idElement.ValueKind == JsonValueKind.String && idElement.GetString() == "1"))
            {
                Console.WriteLine("⚠️ ADVERTENCIA: Recibimos respuesta tardía de initialize");
                Console.WriteLine("Esperando respuesta real de tools/list...");
                
                // Esperar otra respuesta
                toolsResponse = await ReceiveWebSocketMessageAsync(client);
                Console.WriteLine($"MCP Tools list response (segunda): {toolsResponse}");
                
                // Parsear la nueva respuesta
                responseJson = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(toolsResponse);
                var secondId = GetIdAsString(responseJson);
                Console.WriteLine($"Segunda respuesta con ID: {secondId}");
            }
            
            JsonElement mcpResponse;
            try
            {
                mcpResponse = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(toolsResponse);
                Console.WriteLine($"Respuesta WebSocket parseada como JSON: {mcpResponse.ValueKind}");
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"Error deserializando respuesta WebSocket JSON: {ex.Message}");
                Console.WriteLine($"Contenido de respuesta: {toolsResponse}");
                throw new InvalidOperationException($"Respuesta WebSocket del servidor MCP no es JSON válido: {ex.Message}");
            }
            
            var tools = ParseToolsFromResponse(mcpResponse);
            
            // Guardar herramientas en BD
            await SaveToolsToDatabaseAsync(server.Id, tools);
            
            Console.WriteLine($"=== DESCUBRIMIENTO WEBSOCKET COMPLETADO ===");
            return new DiscoverToolsResponse(tools);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error durante el descubrimiento WebSocket: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            throw;
        }
        finally
        {
            try
            {
                if (client.State == System.Net.WebSockets.WebSocketState.Open)
                {
                    Console.WriteLine("Cerrando conexión WebSocket...");
                    await client.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None);
                    Console.WriteLine("Conexión WebSocket cerrada");
                }
            }
            catch (Exception closeEx)
            {
                Console.WriteLine($"Error cerrando WebSocket: {closeEx.Message}");
            }
        }
    }

    private List<ToolItem> ParseToolsFromResponse(JsonElement mcpResponse)
    {
        var tools = new List<ToolItem>();
        
        Console.WriteLine($"=== PARSEANDO RESPUESTA MCP ===");
        Console.WriteLine($"Tipo de respuesta: {mcpResponse.ValueKind}");
        Console.WriteLine($"Contenido completo: {mcpResponse}");
        
        // Verificar si hay error en la respuesta
        if (mcpResponse.TryGetProperty("error", out var errorElement) && errorElement.ValueKind != JsonValueKind.Null)
        {
            var errorCode = errorElement.TryGetProperty("code", out var codeElement) ? codeElement.GetInt32() : -1;
            var errorMessage = errorElement.TryGetProperty("message", out var msgElement) ? msgElement.GetString() : "Unknown error";
            Console.WriteLine($"Error en respuesta MCP: Code={errorCode}, Message={errorMessage}");
            return tools; // Retornar lista vacía si hay error
        }
        
        // Parsear la respuesta MCP exitosa
        if (mcpResponse.TryGetProperty("result", out var resultElement))
        {
            Console.WriteLine($"Result element encontrado: {resultElement.ValueKind}");
            
            if (resultElement.TryGetProperty("tools", out var toolsElement) && 
                toolsElement.ValueKind == JsonValueKind.Array)
            {
                Console.WriteLine($"Found tools array with {toolsElement.GetArrayLength()} items");
                foreach (var toolElement in toolsElement.EnumerateArray())
                {
                                         try
                     {
                         var toolId = Guid.NewGuid();
                         var name = toolElement.TryGetProperty("Name", out var nameElement) ? nameElement.GetString() ?? "unknown" : "unknown";
                         var description = toolElement.TryGetProperty("Description", out var descElement) ? descElement.GetString() : null;
                         var scope = toolElement.TryGetProperty("Namespace", out var scopeElement) ? scopeElement.GetString() : "global";
                         var inputSchema = toolElement.TryGetProperty("InputSchema", out var schemaElement) ? schemaElement.GetRawText() : null;

                         Console.WriteLine($"Adding tool: {name} - {description}");
                         tools.Add(new ToolItem(toolId, name, description, scope, inputSchema));
                     }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error parsing tool element: {ex.Message}");
                        Console.WriteLine($"Tool element: {toolElement}");
                    }
                }
            }
            else
            {
                Console.WriteLine("No tools property found in result or tools is not an array");
                Console.WriteLine($"Result structure: {resultElement}");
            }
        }
        else
        {
            Console.WriteLine("No result property found in response");
            Console.WriteLine($"Response structure: {mcpResponse}");
        }
        
        Console.WriteLine($"=== PARSEO COMPLETADO: {tools.Count} herramientas ===");
        return tools;
    }

    private async Task SaveToolsToDatabaseAsync(Guid serverId, List<ToolItem> tools)
    {
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
            
            Console.WriteLine($"Saved {tools.Count} tools to database for server {serverId}");
        }
        else
        {
            Console.WriteLine($"No tools to save for server {serverId}");
        }
    }

    public async Task<IReadOnlyList<(Guid ToolId, Guid McpServerId, string ServerName, string Name, string? Description, string? InputSchemaJson, string? Scope)>> ListBoundToolsAsync(Guid agentId, CancellationToken ct)
    {
        // Obtener las herramientas vinculadas al agente
        var bindings = await _context.AgentToolBindings
            .Where(b => b.AgentId == agentId && b.Enabled)
            .Include(b => b.Tool)
            .Include(b => b.Tool!.McpServer)
            .ToListAsync(ct);

        var tools = new List<(Guid ToolId, Guid McpServerId, string ServerName, string Name, string? Description, string? InputSchemaJson, string? Scope)>();
        foreach (var binding in bindings)
        {
            if (binding.Tool != null)
            {
                tools.Add((
                    binding.Tool.Id,
                    binding.Tool.McpServerId,
                    binding.Tool.McpServer?.Name ?? "Unknown",
                    binding.Tool.Name,
                    binding.Tool.Description,
                    binding.Tool.InputSchemaJson,
                    binding.Tool.Scope));
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

    private string GetIdAsString(JsonElement response)
    {
        if (response.TryGetProperty("id", out var idElement))
        {
            return idElement.ValueKind switch
            {
                JsonValueKind.String => idElement.GetString() ?? "sin ID",
                JsonValueKind.Number => idElement.GetInt32().ToString(),
                _ => "sin ID"
            };
        }
        return "sin ID";
    }
}
