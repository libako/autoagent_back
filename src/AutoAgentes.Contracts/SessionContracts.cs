namespace AutoAgentes.Contracts;

public record CreateSessionRequest(Guid AgentId);
public record SessionCreatedResponse(Guid Id, Guid AgentId, string Status, string HubUrl, string Group);

public record PostMessageRequest(string Content);
public record AcceptedResponse(bool Accepted);

public record SessionResponse(Guid Id, Guid AgentId, string Status, DateTime CreatedAt, IReadOnlyList<MessageDto> Messages);
public record MessageDto(Guid Id, string Role, string Content, DateTime CreatedAt);

public record TraceItemDto(Guid Id, string Kind, object Payload, DateTime CreatedAt);
public record TracePageDto(IReadOnlyList<TraceItemDto> Items, int NextAfterIdx);


