namespace LeaseGate.Protocol;

public sealed class PipeCommandRequest
{
    public string ProtocolVersion { get; set; } = ProtocolVersionInfo.ProtocolVersion;
    public string Command { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
}

public sealed class PipeCommandResponse
{
    public string ProtocolVersion { get; set; } = ProtocolVersionInfo.ProtocolVersion;
    public bool Success { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
}
