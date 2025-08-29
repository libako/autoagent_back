using System;

namespace AutoAgentes.Domain.Entities;

public class TraceStep
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public int Idx { get; set; }
    public string Kind { get; set; } = string.Empty; // plan|tool_call|observation|summary|error
    public string? PayloadJson { get; set; }
    public DateTime? StartedUtc { get; set; }
    public DateTime? EndedUtc { get; set; }
    public decimal? Cost { get; set; }

    public Session? Session { get; set; }
}


