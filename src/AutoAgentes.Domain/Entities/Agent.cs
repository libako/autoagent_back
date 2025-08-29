using System;

namespace AutoAgentes.Domain.Entities;

public class Agent
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string? TemplateRef { get; set; }
    public string? ParamsJson { get; set; }
    public string Autonomy { get; set; } = "Supervised"; // Manual|Supervised|Auto
    public double Temperature { get; set; } = 0.7;
    public double TopP { get; set; } = 1;
    public int ToolBudget { get; set; } = 10;
    public int TokenBudget { get; set; } = 50000;
    public string? GuardrailsJson { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }

    public ICollection<AgentToolBinding> Bindings { get; set; } = new List<AgentToolBinding>();
    public ICollection<Session> Sessions { get; set; } = new List<Session>();
}


