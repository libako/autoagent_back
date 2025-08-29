using AutoAgentes.Contracts;

namespace AutoAgentes.Infrastructure.Services;

public class McpCallerStub : IMcpCaller
{
    public async Task<string> CallAsync(Guid serverId, string toolName, string argsJson, CancellationToken ct)
    {
        // Simular una llamada a herramienta MCP
        await Task.Delay(100, ct); // Simular latencia
        
        return $"{{\"result\": \"Ejecutado {toolName} con argumentos: {argsJson}\", \"success\": true}}";
    }
}
