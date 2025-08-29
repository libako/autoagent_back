using System;

namespace AutoAgentes.Domain.Entities;

public class Message
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string Role { get; set; } = string.Empty; // user | assistant | system
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public string? TokenUsageJson { get; set; }

    public Session? Session { get; set; }
}


