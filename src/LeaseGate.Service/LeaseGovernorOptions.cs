namespace LeaseGate.Service;

public sealed class LeaseGovernorOptions
{
    public string PipeName { get; set; } = "leasegate-governor";
    public TimeSpan LeaseTtl { get; set; } = TimeSpan.FromSeconds(20);
    public int MaxInFlight { get; set; } = 4;
    public int DailyBudgetCents { get; set; } = 500;
}
