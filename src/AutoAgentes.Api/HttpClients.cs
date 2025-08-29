using AutoAgentes.Contracts;
using AutoAgentes.Infrastructure;

namespace Microsoft.Extensions.DependencyInjection;

public static class HttpClientsRegistration
{
    public static IServiceCollection AddMcpHttpClients(this IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddHttpClient("mcp:default");
        services.AddScoped<IMcpCaller, McpCaller>();
        return services;
    }
}


