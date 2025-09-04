using Microsoft.SemanticKernel;
using AutoAgentes.Domain.Entities;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AutoAgentes.App;

public interface IPlanner
{
    Task<(string Goal, IReadOnlyList<PlanStep> Steps, int RegistryVersion)> CreatePlanAsync(Kernel kernel, ChatHistory conversationHistory, Agent agent, Guid sessionId, CancellationToken ct);
}


