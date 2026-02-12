namespace LeaseGate.Policy;

public sealed class PolicySnapshot
{
    public LeaseGatePolicy Policy { get; init; } = new();
    public string RawText { get; init; } = "{}";
    public string PolicyHash { get; init; } = string.Empty;
}
