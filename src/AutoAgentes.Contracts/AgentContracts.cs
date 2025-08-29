namespace AutoAgentes.Contracts;

public record CreateAgentRequest(
    string Name,
    string? Description,
    string SystemPrompt,
    string Autonomy
);

public record UpdateAgentRequest(
    string? Name,
    string? Description,
    string? SystemPrompt,
    string? Autonomy
);

public record AgentResponse(
    Guid Id,
    string Name,
    string? Description,
    string SystemPrompt,
    string Autonomy,
    DateTime CreatedAt
);

public record CreateBindingRequest(Guid ToolId, object? Config, bool Enabled);
public record BindingResponse(Guid Id, Guid AgentId, Guid ToolId, bool Enabled);


