using System;

namespace AutoAgentes.Domain.Entities;

public class Session
{
    public Guid Id { get; set; }
    public Guid AgentId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Status { get; set; } = "running";
    public DateTime StartedUtc { get; set; }
    public DateTime? EndedUtc { get; set; }
    public int LastEventIdx { get; set; }

    public Agent? Agent { get; set; }
    public ICollection<Message> Messages { get; set; } = new List<Message>();
    public ICollection<TraceStep> TraceSteps { get; set; } = new List<TraceStep>();
}


