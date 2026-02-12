namespace LeaseGate.Service;

public sealed class LeaseGovernorOptions
{
    public string PipeName { get; set; } = "leasegate-governor";
    public TimeSpan LeaseTtl { get; set; } = TimeSpan.FromSeconds(20);
    public int MaxInFlight { get; set; } = 4;
    public int DailyBudgetCents { get; set; } = 500;
    public int MaxRequestsPerMinute { get; set; } = 120;
    public int MaxTokensPerMinute { get; set; } = 250_000;
    public int MaxContextTokens { get; set; } = 16_000;
    public int MaxRetrievedChunks { get; set; } = 40;
    public int MaxToolOutputTokens { get; set; } = 4_000;
    public int MaxToolCallsPerLease { get; set; } = 6;
    public int MaxComputeUnits { get; set; } = 8;
    public TimeSpan RateWindow { get; set; } = TimeSpan.FromMinutes(1);
}
