using LeaseGate.Protocol;

namespace LeaseGate.Providers;

public sealed class ModelCallSpec
{
    public string ProviderId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public int PromptTokens { get; set; }
    public int MaxOutputTokens { get; set; }
    public double Temperature { get; set; } = 0.2;
    public IReadOnlyList<string> RequestedCapabilities { get; set; } = Array.Empty<string>();
}

public sealed class ModelCallResult
{
    public bool Success { get; set; }
    public string OutputText { get; set; } = string.Empty;
    public int ActualPromptTokens { get; set; }
    public int ActualOutputTokens { get; set; }
    public int ActualCostCents { get; set; }
    public long LatencyMs { get; set; }
    public string FinishReason { get; set; } = "stop";
    public ProviderErrorClassification ErrorClassification { get; set; } = ProviderErrorClassification.None;
    public LeaseOutcome Outcome { get; set; } = LeaseOutcome.Success;
}
