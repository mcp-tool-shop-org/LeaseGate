using System.Security.Cryptography;
using System.Text;

namespace LeaseGate.Audit;

public static class AuditHashChain
{
    public static string ComputeEntryHash(AuditEvent auditEvent)
    {
        var canonical = string.Join('|',
            auditEvent.TimestampUtc.ToString("O"),
            auditEvent.EventType,
            auditEvent.LeaseId,
            auditEvent.Decision,
            auditEvent.PolicyHash,
            auditEvent.ActorId,
            auditEvent.WorkspaceId,
            auditEvent.ModelId,
            auditEvent.EstimatedCostCents,
            auditEvent.ActualCostCents,
            string.Join(',', auditEvent.RequestedTools),
            string.Join(',', auditEvent.ToolUsageSummary),
            auditEvent.PrevHash);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
