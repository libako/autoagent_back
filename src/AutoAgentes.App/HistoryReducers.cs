using Microsoft.SemanticKernel.ChatCompletion;
using AutoAgentes.Contracts;

namespace AutoAgentes.App;

public sealed class LastKMessagesReducer : IHistoryReducer
{
    private readonly int _k;
    
    public LastKMessagesReducer(int k = 100) => _k = k; // Aumentado de 20 a 100 - LÍMITE CRÍTICO

    public object Apply(object input, int maxTokens = 8000) // Aumentado de 4000 a 8000
    {
        var history = input as ChatHistory ?? new ChatHistory();
        var items = history.ToList();
        var sliced = items.Skip(Math.Max(0, items.Count - _k)).ToList();
        
        var output = new ChatHistory();
        foreach (var item in sliced)
        {
            output.Add(item);
        }
        
        return output;
    }
}

public sealed class WhiteboardReducer : IHistoryReducer
{
    private readonly int _maxRecentMessages;
    private readonly int _maxWhiteboardItems;
    
    public WhiteboardReducer(int maxRecentMessages = 20, int maxWhiteboardItems = 25) // Aumentado de 15,8 a 20,25
    {
        _maxRecentMessages = maxRecentMessages;
        _maxWhiteboardItems = maxWhiteboardItems;
    }

    public object Apply(object input, int maxTokens = 8000) // Aumentado de 4000 a 8000
    {
        var history = input as ChatHistory ?? new ChatHistory();
        var items = history.ToList();
        var output = new ChatHistory();
        
        // Extraer hechos/decisiones clave (simulado - en producción usarías LLM)
        var whiteboardItems = ExtractKeyFacts(items.Cast<dynamic>().ToList());
        
        // Añadir whiteboard como system message
        if (whiteboardItems.Any())
        {
            var whiteboardContent = string.Join("\n", whiteboardItems.Take(_maxWhiteboardItems));
            output.AddSystemMessage($"Contexto clave de la conversación:\n{whiteboardContent}");
        }
        
        // Añadir últimos mensajes recientes
        var recentMessages = items.Skip(Math.Max(0, items.Count - _maxRecentMessages)).ToList();
        foreach (var item in recentMessages)
        {
            output.Add(item);
        }
        
        return output;
    }

    private List<string> ExtractKeyFacts(List<dynamic> messages)
    {
        var facts = new List<string>();
        
        // Extraer TODOS los hechos importantes, no solo patrones específicos
        foreach (var message in messages)
        {
            var content = message.Content ?? "";
            
            // 1. INFORMACIÓN PERSONAL (nombres, fechas, preferencias)
            if (content.Contains("me llamo") || content.Contains("mi nombre es"))
                facts.Add($"Nombre: {ExtractSummary(content, 50)}");
            
            if (content.Contains("cumple") || content.Contains("cumpleaños") || content.Contains("nacimiento"))
                facts.Add($"Cumpleaños: {ExtractSummary(content, 50)}");
            
            if (content.Contains("me gusta") || content.Contains("prefiero") || content.Contains("favorito"))
                facts.Add($"Preferencia: {ExtractSummary(content, 50)}");
            
            // 2. DECISIONES Y PLANES
            if (content.Contains("decidí") || content.Contains("decidimos") || content.Contains("voy a"))
                facts.Add($"Decisión: {ExtractSummary(content, 100)}");
            
            if (content.Contains("plan") || content.Contains("planes") || content.Contains("intención"))
                facts.Add($"Plan: {ExtractSummary(content, 100)}");
            
            // 3. HECHOS Y HALLAZGOS
            if (content.Contains("encontré") || content.Contains("encontré") || content.Contains("descubrí"))
                facts.Add($"Hallazgo: {ExtractSummary(content, 100)}");
            
            if (content.Contains("sabes que") || content.Contains("te cuento que"))
                facts.Add($"Información: {ExtractSummary(content, 100)}");
            
            // 4. PROBLEMAS Y ERRORES
            if (content.Contains("error") || content.Contains("falló") || content.Contains("problema"))
                facts.Add($"Problema: {ExtractSummary(content, 100)}");
            
            // 5. CONTEXTO GENERAL (capturar todo lo demás importante)
            if (content.Length > 20 && !facts.Any(f => f.Contains(ExtractSummary(content, 30))))
            {
                var words = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length > 3) // Solo mensajes sustanciales
                {
                    facts.Add($"Contexto: {ExtractSummary(content, 80)}");
                }
            }
        }
        
        return facts.Distinct().Take(_maxWhiteboardItems).ToList();
    }

    private string ExtractSummary(string content, int maxLength)
    {
        if (content.Length <= maxLength)
            return content;
        
        return content.Substring(0, maxLength) + "...";
    }
}
