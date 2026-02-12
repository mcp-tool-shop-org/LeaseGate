namespace LeaseGate.Client;

public enum FallbackMode
{
    Dev,
    Prod
}

public sealed class LeaseGateClientOptions
{
    public string PipeName { get; set; } = "leasegate-governor";
    public FallbackMode FallbackMode { get; set; } = FallbackMode.Dev;
    public int DevMaxOutputTokens { get; set; } = 256;
    public int ProdReadOnlyMaxOutputTokens { get; set; } = 128;
    public HashSet<string> RiskyCapabilities { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "network_write",
        "file_write",
        "exec"
    };
}
