using Microsoft.AspNetCore.SignalR;
using AutoAgentes.Contracts;

namespace AutoAgentes.Api;

public class TraceHub : Hub 
{
    public async Task JoinGroup(string sessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
    }

    public async Task LeaveGroup(string sessionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
    }
}

public class SignalRTraceEmitter : ITraceEmitter
{
    private readonly IHubContext<TraceHub> _hub;
    
    public SignalRTraceEmitter(IHubContext<TraceHub> hub) 
    { 
        _hub = hub; 
    }

    public async Task EmitAsync(Guid sessionId, TraceEvent evt, CancellationToken ct)
    {
        // Solo enviar por SignalR, no guardar en base de datos aquí
        await _hub.Clients.Group(sessionId.ToString())
                       .SendAsync("trace", new { sessionId, evt.Kind, evt.Payload, evt.At }, ct);
    }
}


