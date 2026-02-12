using LeaseGate.Audit;
using LeaseGate.Protocol;

if (args.Length == 0)
{
    Console.WriteLine("Usage: dotnet run --project samples/LeaseGate.AuditVerifier -- <audit.jsonl>");
    return 1;
}

var filePath = args[0];
if (!File.Exists(filePath))
{
    Console.WriteLine($"File not found: {filePath}");
    return 2;
}

var prevHash = new string('0', 64);
var lineNumber = 0;
foreach (var line in File.ReadLines(filePath))
{
    lineNumber++;
    if (string.IsNullOrWhiteSpace(line))
    {
        continue;
    }

    var evt = ProtocolJson.Deserialize<AuditEvent>(line);
    if (!string.Equals(evt.PrevHash, prevHash, StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine($"INVALID at line {lineNumber}: prevHash mismatch");
        return 10;
    }

    var expected = AuditHashChain.ComputeEntryHash(evt);
    if (!string.Equals(expected, evt.EntryHash, StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine($"INVALID at line {lineNumber}: entryHash mismatch");
        return 11;
    }

    prevHash = evt.EntryHash;
}

Console.WriteLine($"OK: verified {lineNumber} lines");
return 0;
