using System.Diagnostics.Metrics;
using System.Diagnostics;

namespace AutoAgentes.App;

public static class Telemetry
{
    public static readonly ActivitySource OrchestratorSource = new("AutoAgentes.Orchestrator");
    public static readonly ActivitySource McpCallerSource   = new("AutoAgentes.McpCaller");

    public static readonly Meter Meter = new("AutoAgentes.Metrics", "1.0.0");
    public static readonly Counter<long> ToolCallsTotal = Meter.CreateCounter<long>("tool_calls_total");
    public static readonly Counter<long> TokensConsumed = Meter.CreateCounter<long>("tokens_consumed");
}


