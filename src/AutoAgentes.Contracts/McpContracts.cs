namespace AutoAgentes.Contracts;

public record CreateMcpServerRequest(string Name, string BaseUrl, string AuthType, object? Credentials);
public record McpServerResponse(Guid Id, string Name, string? Status, DateTime? LastSeenUtc, string BaseUrl);

public record ToolItem(Guid Id, string Name, string? Description, string? Scope, string? InputSchemaJson);
public record DiscoverToolsResponse(IReadOnlyList<ToolItem> Tools);


