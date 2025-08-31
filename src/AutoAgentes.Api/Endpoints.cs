using AutoAgentes.Contracts;
using AutoAgentes.App;
using AutoAgentes.Infrastructure.Services;
using AutoAgentes.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoAgentes.Api;

public static class Endpoints
{
    public static void MapApi(this IEndpointRouteBuilder app)
    {
        var mcp = app.MapGroup("/mcp");
        mcp.MapPost("/servers", async ([FromBody] CreateMcpServerRequest req, IMcpServerRegistry registry) =>
        {
            var server = await registry.CreateServerAsync(req);
            return Results.Created($"/mcp/servers/{server.Id}", server);
        });
        mcp.MapGet("/servers", async (IMcpServerRegistry registry) =>
        {
            var servers = await registry.GetServersAsync();
            return Results.Ok(servers);
        });
        mcp.MapPost("/servers/{id:guid}/discover", async (Guid id, IMcpServerRegistry registry) =>
        {
            var tools = await registry.DiscoverToolsAsync(id);
            return Results.Ok(tools);
        });
        
        mcp.MapPost("/servers/{id:guid}/refresh", async (Guid id, IMcpServerRegistry registry) =>
        {
            try
            {
                var tools = await registry.DiscoverToolsAsync(id);
                return Results.Ok(new { message = "Herramientas refrescadas exitosamente", tools });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error refrescando herramientas: {ex.Message}");
            }
        });

        mcp.MapGet("/tools", async ([FromQuery] Guid? serverId, IMcpServerRegistry registry) =>
        {
            if (!serverId.HasValue)
                return Results.BadRequest("serverId is required");

            var tools = await registry.DiscoverToolsAsync(serverId.Value);
            return Results.Ok(tools.Tools);
        });
        
        mcp.MapGet("/tools/all", async (AppDbContext context) =>
        {
            var allTools = await context.Tools
                .Include(t => t.McpServer)
                .Select(t => new
                {
                    t.Id,
                    t.Name,
                    t.Description,
                    t.Scope,
                    t.InputSchemaJson,
                    ServerId = t.McpServerId,
                    ServerName = t.McpServer!.Name
                })
                .ToListAsync();
            
            return Results.Ok(allTools);
        });

        var agents = app.MapGroup("/agents");
        agents.MapPost("/", async ([FromBody] CreateAgentRequest req, IAgentsService service) =>
        {
            var agent = await service.CreateAgentAsync(req);
            return Results.Created($"/agents/{agent.Id}", agent);
        });
        agents.MapGet("/", async (IAgentsService service) =>
        {
            var agents = await service.GetAgentsAsync();
            return Results.Ok(agents);
        });
        agents.MapGet("/{id:guid}", async (Guid id, IAgentsService service) =>
        {
            var agent = await service.GetAgentAsync(id);
            if (agent == null) return Results.NotFound();
            return Results.Ok(agent);
        });
        agents.MapPatch("/{id:guid}", async (Guid id, [FromBody] UpdateAgentRequest req, IAgentsService service) =>
        {
            var agent = await service.UpdateAgentAsync(id, req);
            return Results.Ok(agent);
        });
        agents.MapPost("/{id:guid}/bindings", async (Guid id, [FromBody] CreateBindingRequest req, IAgentsService service) =>
        {
            var binding = await service.CreateBindingAsync(id, req);
            return Results.Created($"/agents/{id}/bindings/{binding.Id}", binding);
        });
        agents.MapGet("/{id:guid}/bindings", async (Guid id, IAgentsService service) =>
        {
            var bindings = await service.GetBindingsAsync(id);
            return Results.Ok(bindings);
        });
        agents.MapDelete("/{id:guid}/bindings/{bindingId:guid}", async (Guid id, Guid bindingId, IAgentsService service) =>
        {
            await service.DeleteBindingAsync(id, bindingId);
            return Results.NoContent();
        });

        var sessions = app.MapGroup("/sessions");
        sessions.MapPost("/", async ([FromBody] CreateSessionRequest req, ISessionsService service) =>
        {
            var session = await service.CreateSessionAsync(req);
            return Results.Created($"/sessions/{session.Id}", session);
        });
        sessions.MapPost("/{id:guid}/messages", async (Guid id, [FromBody] PostMessageRequest req, ISessionsService service) =>
        {
            var response = await service.PostMessageAsync(id, req);
            return Results.Accepted($"/sessions/{id}/messages", response);
        });
        sessions.MapGet("/{id:guid}", async (Guid id, ISessionsService service) =>
        {
            var session = await service.GetSessionAsync(id);
            if (session == null) return Results.NotFound();
            return Results.Ok(session);
        });
        sessions.MapGet("/{id:guid}/trace", async (Guid id, ISessionsService service, [FromQuery] int afterIdx = 0, [FromQuery] int limit = 100) =>
        {
            var trace = await service.GetTraceAsync(id, afterIdx, limit);
            return Results.Ok(trace);
        });
        sessions.MapPost("/{id:guid}:pause", async (Guid id, ISessionsService service) =>
        {
            await service.PauseSessionAsync(id);
            return Results.NoContent();
        });
        sessions.MapPost("/{id:guid}:resume", async (Guid id, ISessionsService service) =>
        {
            await service.ResumeSessionAsync(id);
            return Results.NoContent();
        });
        sessions.MapPost("/{id:guid}:cancel", async (Guid id, ISessionsService service) =>
        {
            await service.CancelSessionAsync(id);
            return Results.NoContent();
        });
        sessions.MapPost("/{id:guid}:retryLastStep", async (Guid id, ISessionsService service) =>
        {
            await service.RetryLastStepAsync(id);
            return Results.NoContent();
        });
    }
}


