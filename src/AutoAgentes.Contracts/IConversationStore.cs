using AutoAgentes.Domain.Entities;

namespace AutoAgentes.Contracts;

public interface IConversationStore
{
    Task<IReadOnlyList<Message>> GetLastAsync(Guid sessionId, int take, CancellationToken ct);
    Task AddAsync(Guid sessionId, string role, string content, string? kind, CancellationToken ct);
    Task<int> GetMessageCountAsync(Guid sessionId, CancellationToken ct);
    Task<IReadOnlyList<Message>> GetByKindAsync(Guid sessionId, string kind, int take, CancellationToken ct);
}

