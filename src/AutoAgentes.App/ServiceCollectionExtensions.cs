using Microsoft.Extensions.DependencyInjection;
using AutoAgentes.Contracts;

namespace AutoAgentes.App;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddConversationContext(this IServiceCollection services)
    {
        // Registrar servicios de contexto conversacional
        services.AddScoped<IConversationStore, ConversationStore>();
        services.AddScoped<IContextBuilder, ContextBuilder>();
        
        // Registrar reducers de historial
        services.AddSingleton<IHistoryReducer, LastKMessagesReducer>();
        services.AddSingleton<IHistoryReducer, WhiteboardReducer>();
        
        // Por defecto usar LastKMessagesReducer
        services.AddSingleton<LastKMessagesReducer>();
        
        return services;
    }
}
