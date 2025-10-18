using AutoAgentes.Application.Abstractions;
using AutoAgentes.App.Configuration;
using AutoAgentes.Domain.Entities;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace AutoAgentes.Infrastructure.SemanticKernel;

/// <summary>
/// Implementación de IGovernancePolicy para políticas de gobernanza
/// </summary>
public class GovernancePolicy : IGovernancePolicy
{
    private readonly ILogger<GovernancePolicy> _logger;
    private readonly IOptionsSnapshot<GovernanceOptions> _governanceOptions;
    private readonly Regex[] _dangerousFunctionPatterns;

    public GovernancePolicy(
        ILogger<GovernancePolicy> logger,
        IOptionsSnapshot<GovernanceOptions> governanceOptions)
    {
        _logger = logger;
        _governanceOptions = governanceOptions;
        
        // Compilar patrones regex para mejor rendimiento
        _dangerousFunctionPatterns = governanceOptions.Value.DangerousFunctionPatterns
            .Select(pattern => new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase))
            .ToArray();
    }

    public bool IsDangerousFunction(string functionName, Agent agent)
    {
        if (string.IsNullOrEmpty(functionName))
            return false;

        var securityLevel = GetSecurityLevel(agent);
        
        // Solo aplicar filtros en niveles altos de seguridad
        if (!securityLevel.Equals("High", StringComparison.OrdinalIgnoreCase))
            return false;

        // Verificar contra patrones regex compilados
        return _dangerousFunctionPatterns.Any(pattern => pattern.IsMatch(functionName));
    }

    public string GetSecurityLevel(Agent agent)
    {
        // El nivel de seguridad puede venir del agente o de la configuración global
        return agent.Autonomy?.ToLowerInvariant() switch
        {
            "supervised" => "High",
            "autonomous" => "Medium",
            "unsupervised" => "Low",
            _ => _governanceOptions.Value.SecurityLevel
        };
    }

    public (int MaxTokens, double Temperature) GetTokenLimits(Agent agent)
    {
        return agent.Autonomy?.ToLowerInvariant() switch
        {
            "supervised" => (4000, 0.1),
            "autonomous" => (8000, 0.3),
            "unsupervised" => (12000, 0.5),
            _ => (4000, 0.2)
        };
    }
}
