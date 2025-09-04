using Microsoft.SemanticKernel.ChatCompletion;
using AutoAgentes.Domain.Entities;
using AutoAgentes.Contracts;

namespace AutoAgentes.App;

public sealed class ContextBuilder : IContextBuilder
{
    private readonly IConversationStore _store;
    private readonly IHistoryReducer _reducer;
    private readonly ITraceEmitter _emitter;
    private readonly int _maxHistoryMessages;
    private readonly int _maxObservationSummaryChars;

    public ContextBuilder(
        IConversationStore store, 
        IHistoryReducer reducer,
        ITraceEmitter emitter,
        int maxHistoryMessages = 200, // Aumentado de 50 a 200
        int maxObservationSummaryChars = 1000) // Aumentado de 200 a 1000
    {
        _store = store;
        _reducer = reducer;
        _emitter = emitter;
        _maxHistoryMessages = maxHistoryMessages;
        _maxObservationSummaryChars = maxObservationSummaryChars;
    }

    public async Task<object> BuildAsync(
        Agent agent, 
        Guid sessionId,
        string currentUserMessage,
        IEnumerable<(string Tool, string Summary)> currentObservations,
        CancellationToken ct)
    {
        var history = new ChatHistory();
        
        // 1. Añadir system prompt del agente
        string systemPrompt;
        if (!string.IsNullOrWhiteSpace(agent.SystemPrompt))
        {
            systemPrompt = agent.SystemPrompt;
        }
        else
        {
            systemPrompt = $"Eres {agent.Name}, un agente con autonomía {agent.Autonomy}. " +
                          "Responde de manera útil y directa al usuario.";
        }
        history.AddSystemMessage(systemPrompt);

        // 2. Cargar mensajes previos de la conversación
        var pastMessages = await _store.GetLastAsync(sessionId, _maxHistoryMessages, ct);
        
        foreach (var message in pastMessages.OrderBy(x => x.CreatedUtc))
        {
            switch (message.Role.ToLower())
            {
                case "user":
                    history.AddUserMessage(message.Content);
                    break;
                case "assistant":
                    history.AddAssistantMessage(message.Content);
                    break;
                case "system":
                    history.AddSystemMessage(message.Content);
                    break;
            }
        }

        // 3. Añadir observaciones del turno en curso (resumidas)
        foreach (var observation in currentObservations)
        {
            var summary = TruncateSummary(observation.Summary);
            history.AddAssistantMessage($"[tool:{observation.Tool}] {summary}");
        }

        // 4. Añadir mensaje actual del usuario
        history.AddUserMessage(currentUserMessage);

        // 5. Aplicar reducer según presupuesto de tokens
        var reducedHistory = _reducer.Apply(history);
        
        // 6. Emitir métricas de contexto
        var reducedChatHistory = reducedHistory as ChatHistory ?? new ChatHistory();
        await _emitter.EmitAsync(sessionId, new TraceEvent("context_builder_done", new
        {
            history_count_before = history.Count,
            history_count_after = reducedChatHistory.Count,
            reducer = _reducer.GetType().Name,
            whiteboard_size = currentObservations.Count(),
            session_id = sessionId
        }, DateTimeOffset.UtcNow), ct);

        return reducedHistory as object;
    }

    private string TruncateSummary(string summary)
    {
        if (string.IsNullOrEmpty(summary) || summary.Length <= _maxObservationSummaryChars)
            return summary ?? "";
        
        return summary.Substring(0, _maxObservationSummaryChars) + "...";
    }
}
