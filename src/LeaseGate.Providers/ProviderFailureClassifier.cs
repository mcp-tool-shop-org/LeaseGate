using LeaseGate.Protocol;

namespace LeaseGate.Providers;

public static class ProviderFailureClassifier
{
    public static (LeaseOutcome Outcome, string Recommendation) Classify(ProviderErrorClassification classification)
    {
        return classification switch
        {
            ProviderErrorClassification.RateLimited => (LeaseOutcome.ProviderRateLimit, "backoff and retry"),
            ProviderErrorClassification.Timeout => (LeaseOutcome.Timeout, "reduce context or retry with higher timeout"),
            ProviderErrorClassification.ContextTooLarge => (LeaseOutcome.PolicyDenied, "reduce context tokens/chunks"),
            ProviderErrorClassification.ModelUnavailable => (LeaseOutcome.UnknownError, "switch model"),
            ProviderErrorClassification.Unauthorized => (LeaseOutcome.UnknownError, "check provider credentials"),
            _ => (LeaseOutcome.UnknownError, "inspect provider error details")
        };
    }
}
