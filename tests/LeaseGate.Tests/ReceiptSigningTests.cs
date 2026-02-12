using System.Security.Cryptography;
using LeaseGate.Audit;
using LeaseGate.Protocol;
using LeaseGate.Receipt;

namespace LeaseGate.Tests;

public class ReceiptSigningTests
{
    private string SetupAuditFile()
    {
        var auditDir = Path.Combine(Path.GetTempPath(), $"receipt-sign-{Guid.NewGuid():N}");
        Directory.CreateDirectory(auditDir);
        var writer = new JsonlAuditWriter(auditDir);

        writer.WriteAsync(new AuditEvent
        {
            EventType = "lease_acquired",
            PolicyHash = "abc123",
            LeaseId = "lease-1",
            ActorId = "actor-a",
            WorkspaceId = "ws-a",
            ModelId = "gpt-4o-mini",
            Decision = "granted"
        }, CancellationToken.None).Wait();

        writer.WriteAsync(new AuditEvent
        {
            EventType = "lease_released",
            PolicyHash = "abc123",
            LeaseId = "lease-1",
            ActorId = "actor-a",
            WorkspaceId = "ws-a",
            ModelId = "gpt-4o-mini",
            Decision = "released"
        }, CancellationToken.None).Wait();

        return Path.Combine(auditDir, $"leasegate-audit-{DateTime.UtcNow:yyyy-MM-dd}.jsonl");
    }

    [Fact]
    public void ExportProof_WithExternalKey_ProducesVerifiableBundle()
    {
        var auditFile = SetupAuditFile();
        var auditDir = Path.GetDirectoryName(auditFile)!;

        try
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var service = new GovernanceReceiptService();
            var bundle = service.ExportProof(auditFile, 1, 2, "policy-hash-1", "sig-info-1", ecdsa);

            Assert.NotEmpty(bundle.SignatureBase64);
            Assert.NotEmpty(bundle.PublicKeyBase64);
            Assert.Equal(2, bundle.Anchors.Count);

            // Save and verify
            var receiptPath = Path.Combine(Path.GetTempPath(), $"receipt-ext-{Guid.NewGuid():N}.json");
            service.SaveBundle(bundle, receiptPath);

            var verification = service.VerifyReceipt(receiptPath, auditFile);
            Assert.True(verification.Valid, verification.Message);

            File.Delete(receiptPath);
        }
        finally
        {
            Directory.Delete(auditDir, true);
        }
    }

    [Fact]
    public void ExportProof_WithExternalKey_MatchesPublicKey()
    {
        var auditFile = SetupAuditFile();
        var auditDir = Path.GetDirectoryName(auditFile)!;

        try
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var expectedPublicKey = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());

            var service = new GovernanceReceiptService();
            var bundle = service.ExportProof(auditFile, 1, 2, "ph", "si", ecdsa);

            Assert.Equal(expectedPublicKey, bundle.PublicKeyBase64);
        }
        finally
        {
            Directory.Delete(auditDir, true);
        }
    }

    [Fact]
    public void ExportProof_EphemeralKey_DiffersPerExport()
    {
        var auditFile = SetupAuditFile();
        var auditDir = Path.GetDirectoryName(auditFile)!;

        try
        {
            var service = new GovernanceReceiptService();
            var bundle1 = service.ExportProof(auditFile, 1, 2, "ph", "si");
            var bundle2 = service.ExportProof(auditFile, 1, 2, "ph", "si");

            // Ephemeral keys should differ
            Assert.NotEqual(bundle1.PublicKeyBase64, bundle2.PublicKeyBase64);
            Assert.NotEqual(bundle1.SignatureBase64, bundle2.SignatureBase64);
        }
        finally
        {
            Directory.Delete(auditDir, true);
        }
    }
}
