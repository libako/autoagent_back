using Microsoft.Extensions.DependencyInjection;
using AutoAgentes.Domain.Entities;
using AutoAgentes.Infrastructure;
using AutoAgentes.Contracts;

namespace AutoAgentes.App;

public class ConversationStore : IConversationStore
{
    private readonly IServiceProvider _serviceProvider;

    public ConversationStore(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<IReadOnlyList<Message>> GetLastAsync(Guid sessionId, int take, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        var messages = context.Messages
            .Where(m => m.SessionId == sessionId)
            .OrderByDescending(m => m.CreatedUtc)
            .Take(take)
            .ToList();
        
        return messages.OrderBy(m => m.CreatedUtc).ToList();
    }

    public async Task AddAsync(Guid sessionId, string role, string content, string? kind, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        var message = new Message
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            Role = role,
            Content = content,
            Kind = kind, // Nuevo campo para distinguir tipos de mensaje
            CreatedUtc = DateTime.UtcNow
        };

        context.Messages.Add(message);
        await context.SaveChangesAsync(ct);
    }

    public async Task<int> GetMessageCountAsync(Guid sessionId, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        return context.Messages
            .Where(m => m.SessionId == sessionId)
            .Count();
    }

    public async Task<IReadOnlyList<Message>> GetByKindAsync(Guid sessionId, string kind, int take, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        var messages = context.Messages
            .Where(m => m.SessionId == sessionId && m.Kind == kind)
            .OrderByDescending(m => m.CreatedUtc)
            .Take(take)
            .ToList();
        
        return messages.OrderBy(m => m.CreatedUtc).ToList();
    }
}
