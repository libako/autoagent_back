using System;

namespace AutoAgentes.Domain.Entities;

public class AgentToolBinding
{
    public Guid Id { get; set; }
    public Guid AgentId { get; set; }
    public Guid ToolId { get; set; }
    public string? ConfigJson { get; set; }
    public bool Enabled { get; set; } = true;

    public Agent? Agent { get; set; }
    public Tool? Tool { get; set; }
}


