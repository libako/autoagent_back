using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AutoAgentes.Contracts;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;

namespace AutoAgentes.Infrastructure;

public class McpWebSocketCaller : IMcpCaller
{
    private readonly ILogger<McpWebSocketCaller> _log;
    private readonly IMcpRegistry _mcpRegistry;

    public McpWebSocketCaller(ILogger<McpWebSocketCaller> log, IMcpRegistry mcpRegistry)
    {
        _log = log;
        _mcpRegistry = mcpRegistry;
    }

    private static readonly ActivitySource Activity = new("AutoAgentes.McpWebSocketCaller");

    public async Task<string> CallAsync(Guid serverId, string toolName, string argsJson, CancellationToken ct)
    {
        using var activity = Activity.StartActivity("mcp.websocket.call", ActivityKind.Client);
        activity?.SetTag("mcp.server_id", serverId);
        activity?.SetTag("mcp.tool", toolName);

        // Obtener la URL del servidor MCP desde la base de datos
        var servers = await _mcpRegistry.GetServersAsync();
        var server = servers.FirstOrDefault(s => s.Id == serverId);
        
        if (server == null)
        {
            throw new InvalidOperationException($"Servidor MCP {serverId} no encontrado");
        }

        // Convertir URL HTTP a WebSocket
        var wsUrl = server.BaseUrl.Replace("http://", "ws://").Replace("https://", "wss://") + "/ws";
        
        _log.LogInformation("Conectando a MCP WebSocket: {WsUrl}", wsUrl);

        // Intentar primero con ClientWebSocket estándar
        try
        {
            return await CallWithStandardWebSocket(wsUrl, toolName, argsJson, ct);
        }
        catch (Exception ex) when (ex.Message.Contains("400") || ex.Message.Contains("101") || ex.Message.Contains("WebSocket"))
        {
            _log.LogWarning("WebSocket estándar falló, intentando con HTTP upgrade manual: {Error}", ex.Message);
            
            // Si falla, intentar con HTTP upgrade manual
            try
            {
                return await CallWithHttpUpgrade(server.BaseUrl, toolName, argsJson, ct);
            }
            catch (Exception httpEx)
            {
                _log.LogError("HTTP upgrade también falló: {Error}", httpEx.Message);
                throw new InvalidOperationException($"Tanto WebSocket como HTTP fallaron. WebSocket: {ex.Message}, HTTP: {httpEx.Message}");
            }
        }
        catch (Exception ex)
        {
            _log.LogError("Error inesperado en WebSocket: {Error}", ex.Message);
            throw;
        }
    }

    private async Task<string> CallWithStandardWebSocket(string wsUrl, string toolName, string argsJson, CancellationToken ct)
    {
        using var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri(wsUrl), ct);

        try
        {
            // Paso 1: Enviar mensaje de inicialización MCP
            var initMessage = new
            {
                Jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { tools = new { } },
                    clientInfo = new
                    {
                        name = "AutoAgentes",
                        version = "1.0.0"
                    }
                }
            };

            await SendMessageAsync(client, initMessage, ct);
            var initResponse = await ReceiveMessageAsync(client, ct);
            
            _log.LogDebug("MCP Initialize response: {Response}", initResponse);

            // Verificar que la inicialización fue exitosa
            var initJson = JsonSerializer.Deserialize<JsonElement>(initResponse);
            if (initJson.TryGetProperty("error", out var initError) && initError.ValueKind != JsonValueKind.Null)
            {
                var errorMessage = initError.TryGetProperty("message", out var msgElement) 
                    ? msgElement.GetString() 
                    : "Unknown error";
                throw new InvalidOperationException($"MCP Initialize error: {errorMessage}");
            }

            // Paso 2: Enviar notificación de inicialización completada
            /*var initializedMessage = new
            {
                Jsonrpc = "2.0",
                method = "notifications/initialized",
                @params = new { }
            };

            await SendMessageAsync(client, initializedMessage, ct);
            _log.LogDebug("Sent initialized notification");*/

            // Esperar un poco para que el servidor procese la notificación
            await Task.Delay(100, ct);

            // Paso 3: Llamar a la herramienta
            var callId = 2; // ID único para la llamada
            var callMessage = new
            {
                Jsonrpc = "2.0",
                id = callId,
                method = "tools/call",
                @params = new
                {
                    name = toolName,
                    arguments = JsonSerializer.Deserialize<JsonElement>(argsJson)
                }
            };

            await SendMessageAsync(client, callMessage, ct);
            
            // Esperar la respuesta de la herramienta
            string? callResponse = null;
            var timeout = Task.Delay(TimeSpan.FromSeconds(30), ct);
            
