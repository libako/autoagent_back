using AutoAgentes.Contracts;
using AutoAgentes.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AutoAgentes.Infrastructure.Services;

public interface IAgentsService
{
    Task<AgentResponse> CreateAgentAsync(CreateAgentRequest request);
    Task<IReadOnlyList<AgentResponse>> GetAgentsAsync();
    Task<AgentResponse?> GetAgentAsync(Guid id);
    Task<AgentResponse> UpdateAgentAsync(Guid id, UpdateAgentRequest request);
    Task<BindingResponse> CreateBindingAsync(Guid agentId, CreateBindingRequest request);
    Task<IReadOnlyList<BindingResponse>> GetBindingsAsync(Guid agentId);
    Task DeleteBindingAsync(Guid agentId, Guid bindingId);
}

public class AgentsService : IAgentsService
{
    private readonly AppDbContext _context;

    public AgentsService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<AgentResponse> CreateAgentAsync(CreateAgentRequest request)
    {
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Provider = "openai", // Default provider
            Autonomy = request.Autonomy,
            ParamsJson = request.SystemPrompt, // Store system prompt in params
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };

        _context.Agents.Add(agent);
        await _context.SaveChangesAsync();

        return new AgentResponse(agent.Id, agent.Name, agent.ParamsJson, agent.ParamsJson ?? "", agent.Autonomy, agent.CreatedUtc);
    }

    public async Task<IReadOnlyList<AgentResponse>> GetAgentsAsync()
    {
        var agents = await _context.Agents
            .OrderBy(a => a.CreatedUtc)
            .ToListAsync();

        return agents.Select(a => new AgentResponse(a.Id, a.Name, a.ParamsJson, a.ParamsJson ?? "", a.Autonomy, a.CreatedUtc)).ToList();
    }

    public async Task<AgentResponse?> GetAgentAsync(Guid id)
    {
        var agent = await _context.Agents.FindAsync(id);
        if (agent == null) return null;

        return new AgentResponse(agent.Id, agent.Name, agent.ParamsJson, agent.ParamsJson ?? "", agent.Autonomy, agent.CreatedUtc);
    }

    public async Task<AgentResponse> UpdateAgentAsync(Guid id, UpdateAgentRequest request)
    {
        var agent = await _context.Agents.FindAsync(id);
        if (agent == null)
            throw new ArgumentException($"Agent {id} not found");

        if (request.Name != null) agent.Name = request.Name;
        if (request.SystemPrompt != null) agent.ParamsJson = request.SystemPrompt;
        if (request.Autonomy != null) agent.Autonomy = request.Autonomy;
        agent.UpdatedUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return new AgentResponse(agent.Id, agent.Name, agent.ParamsJson, agent.ParamsJson ?? "", agent.Autonomy, agent.CreatedUtc);
    }

    public async Task<BindingResponse> CreateBindingAsync(Guid agentId, CreateBindingRequest request)
    {
        var agent = await _context.Agents.FindAsync(agentId);
        if (agent == null)
            throw new ArgumentException($"Agent {agentId} not found");

        var binding = new AgentToolBinding
        {
            Id = Guid.NewGuid(),
            AgentId = agentId,
            ToolId = request.ToolId,
            ConfigJson = request.Config?.ToString(),
            Enabled = request.Enabled
        };

        _context.AgentToolBindings.Add(binding);
        await _context.SaveChangesAsync();

        return new BindingResponse(binding.Id, binding.AgentId, binding.ToolId, binding.Enabled);
    }

    public async Task<IReadOnlyList<BindingResponse>> GetBindingsAsync(Guid agentId)
    {
        var bindings = await _context.AgentToolBindings
            .Where(b => b.AgentId == agentId)
            .ToListAsync();

        return bindings.Select(b => new BindingResponse(b.Id, b.AgentId, b.ToolId, b.Enabled)).ToList();
    }

    public async Task DeleteBindingAsync(Guid agentId, Guid bindingId)
    {
        var binding = await _context.AgentToolBindings
            .FirstOrDefaultAsync(b => b.Id == bindingId && b.AgentId == agentId);

        if (binding == null)
            throw new ArgumentException($"Binding {bindingId} not found for agent {agentId}");

        _context.AgentToolBindings.Remove(binding);
        await _context.SaveChangesAsync();
    }
}
