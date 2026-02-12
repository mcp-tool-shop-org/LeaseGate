namespace LeaseGate.Protocol;

public enum ActionType
{
    ChatCompletion,
    Embedding,
    ToolCall,
    WorkflowStep
}

public enum LeaseOutcome
{
    Success,
    ProviderRateLimit,
    Timeout,
    PolicyDenied,
    ToolError,
    UnknownError
}

public enum ReleaseClassification
{
    Recorded,
    LeaseNotFound,
    LeaseExpired
}
