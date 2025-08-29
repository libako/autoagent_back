using AutoAgentes.Contracts;
using AutoAgentes.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Microsoft.Extensions.DependencyInjection;

namespace AutoAgentes.Infrastructure.Services;

public interface ISessionsService
{
    Task<SessionCreatedResponse> CreateSessionAsync(CreateSessionRequest request);
    Task<SessionResponse?> GetSessionAsync(Guid id);
    Task<AcceptedResponse> PostMessageAsync(Guid sessionId, PostMessageRequest request);
    Task<TracePageDto> GetTraceAsync(Guid sessionId, int afterIdx = 0, int limit = 100);
    Task PauseSessionAsync(Guid sessionId);
    Task ResumeSessionAsync(Guid sessionId);
    Task CancelSessionAsync(Guid sessionId);
    Task RetryLastStepAsync(Guid sessionId);
}

public class SessionsService : ISessionsService
{
    private readonly AppDbContext _context;
    private readonly ITraceEmitter _traceEmitter;
    private readonly IAgentsService _agentsService;
    private readonly IMcpServerRegistry _mcpRegistry;
    private readonly IServiceScopeFactory _scopeFactory;

    public SessionsService(
        AppDbContext context,
        ITraceEmitter traceEmitter,
        IAgentsService agentsService,
        IMcpServerRegistry mcpRegistry,
        IServiceScopeFactory scopeFactory)
    {
        _context = context;
        _traceEmitter = traceEmitter;
        _agentsService = agentsService;
        _mcpRegistry = mcpRegistry;
        _scopeFactory = scopeFactory;
    }

    public async Task<SessionCreatedResponse> CreateSessionAsync(CreateSessionRequest request)
    {
        var agent = await _agentsService.GetAgentAsync(request.AgentId);
        if (agent == null)
            throw new ArgumentException($"Agent {request.AgentId} not found");

        var session = new Session
        {
            Id = Guid.NewGuid(),
            AgentId = request.AgentId,
            Status = "running",
            StartedUtc = DateTime.UtcNow
        };

        _context.Sessions.Add(session);
        await _context.SaveChangesAsync();

        // Emitir evento de sesión creada
        await _traceEmitter.EmitAsync(session.Id, new TraceEvent("session_created", new { sessionId = session.Id, agentId = request.AgentId }, DateTimeOffset.UtcNow), CancellationToken.None);

        return new SessionCreatedResponse(session.Id, request.AgentId, "running", "/hubs/trace", session.Id.ToString());
    }

    public async Task<SessionResponse?> GetSessionAsync(Guid id)
    {
        var session = await _context.Sessions
            .Include(s => s.Messages.OrderBy(m => m.CreatedUtc))
            .FirstOrDefaultAsync(s => s.Id == id);

        if (session == null) return null;

        var messages = session.Messages.Select(m => new MessageDto(m.Id, m.Role, m.Content, m.CreatedUtc)).ToList();
        return new SessionResponse(session.Id, session.AgentId, session.Status, session.StartedUtc, messages);
    }

    public async Task<AcceptedResponse> PostMessageAsync(Guid sessionId, PostMessageRequest request)
    {
        var session = await _context.Sessions.FindAsync(sessionId);
        if (session == null)
            throw new ArgumentException($"Session {sessionId} not found");

        if (session.Status != "running")
            throw new InvalidOperationException($"Session {sessionId} is not running");

        // Guardar mensaje del usuario
        var userMessage = new Message
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            Role = "user",
            Content = request.Content,
            CreatedUtc = DateTime.UtcNow
        };

        _context.Messages.Add(userMessage);
        await _context.SaveChangesAsync();

        // Emitir evento de mensaje recibido
        await _traceEmitter.EmitAsync(sessionId, new TraceEvent("message_received", new { messageId = userMessage.Id, content = request.Content }, DateTimeOffset.UtcNow), CancellationToken.None);

