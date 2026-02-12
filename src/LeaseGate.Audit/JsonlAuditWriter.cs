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
        var acquired = false;
        try
        {
            await _gate.WaitAsync(cancellationToken);
            acquired = true;
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
            if (acquired)
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

            string? lastLine = null;
            long count = 0;
            using var reader = new StreamReader(path);
            while (reader.ReadLine() is { } line)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    lastLine = line;
                }
                count++;
            }

            _lineNumber = count;
            if (lastLine is null)
            {
                return;
            }

            var last = ProtocolJson.Deserialize<AuditEvent>(lastLine);
            _lastHash = string.IsNullOrWhiteSpace(last.EntryHash) ? _lastHash : last.EntryHash;
        }
        catch
        {
        }
    }
}
