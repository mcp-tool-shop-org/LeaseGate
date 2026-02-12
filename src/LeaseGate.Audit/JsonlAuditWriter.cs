using LeaseGate.Protocol;

namespace LeaseGate.Audit;

public sealed class JsonlAuditWriter : IAuditWriter
{
    private readonly string _directory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string _lastHash = new('0', 64);
    private long _lineNumber;

    public JsonlAuditWriter(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(_directory);
        LoadTailState();
    }

    public async Task<AuditWriteResult> WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        try
        {
            await _gate.WaitAsync(cancellationToken);
            var filePath = Path.Combine(_directory, $"leasegate-audit-{DateTime.UtcNow:yyyy-MM-dd}.jsonl");
            auditEvent.PrevHash = _lastHash;
            auditEvent.EntryHash = AuditHashChain.ComputeEntryHash(auditEvent);
            var line = ProtocolJson.Serialize(auditEvent);
            await File.AppendAllTextAsync(filePath, line + Environment.NewLine, cancellationToken);

            _lastHash = auditEvent.EntryHash;
            _lineNumber++;
            return new AuditWriteResult
            {
                EntryHash = auditEvent.EntryHash,
                PrevHash = auditEvent.PrevHash,
                LineNumber = _lineNumber
            };
        }
        catch
        {
            return new AuditWriteResult
            {
                EntryHash = string.Empty,
                PrevHash = _lastHash,
                LineNumber = _lineNumber
            };
        }
        finally
        {
            if (_gate.CurrentCount == 0)
            {
                _gate.Release();
            }
        }
    }

    private void LoadTailState()
    {
        try
        {
            var path = Path.Combine(_directory, $"leasegate-audit-{DateTime.UtcNow:yyyy-MM-dd}.jsonl");
            if (!File.Exists(path))
            {
                return;
            }

            var lines = File.ReadAllLines(path);
            _lineNumber = lines.LongLength;
            if (_lineNumber == 0)
            {
                return;
            }

            var last = ProtocolJson.Deserialize<AuditEvent>(lines[^1]);
            _lastHash = string.IsNullOrWhiteSpace(last.EntryHash) ? _lastHash : last.EntryHash;
        }
        catch
        {
        }
    }
}
