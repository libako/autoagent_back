using Microsoft.SemanticKernel;
using AutoAgentes.Domain.Entities;

namespace AutoAgentes.App;

public interface IPlanner
{
    Task<(string Goal, IReadOnlyList<KernelFunction> Steps)> CreatePlanAsync(Kernel kernel, string userMessage, Guid agentId, CancellationToken ct);
}


