using System;

namespace AutoAgentes.Domain.Entities;

public class AuditLog
{
    public Guid Id { get; set; }
    public string Actor { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string? PayloadJson { get; set; }
    public DateTime AtUtc { get; set; }
}


