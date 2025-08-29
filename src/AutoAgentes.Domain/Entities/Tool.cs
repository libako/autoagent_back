using System;

namespace AutoAgentes.Domain.Entities;

public class Tool
{
    public Guid Id { get; set; }
    public Guid McpServerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? InputSchemaJson { get; set; }
    public string? Scope { get; set; }
    public bool Enabled { get; set; } = true;

    public McpServer? McpServer { get; set; }
    public ICollection<AgentToolBinding> Bindings { get; set; } = new List<AgentToolBinding>();
}