        // Iniciar procesamiento asíncrono del agente con Semantic Kernel
        _ = Task.Run(async () =>
        {
            try
            {
                await _traceEmitter.EmitAsync(sessionId, new TraceEvent("background_task_started", new { }, DateTimeOffset.UtcNow), CancellationToken.None);
                
                var agent = await _agentsService.GetAgentAsync(session.AgentId);
                if (agent == null) 
                {
                    await _traceEmitter.EmitAsync(sessionId, new TraceEvent("agent_not_found", new { agentId = session.AgentId }, DateTimeOffset.UtcNow), CancellationToken.None);
                    return;
                }

                await _traceEmitter.EmitAsync(sessionId, new TraceEvent("agent_found", new { agentId = agent.Id, agentName = agent.Name }, DateTimeOffset.UtcNow), CancellationToken.None);

                // Crear scope y ejecutar usando DI
                using var scope = _scopeFactory.CreateScope();
                var orchestrator = scope.ServiceProvider.GetRequiredService<IOrchestrator>();
                await _traceEmitter.EmitAsync(sessionId, new TraceEvent("orchestrator_resolved", new { }, DateTimeOffset.UtcNow), CancellationToken.None);

                await orchestrator.RunAsync(sessionId, new AutoAgentes.Domain.Entities.Agent 
                { 
                    Id = agent.Id, 
                    Name = agent.Name,
                    Provider = "openai",
                    Autonomy = agent.Autonomy,
                    ParamsJson = agent.SystemPrompt
                }, request.Content, CancellationToken.None);
            }
            catch (Exception ex)
            {
                await _traceEmitter.EmitAsync(sessionId, new TraceEvent("background_task_error", new { message = ex.Message, stackTrace = ex.StackTrace }, DateTimeOffset.UtcNow), CancellationToken.None);
            }
        });

        return new AcceptedResponse(true);
    }

    public async Task<TracePageDto> GetTraceAsync(Guid sessionId, int afterIdx = 0, int limit = 100)
    {
        var session = await _context.Sessions.FindAsync(sessionId);
        if (session == null)
            throw new ArgumentException($"Session {sessionId} not found");

        var traceSteps = await _context.TraceSteps
            .Where(t => t.SessionId == sessionId)
            .OrderBy(t => t.StartedUtc)
            .Skip(afterIdx)
            .Take(limit)
            .ToListAsync();

        var traceItems = traceSteps.Select(t => new TraceItemDto(t.Id, t.Kind, t.PayloadJson ?? "{}", t.StartedUtc ?? DateTime.UtcNow)).ToList();

        return new TracePageDto(traceItems, afterIdx + limit);
    }





    public async Task PauseSessionAsync(Guid sessionId)
    {
        var session = await _context.Sessions.FindAsync(sessionId);
        if (session == null)
            throw new ArgumentException($"Session {sessionId} not found");

        session.Status = "paused";
        await _context.SaveChangesAsync();

        await _traceEmitter.EmitAsync(sessionId, new TraceEvent("session_paused", new { sessionId }, DateTimeOffset.UtcNow), CancellationToken.None);
    }

    public async Task ResumeSessionAsync(Guid sessionId)
    {
        var session = await _context.Sessions.FindAsync(sessionId);
        if (session == null)
            throw new ArgumentException($"Session {sessionId} not found");

        session.Status = "running";
        await _context.SaveChangesAsync();

        await _traceEmitter.EmitAsync(sessionId, new TraceEvent("session_resumed", new { sessionId }, DateTimeOffset.UtcNow), CancellationToken.None);
    }

    public async Task CancelSessionAsync(Guid sessionId)
    {
        var session = await _context.Sessions.FindAsync(sessionId);
        if (session == null)
            throw new ArgumentException($"Session {sessionId} not found");

        session.Status = "cancelled";
        await _context.SaveChangesAsync();

        await _traceEmitter.EmitAsync(sessionId, new TraceEvent("session_cancelled", new { sessionId }, DateTimeOffset.UtcNow), CancellationToken.None);
    }

    public async Task RetryLastStepAsync(Guid sessionId)
    {
        var session = await _context.Sessions.FindAsync(sessionId);
        if (session == null)
            throw new ArgumentException($"Session {sessionId} not found");

        // TODO: Implementar lógica de retry del último paso
        await _traceEmitter.EmitAsync(sessionId, new TraceEvent("retry_last_step", new { sessionId }, DateTimeOffset.UtcNow), CancellationToken.None);
    }
}