            while (!timeout.IsCompleted)
            {
                try
                {
                    var response = await ReceiveMessageAsync(client, ct);
                    var responseJson = JsonSerializer.Deserialize<JsonElement>(response);
                    
                    // Si es una notificación, ignorarla y seguir esperando
                    if (responseJson.TryGetProperty("method", out var methodElement))
                    {
                        _log.LogDebug("Received notification: {Method}", methodElement.GetString());
                        continue;
                    }
                    
                    // Si es la respuesta a nuestra llamada
                    if (responseJson.TryGetProperty("id", out var idElement) && 
                        GetIdAsInt32(idElement) == callId)
                    {
                        callResponse = response;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarning("Error receiving message: {Error}", ex.Message);
                    break;
                }
            }
            
            if (callResponse == null)
            {
                throw new InvalidOperationException("Timeout esperando respuesta del servidor MCP");
            }
            
            _log.LogDebug("MCP Tool call response: {Response}", callResponse);

            // Parsear la respuesta
            var resultJson = JsonSerializer.Deserialize<JsonElement>(callResponse);
            
            if (resultJson.TryGetProperty("error", out var errorElement) && errorElement.ValueKind != JsonValueKind.Null)
            {
                var errorMessage = errorElement.TryGetProperty("message", out var msgElement) 
                    ? msgElement.GetString() 
                    : "Unknown error";
                throw new InvalidOperationException($"MCP Tool error: {errorMessage}");
            }

            if (resultJson.TryGetProperty("result", out var resultElement))
            {
                return resultElement.GetRawText();
            }

            return callResponse;
        }
        finally
        {
            if (client.State == WebSocketState.Open)
            {
                await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", ct);
            }
        }
    }

    private async Task<string> CallWithHttpUpgrade(string baseUrl, string toolName, string argsJson, CancellationToken ct)
    {
        // Crear una solicitud HTTP con upgrade a WebSocket
        var request = (HttpWebRequest)WebRequest.Create($"{baseUrl}/ws");
        request.Method = "GET";
        request.Headers.Add("Upgrade", "websocket");
        request.Headers.Add("Connection", "Upgrade");
        request.Headers.Add("Sec-WebSocket-Key", Convert.ToBase64String(Guid.NewGuid().ToByteArray()));
        request.Headers.Add("Sec-WebSocket-Version", "13");
        request.Headers.Add("Sec-WebSocket-Protocol", "mcp");

        _log.LogInformation("Intentando HTTP upgrade a WebSocket en: {Url}", request.RequestUri);

        try
        {
            using var response = (HttpWebResponse)await request.GetResponseAsync();
            
            if (response.StatusCode == HttpStatusCode.SwitchingProtocols)
            {
                _log.LogInformation("HTTP upgrade exitoso, usando stream para comunicación");
                
                // Usar el stream para comunicación
                using var stream = response.GetResponseStream();
                if (stream != null)
                {
                    // Enviar mensaje MCP por el stream
                    var mcpMessage = new
                    {
                        Jsonrpc = "2.0",
                        id = 1,
                        method = "tools/call",
                        @params = new
                        {
                            name = toolName,
                            arguments = JsonSerializer.Deserialize<JsonElement>(argsJson)
                        }
                    };

                    var json = JsonSerializer.Serialize(mcpMessage);
                    var buffer = Encoding.UTF8.GetBytes(json);
                    await stream.WriteAsync(buffer, 0, buffer.Length, ct);

                    // Leer respuesta
                    var responseBuffer = new byte[4096];
                    var bytesRead = await stream.ReadAsync(responseBuffer, 0, responseBuffer.Length, ct);
                    var responseText = Encoding.UTF8.GetString(responseBuffer, 0, bytesRead);

                    _log.LogDebug("Respuesta MCP: {Response}", responseText);
                    return responseText;
                }
            }
            
            throw new InvalidOperationException($"HTTP upgrade falló con status: {response.StatusCode}");
        }
        catch (WebException ex)
        {
            if (ex.Response is HttpWebResponse httpResponse)
            {
                throw new InvalidOperationException($"HTTP upgrade falló con status: {httpResponse.StatusCode} - {httpResponse.StatusDescription}");
            }
            throw;
        }
    }

    private async Task SendMessageAsync(ClientWebSocket client, object message, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(message);
        var buffer = Encoding.UTF8.GetBytes(json);
        await client.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, ct);
        _log.LogDebug("Sent MCP message: {Message}", json);
    }

    private async Task<string> ReceiveMessageAsync(ClientWebSocket client, CancellationToken ct)
    {
        var buffer = new byte[4096];
        var messageBuilder = new StringBuilder();
        
        WebSocketReceiveResult result;
        do
        {
            result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
        } while (!result.EndOfMessage);

        var message = messageBuilder.ToString();
        _log.LogDebug("Received MCP message: {Message}", message);
        return message;
    }

    private int GetIdAsInt32(JsonElement idElement)
    {
        return idElement.ValueKind switch
        {
            JsonValueKind.String => int.TryParse(idElement.GetString(), out var result) ? result : -1,
            JsonValueKind.Number => idElement.GetInt32(),
            _ => -1
        };
    }
}
