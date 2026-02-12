using LeaseGate.Protocol;

namespace LeaseGate.Audit;

public sealed class JsonlAuditWriter : IAuditWriter
{
    private readonly string _directory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonlAuditWriter(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    public async Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        try
        {
            await _gate.WaitAsync(cancellationToken);
            var filePath = Path.Combine(_directory, $"leasegate-audit-{DateTime.UtcNow:yyyy-MM-dd}.jsonl");
            var line = ProtocolJson.Serialize(auditEvent);
            await File.AppendAllTextAsync(filePath, line + Environment.NewLine, cancellationToken);
        }
        catch
        {
        }
        finally
        {
            if (_gate.CurrentCount == 0)
            {
                _gate.Release();
            }
        }
    }
}
