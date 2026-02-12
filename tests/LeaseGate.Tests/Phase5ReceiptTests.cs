using LeaseGate.Audit;
using LeaseGate.Protocol;
using LeaseGate.Receipt;

namespace LeaseGate.Tests;

public class Phase5ReceiptTests
{
    [Fact]
    public async Task ExportAndVerifyReceipt_SucceedsAgainstAuditAnchors()
    {
        var auditDir = Path.Combine(Path.GetTempPath(), $"leasegate-receipt-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(auditDir);
        var writer = new JsonlAuditWriter(auditDir);

        await writer.WriteAsync(new AuditEvent
        {
            EventType = "lease_acquired",
            PolicyHash = "policy-hash-1",
            LeaseId = "lease-1",
            ActorId = "alice",
            WorkspaceId = "ws-alpha",
            ModelId = "gpt-4o-mini",
            RequestedTools = new List<string> { "net.fetch:NetworkRead" },
            Decision = "granted"
        }, CancellationToken.None);

        await writer.WriteAsync(new AuditEvent
        {
            EventType = "approval_reviewed",
            PolicyHash = "policy-hash-1",
            LeaseId = "lease-1",
            ActorId = "reviewer-1",
            WorkspaceId = "ws-alpha",
            ModelId = "gpt-4o-mini",
            Decision = "Granted"
        }, CancellationToken.None);

        await writer.WriteAsync(new AuditEvent
        {
            EventType = "lease_released",
            PolicyHash = "policy-hash-1",
            LeaseId = "lease-1",
            ActorId = "alice",
            WorkspaceId = "ws-alpha",
            ModelId = "gpt-4o-mini",
            RequestedTools = new List<string> { "net.fetch:NetworkRead" },
            Decision = "Success"
        }, CancellationToken.None);

        var auditFile = Directory.GetFiles(auditDir, "*.jsonl").Single();
        var service = new GovernanceReceiptService();
        var bundle = service.ExportProof(auditFile, 1, 3, "bundle-hash-1", "sig-info-1");

        var receiptPath = Path.Combine(auditDir, "receipt.json");
        service.SaveBundle(bundle, receiptPath);

        var verify = service.VerifyReceipt(receiptPath, auditFile);
        Assert.True(verify.Valid);
    }

    [Fact]
    public async Task TamperedReceipt_FailsVerification()
    {
        var auditDir = Path.Combine(Path.GetTempPath(), $"leasegate-receipt-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(auditDir);
        var writer = new JsonlAuditWriter(auditDir);

        await writer.WriteAsync(new AuditEvent
        {
            EventType = "lease_acquired",
            PolicyHash = "policy-hash-1",
            LeaseId = "lease-1",
            ActorId = "alice",
            WorkspaceId = "ws-alpha",
            ModelId = "gpt-4o-mini",
            Decision = "granted"
        }, CancellationToken.None);

        var auditFile = Directory.GetFiles(auditDir, "*.jsonl").Single();
        var service = new GovernanceReceiptService();
        var bundle = service.ExportProof(auditFile, 1, 1, "bundle-hash-1", "sig-info-1");
        bundle.PolicyBundleHash = "tampered";

        var tamperedPath = Path.Combine(auditDir, "receipt-tampered.json");
        service.SaveBundle(bundle, tamperedPath);

        var verify = service.VerifyReceipt(tamperedPath, auditFile);
        Assert.False(verify.Valid);
    }
}
