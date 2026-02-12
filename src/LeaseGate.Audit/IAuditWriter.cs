namespace LeaseGate.Audit;

public sealed class AuditWriteResult
{
    public string EntryHash { get; set; } = string.Empty;
    public string PrevHash { get; set; } = string.Empty;
    public long LineNumber { get; set; }
}

public interface IAuditWriter
{
    Task<AuditWriteResult> WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken);
}
