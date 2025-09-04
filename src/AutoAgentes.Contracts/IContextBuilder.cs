using AutoAgentes.Domain.Entities;

namespace AutoAgentes.Contracts;

public interface IContextBuilder
{
    Task<object> BuildAsync(
        Agent agent, 
        Guid sessionId,
        string currentUserMessage,
        IEnumerable<(string Tool, string Summary)> currentObservations,
        CancellationToken ct);
}

