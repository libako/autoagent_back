namespace AutoAgentes.Contracts;

public interface ITraceEmitter : IDisposable
{
    Task EmitAsync(Guid sessionId, TraceEvent evt, CancellationToken ct);
}

public record TraceEvent(string Kind, object Payload, DateTimeOffset At, string Id = "", int? Idx = null)
{
    public string Id { get; init; } = string.IsNullOrEmpty(Id) ? Guid.NewGuid().ToString() : Id;
    public string Kind { get; init; } = Kind;
    public object Payload { get; init; } = Payload;
    public DateTimeOffset At { get; init; } = At;
    public int? Idx { get; init; } = Idx;
}

public interface IMcpCaller
{
    Task<string> CallAsync(Guid serverId, string toolName, string argsJson, CancellationToken ct);
}

public interface IOrchestrator
{
    Task RunAsync(Guid sessionId, AutoAgentes.Domain.Entities.Agent agent, string userMessage, CancellationToken ct);
}

public interface IMcpRegistry
{
    Task<IReadOnlyList<(Guid ToolId, Guid McpServerId, string ServerName, string Name, string? Description, string? InputSchemaJson, string? Scope)>> ListBoundToolsAsync(Guid agentId, CancellationToken ct);
    Task<IReadOnlyList<McpServerResponse>> GetServersAsync();
}

public interface IMcpServerRegistry
{
    Task<McpServerResponse> CreateServerAsync(CreateMcpServerRequest request);
    Task<IReadOnlyList<McpServerResponse>> GetServersAsync();
    Task<McpServerResponse?> GetServerAsync(Guid id);
    Task<DiscoverToolsResponse> DiscoverToolsAsync(Guid serverId);
}


