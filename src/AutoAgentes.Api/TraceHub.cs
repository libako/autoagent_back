using Microsoft.AspNetCore.SignalR;
using AutoAgentes.Contracts;
using System.Collections.Concurrent;

namespace AutoAgentes.Api;

public class TraceHub : Hub 
{
    // 2. Implementar deduplicación en el servidor
    private static readonly ConcurrentDictionary<string, HashSet<string>> _sentEvents = new();
    
    public async Task JoinGroup(string sessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
        
        // 3. Enviar eventos de estado de sesión
        await SendSessionStatus(sessionId, "connected");
    }

    public async Task LeaveGroup(string sessionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
        
        // 3. Enviar eventos de estado de sesión
        await SendSessionStatus(sessionId, "disconnected");
    }

    // 2. Implementar deduplicación en el servidor
    public async Task SendTrace(string sessionId, TraceEvent traceEvent)
    {
        var key = $"{sessionId}:{traceEvent.Id}";
        
        if (_sentEvents.GetOrAdd(sessionId, _ => new HashSet<string>()).Add(key))
        {
            // Solo enviar si no se ha enviado antes
            await Clients.Group(sessionId).SendAsync("trace", traceEvent);
        }
    }

    // 3. Enviar eventos de estado de sesión
    public async Task SendSessionStatus(string sessionId, string status)
    {
        await Clients.Group(sessionId).SendAsync("sessionStatus", new { 
            SessionId = sessionId, 
            Status = status, 
            Timestamp = DateTime.UtcNow 
        });
    }

    // 4. Implementar heartbeat/keepalive
    public async Task SendHeartbeat(string sessionId)
    {
        await Clients.Group(sessionId).SendAsync("heartbeat", new { 
            SessionId = sessionId, 
            Timestamp = DateTime.UtcNow 
        });
    }

    // Limpiar eventos antiguos para evitar crecimiento de memoria
    public Task CleanupOldEvents(string sessionId, int maxEvents = 1000)
    {
        if (_sentEvents.TryGetValue(sessionId, out var events))
        {
            if (events.Count > maxEvents)
            {
                // Mantener solo los últimos eventos
                var eventsArray = events.ToArray();
                events.Clear();
                foreach (var evt in eventsArray.Skip(eventsArray.Length - maxEvents / 2))
                {
                    events.Add(evt);
                }
            }
        }
        
        return Task.CompletedTask;
    }
}

public class SignalRTraceEmitter : ITraceEmitter
{
    private readonly IHubContext<TraceHub> _hub;
    private readonly Timer _heartbeatTimer;
    private readonly ConcurrentDictionary<string, DateTime> _activeSessions = new();
    
    public SignalRTraceEmitter(IHubContext<TraceHub> hub) 
    { 
        _hub = hub; 
        
        // 4. Iniciar timer para heartbeat cada 30 segundos
        _heartbeatTimer = new Timer(SendHeartbeats, null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
    }

    public async Task EmitAsync(Guid sessionId, TraceEvent evt, CancellationToken ct)
    {
        var sessionIdStr = sessionId.ToString();
        
        // Marcar sesión como activa
        _activeSessions.AddOrUpdate(sessionIdStr, DateTime.UtcNow, (_, _) => DateTime.UtcNow);
        
        // 1. Enviar un ID único por evento
        // El TraceEvent ya incluye el ID único automáticamente
        
        // 2. Enviar directamente al cliente con deduplicación
        // La deduplicación se maneja en el cliente del frontend usando el ID único
        await _hub.Clients.Group(sessionIdStr).SendAsync("trace", evt, ct);
    }

    private async void SendHeartbeats(object? state)
    {
        var now = DateTime.UtcNow;
        var sessionsToRemove = new List<string>();
        
        foreach (var session in _activeSessions)
        {
            // Enviar heartbeat a sesiones activas
            await _hub.Clients.Group(session.Key).SendAsync("heartbeat", new { 
                SessionId = session.Key, 
                Timestamp = now 
            });
            
            // Limpiar sesiones inactivas (más de 5 minutos)
            if (now - session.Value > TimeSpan.FromMinutes(5))
            {
                sessionsToRemove.Add(session.Key);
            }
        }
        
        // Remover sesiones inactivas
        foreach (var sessionId in sessionsToRemove)
        {
            _activeSessions.TryRemove(sessionId, out _);
        }
    }

    public void Dispose()
    {
        _heartbeatTimer?.Dispose();
    }
}


