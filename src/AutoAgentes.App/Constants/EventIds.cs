namespace AutoAgentes.App.Constants;

/// <summary>
/// EventIds estructurados para logging consistente
/// </summary>
public static class EventIds
{
    // Orchestrator Events (1000-1099)
    public static readonly EventId OrchestratorStarted = new(1000, nameof(OrchestratorStarted));
    public static readonly EventId OrchestratorCompleted = new(1001, nameof(OrchestratorCompleted));
    public static readonly EventId OrchestratorError = new(1002, nameof(OrchestratorError));
    
    // Planning Events (1100-1199)
    public static readonly EventId PlanningStarted = new(1100, nameof(PlanningStarted));
    public static readonly EventId PlanCreated = new(1101, nameof(PlanCreated));
    public static readonly EventId PlanRepairAttempt = new(1102, nameof(PlanRepairAttempt));
    public static readonly EventId PlanRepairResult = new(1103, nameof(PlanRepairResult));
    public static readonly EventId PlanParseFailed = new(1104, nameof(PlanParseFailed));
    
    // Tool Execution Events (1200-1299)
    public static readonly EventId ToolCall = new(1200, nameof(ToolCall));
    public static readonly EventId ToolRun = new(1201, nameof(ToolRun));
    public static readonly EventId ToolSuccess = new(1202, nameof(ToolSuccess));
    public static readonly EventId ToolError = new(1203, nameof(ToolError));
    public static readonly EventId ToolTimeout = new(1204, nameof(ToolTimeout));
    
    // Kernel Events (1300-1399)
    public static readonly EventId KernelCreated = new(1300, nameof(KernelCreated));
    public static readonly EventId KernelFactoryStarted = new(1301, nameof(KernelFactoryStarted));
    public static readonly EventId KernelFactoryCompleted = new(1302, nameof(KernelFactoryCompleted));
    public static readonly EventId KernelFactoryError = new(1303, nameof(KernelFactoryError));
    public static readonly EventId KernelGovernanceApplied = new(1304, nameof(KernelGovernanceApplied));
    
    // MCP Events (1400-1499)
    public static readonly EventId McpToolExecutionStarted = new(1400, nameof(McpToolExecutionStarted));
    public static readonly EventId McpToolExecution = new(1401, nameof(McpToolExecution));
    public static readonly EventId McpToolSuccess = new(1402, nameof(McpToolSuccess));
    public static readonly EventId McpToolError = new(1403, nameof(McpToolError));
    public static readonly EventId McpToolNotFound = new(1404, nameof(McpToolNotFound));
    
    // Tools Registration Events (1500-1599)
    public static readonly EventId ToolsRegistrationStarted = new(1500, nameof(ToolsRegistrationStarted));
    public static readonly EventId ToolsFound = new(1501, nameof(ToolsFound));
    public static readonly EventId ToolAlreadyRegistered = new(1502, nameof(ToolAlreadyRegistered));
    public static readonly EventId ToolsRegistrationCompleted = new(1503, nameof(ToolsRegistrationCompleted));
    
    // Summary Events (1600-1699)
    public static readonly EventId SummaryStarted = new(1600, nameof(SummaryStarted));
    public static readonly EventId SummarySettingsApplied = new(1601, nameof(SummarySettingsApplied));
    public static readonly EventId SummaryCompleted = new(1602, nameof(SummaryCompleted));
    
    // Error Events (1700-1799)
    public static readonly EventId PlannerError = new(1700, nameof(PlannerError));
    public static readonly EventId KernelError = new(1701, nameof(KernelError));
    public static readonly EventId ConfigurationError = new(1702, nameof(ConfigurationError));
}

