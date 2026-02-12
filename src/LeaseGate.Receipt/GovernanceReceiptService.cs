using System.Security.Cryptography;
using System.Text;
using LeaseGate.Audit;
using LeaseGate.Protocol;

namespace LeaseGate.Receipt;

public sealed class GovernanceReceiptService
{
    public GovernanceReceiptBundle ExportProof(string auditFilePath, int fromLine, int toLine, string policyBundleHash, string policySignatureInfo)
    {
        var events = LoadEvents(auditFilePath, fromLine, toLine);
        if (events.Count == 0)
        {
            throw new InvalidOperationException("No audit events in selected range.");
        }

        var bundle = new GovernanceReceiptBundle
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            RequestSummary = BuildRequestSummary(events),
            PolicyBundleHash = policyBundleHash,
            PolicySignatureInfo = policySignatureInfo,
            AuditFirstPrevHash = events.First().Event.PrevHash,
            AuditLastEntryHash = events.Last().Event.EntryHash,
            LeaseReceiptHashes = events.Where(e => e.Event.EventType == "lease_released")
                .Select(e => e.Event.EntryHash)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            ApprovalReviewers = events
                .Where(e => e.Event.EventType == "approval_reviewed")
                .Select(e => e.Event.ActorId)
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            ModelUsageTotals = events
                .Where(e => !string.IsNullOrWhiteSpace(e.Event.ModelId))
                .GroupBy(e => e.Event.ModelId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase),
            ToolUsageTotals = events
                .SelectMany(e => e.Event.RequestedTools)
                .Select(tool => tool.Split(':')[0])
                .Where(tool => !string.IsNullOrWhiteSpace(tool))
                .GroupBy(tool => tool, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase),
            Anchors = events.Select(e => new ReceiptAuditAnchor
            {
                LineNumber = e.Line,
                EventType = e.Event.EventType,
                PolicyHash = e.Event.PolicyHash,
                PrevHash = e.Event.PrevHash,
                EntryHash = e.Event.EntryHash,
                LeaseId = e.Event.LeaseId,
                RequestedTools = e.Event.RequestedTools.ToList()
            }).ToList()
        };

        Sign(bundle);
        return bundle;
    }

    public void SaveBundle(GovernanceReceiptBundle bundle, string outputPath)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(outputPath, ProtocolJson.Serialize(bundle), Encoding.UTF8);
    }

    public GovernanceReceiptVerificationResult VerifyReceipt(string receiptPath, string auditFilePath)
    {
        var bundle = ProtocolJson.Deserialize<GovernanceReceiptBundle>(File.ReadAllText(receiptPath));

        if (!VerifySignature(bundle))
        {
            return new GovernanceReceiptVerificationResult { Valid = false, Message = "invalid receipt signature" };
        }

        var events = LoadEvents(auditFilePath, bundle.Anchors.Min(a => a.LineNumber), bundle.Anchors.Max(a => a.LineNumber));
        var anchorMap = bundle.Anchors.ToDictionary(a => a.LineNumber);

        foreach (var entry in events)
        {
            if (!anchorMap.TryGetValue(entry.Line, out var anchor))
            {
                continue;
            }

            if (!string.Equals(anchor.EntryHash, entry.Event.EntryHash, StringComparison.Ordinal) ||
                !string.Equals(anchor.PrevHash, entry.Event.PrevHash, StringComparison.Ordinal))
            {
                return new GovernanceReceiptVerificationResult { Valid = false, Message = $"audit anchor mismatch at line {entry.Line}" };
            }
        }

        if (!string.Equals(bundle.AuditLastEntryHash, bundle.Anchors.Last().EntryHash, StringComparison.Ordinal))
        {
            return new GovernanceReceiptVerificationResult { Valid = false, Message = "last audit anchor hash mismatch" };
        }

        return new GovernanceReceiptVerificationResult { Valid = true, Message = "receipt verified" };
    }

    private static List<(int Line, AuditEvent Event)> LoadEvents(string auditFilePath, int fromLine, int toLine)
    {
        var allLines = File.ReadAllLines(auditFilePath);
        var start = Math.Max(1, fromLine);
        var end = Math.Min(toLine, allLines.Length);
        var output = new List<(int Line, AuditEvent Event)>();
        for (var line = start; line <= end; line++)
        {
            var raw = allLines[line - 1];
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            output.Add((line, ProtocolJson.Deserialize<AuditEvent>(raw)));
        }

        return output;
    }

    private static string BuildRequestSummary(List<(int Line, AuditEvent Event)> events)
    {
        var actor = events.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.Event.ActorId)).Event.ActorId;
        var workspace = events.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.Event.WorkspaceId)).Event.WorkspaceId;
        var eventCount = events.Count;
        return $"actor={actor};workspace={workspace};events={eventCount}";
    }

    private static void Sign(GovernanceReceiptBundle bundle)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        bundle.PublicKeyBase64 = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());
        var payload = ReceiptPayload(bundle);
        var signature = ecdsa.SignData(payload, HashAlgorithmName.SHA256);
        bundle.SignatureBase64 = Convert.ToBase64String(signature);
    }

    private static bool VerifySignature(GovernanceReceiptBundle bundle)
    {
        try
        {
            var publicKey = Convert.FromBase64String(bundle.PublicKeyBase64);
            var signature = Convert.FromBase64String(bundle.SignatureBase64);
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(publicKey, out _);
            return ecdsa.VerifyData(ReceiptPayload(bundle), signature, HashAlgorithmName.SHA256);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] ReceiptPayload(GovernanceReceiptBundle bundle)
    {
        var payload = new
        {
            bundle.Version,
            bundle.GeneratedAtUtc,
            bundle.RequestSummary,
            bundle.PolicyBundleHash,
            bundle.PolicySignatureInfo,
            bundle.AuditFirstPrevHash,
            bundle.AuditLastEntryHash,
            bundle.LeaseReceiptHashes,
            bundle.ApprovalReviewers,
            bundle.ModelUsageTotals,
            bundle.ToolUsageTotals,
            bundle.Anchors
        };

        return Encoding.UTF8.GetBytes(ProtocolJson.Serialize(payload));
    }
}
