namespace AutoAgentes.App.Constants;

/// <summary>
/// Nombres de eventos de traza estructurados
/// </summary>
public static class TraceEventNames
{
    // Orchestrator Events
    public const string OrchestratorStarted = "orchestrator_started";
    public const string OrchestratorCompleted = "orchestrator_completed";
    public const string OrchestratorError = "orchestrator_error";
    
    // Planning Events
    public const string PlanningStarted = "planning_started";
    public const string PlannerPrompt = "planner_prompt";
    public const string PlannerSettingsApplied = "planner_settings_applied";
    public const string PlannerResponse = "planner_response";
    public const string PlannerRepairAttempt = "planner_repair_attempt";
    public const string PlannerRepairResult = "planner_repair_result";
    public const string PlannerParseFailed = "planner_parse_failed";
    public const string PlannerError = "planner_error";
    public const string Plan = "plan";
    
    // Tool Execution Events
    public const string ToolCall = "tool_call";
    public const string ToolExecution = "tool_execution";
    public const string Observation = "observation";
    public const string ToolSuccess = "tool_success";
    public const string ToolError = "tool_error";
    
    // Kernel Events
    public const string KernelCreated = "kernel_created";
    public const string KernelFactoryStarted = "kernel_factory_started";
    public const string KernelFactoryCompleted = "kernel_factory_completed";
    public const string KernelFactoryError = "kernel_factory_error";
    public const string KernelFactoryMinimalCreated = "kernel_factory_minimal_created";
    public const string KernelCreationStarted = "kernel_creation_started";
    public const string KernelConfig = "kernel_config";
    public const string KernelBuilt = "kernel_built";
    public const string KernelGovernanceApplied = "kernel_governance_applied";
    public const string SpecializedKernelCreated = "specialized_kernel_created";
    
    // MCP Events
    public const string McpToolExecutionStarted = "mcp_tool_execution_started";
    public const string McpToolExecution = "mcp_tool_execution";
    public const string McpToolSuccess = "mcp_tool_success";
    public const string McpToolError = "mcp_tool_error";
    public const string McpToolNotFound = "mcp_tool_not_found";
    
    // Tools Registration Events
    public const string ToolsRegistrationStarted = "tools_registration_started";
    public const string ToolsFound = "tools_found";
    public const string ToolAlreadyRegistered = "tool_already_registered";
    public const string ToolsRegistrationCompleted = "tools_registration_completed";
    
    // Summary Events
    public const string SummaryStarted = "summary_started";
    public const string SummarySettingsApplied = "summary_settings_applied";
    public const string Summary = "summary";
    
    // Configuration Events
    public const string AnthropicConfigFailed = "anthropic_config_failed";
}

