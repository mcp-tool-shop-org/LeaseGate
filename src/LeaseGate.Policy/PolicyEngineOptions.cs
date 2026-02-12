namespace LeaseGate.Policy;

public sealed class PolicyEngineOptions
{
    public bool RequireSignedBundles { get; set; }
    public List<string> AllowedPublicKeysBase64 { get; set; } = new();
}
