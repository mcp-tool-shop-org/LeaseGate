using LeaseGate.Protocol;

namespace LeaseGate.Providers;

public sealed class DeterministicFakeProviderAdapter : IModelProvider
{
    public int EstimateCost(ModelCallSpec spec)
    {
        var outputTokens = Math.Max(1, spec.MaxOutputTokens / 2);
        var totalTokens = spec.PromptTokens + outputTokens;
        return Math.Max(1, totalTokens / 200);
    }

    public async Task<ModelCallResult> ExecuteAsync(ModelCallSpec spec, CancellationToken cancellationToken)
    {
        var start = DateTimeOffset.UtcNow;
        await Task.Delay(35, cancellationToken);

        if (spec.ModelId.Contains("rate", StringComparison.OrdinalIgnoreCase))
        {
            var mapped = ProviderFailureClassifier.Classify(ProviderErrorClassification.RateLimited);
            return new ModelCallResult
            {
                Success = false,
                FinishReason = "error",
                ErrorClassification = ProviderErrorClassification.RateLimited,
                Outcome = mapped.Outcome,
                LatencyMs = (long)(DateTimeOffset.UtcNow - start).TotalMilliseconds
            };
        }

        if (spec.PromptTokens > 100_000)
        {
            var mapped = ProviderFailureClassifier.Classify(ProviderErrorClassification.ContextTooLarge);
            return new ModelCallResult
            {
                Success = false,
                FinishReason = "error",
                ErrorClassification = ProviderErrorClassification.ContextTooLarge,
                Outcome = mapped.Outcome,
                LatencyMs = (long)(DateTimeOffset.UtcNow - start).TotalMilliseconds
            };
        }

        var actualOutputTokens = Math.Max(1, spec.MaxOutputTokens / 2);
        var result = new ModelCallResult
        {
            Success = true,
            OutputText = $"[fake:{spec.ModelId}] {spec.Prompt[..Math.Min(spec.Prompt.Length, 80)]}",
            ActualPromptTokens = spec.PromptTokens,
            ActualOutputTokens = actualOutputTokens,
            ActualCostCents = EstimateCost(spec),
            FinishReason = "stop",
            ErrorClassification = ProviderErrorClassification.None,
            Outcome = LeaseOutcome.Success,
            LatencyMs = (long)(DateTimeOffset.UtcNow - start).TotalMilliseconds
        };

        return result;
    }
}
