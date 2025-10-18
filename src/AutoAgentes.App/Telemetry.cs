using System.Diagnostics.Metrics;
using System.Diagnostics;
using AutoAgentes.App.Constants;

namespace AutoAgentes.App;

public static class Telemetry
{
    public static readonly ActivitySource OrchestratorSource = new("AutoAgentes.Orchestrator");
    public static readonly ActivitySource McpCallerSource   = new("AutoAgentes.McpCaller");

    public static readonly Meter Meter = new("AutoAgentes.Metrics", "1.0.0");
    public static readonly Counter<long> ToolCallsTotal = Meter.CreateCounter<long>(MetricsNames.ToolCallsTotal);
    public static readonly Counter<long> TokensConsumed = Meter.CreateCounter<long>(MetricsNames.TokensConsumed);
    public static readonly Counter<long> FunctionInvokingTotal = Meter.CreateCounter<long>(MetricsNames.FunctionInvokingTotal);
    public static readonly Counter<long> FunctionInvokedTotal = Meter.CreateCounter<long>(MetricsNames.FunctionInvokedTotal);
    public static readonly Counter<long> ToolTimeoutsTotal = Meter.CreateCounter<long>(MetricsNames.ToolTimeoutsTotal);
    public static readonly Counter<long> ToolErrorsTotal = Meter.CreateCounter<long>(MetricsNames.ToolErrorsTotal);
}


