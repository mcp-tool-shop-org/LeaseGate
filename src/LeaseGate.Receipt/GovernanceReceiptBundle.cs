namespace LeaseGate.Receipt;

public sealed class GovernanceReceiptBundle
{
    public string Version { get; set; } = "v1";
    public DateTimeOffset GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string RequestSummary { get; set; } = string.Empty;
    public string PolicyBundleHash { get; set; } = string.Empty;
    public string PolicySignatureInfo { get; set; } = string.Empty;
    public string AuditFirstPrevHash { get; set; } = string.Empty;
    public string AuditLastEntryHash { get; set; } = string.Empty;
    public List<string> LeaseReceiptHashes { get; set; } = new();
    public List<string> ApprovalReviewers { get; set; } = new();
    public Dictionary<string, int> ModelUsageTotals { get; set; } = new();
    public Dictionary<string, int> ToolUsageTotals { get; set; } = new();
    public List<ReceiptAuditAnchor> Anchors { get; set; } = new();
    public string PublicKeyBase64 { get; set; } = string.Empty;
    public string SignatureBase64 { get; set; } = string.Empty;
}

public sealed class ReceiptAuditAnchor
{
    public int LineNumber { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string PolicyHash { get; set; } = string.Empty;
    public string PrevHash { get; set; } = string.Empty;
    public string EntryHash { get; set; } = string.Empty;
    public string LeaseId { get; set; } = string.Empty;
    public List<string> RequestedTools { get; set; } = new();
}

public sealed class GovernanceReceiptVerificationResult
{
    public bool Valid { get; set; }
    public string Message { get; set; } = string.Empty;
}
