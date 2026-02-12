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

public enum ToolCategory
{
    FileRead,
    FileWrite,
    NetworkRead,
    NetworkWrite,
    Exec,
    Other
}

public enum ProviderErrorClassification
{
    None,
    RateLimited,
    Timeout,
    ContextTooLarge,
    ModelUnavailable,
    Unauthorized,
    Unknown
}

public enum ApprovalDecisionStatus
{
    Pending,
    Granted,
    Denied,
    Expired
}

public enum PrincipalType
{
    Human,
    Service
}

public enum Role
{
    Owner,
    Admin,
    Member,
    Viewer,
    ServiceAccount
}
