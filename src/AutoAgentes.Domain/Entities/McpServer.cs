using System;

namespace AutoAgentes.Domain.Entities;

public class McpServer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string AuthType { get; set; } = string.Empty; // none | apikey | oauth
    public string? Status { get; set; }
    public DateTime? LastSeenUtc { get; set; }
    public string? MetadataJson { get; set; }

    public ICollection<Tool> Tools { get; set; } = new List<Tool>();
}


