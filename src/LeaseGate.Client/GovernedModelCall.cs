using LeaseGate.Protocol;
using LeaseGate.Providers;

namespace LeaseGate.Client;

public sealed class GovernedExecutionMetrics
{
    public int PromptTokens { get; set; }
    public int OutputTokens { get; set; }
    public int CostCents { get; set; }
    public int ToolCallsCount { get; set; }
    public long BytesIn { get; set; }
    public long BytesOut { get; set; }
    public LeaseOutcome Outcome { get; set; } = LeaseOutcome.Success;
}

public static class GovernedModelCall
{
    public static async Task<TResult> ExecuteAsync<TResult>(
        LeaseGateClient client,
        AcquireLeaseRequest acquireRequest,
        Func<CancellationToken, Task<(TResult Result, GovernedExecutionMetrics Metrics)>> execute,
        CancellationToken cancellationToken)
    {
        var acquired = await client.AcquireAsync(acquireRequest, cancellationToken);
        if (!acquired.Granted)
        {
            if (string.Equals(acquired.DeniedReason, "approval_required", StringComparison.OrdinalIgnoreCase))
            {
                throw new ApprovalRequiredException(acquired);
            }

            throw new InvalidOperationException($"Lease denied: {acquired.DeniedReason}; recommendation: {acquired.Recommendation}");
        }

        try
        {
            var (result, metrics) = await execute(cancellationToken);
            await client.ReleaseAsync(new ReleaseLeaseRequest
            {
                LeaseId = acquired.LeaseId,
                ActualPromptTokens = metrics.PromptTokens,
                ActualOutputTokens = metrics.OutputTokens,
                ActualCostCents = metrics.CostCents,
                ToolCallsCount = metrics.ToolCallsCount,
                BytesIn = metrics.BytesIn,
                BytesOut = metrics.BytesOut,
                Outcome = metrics.Outcome,
                IdempotencyKey = acquireRequest.IdempotencyKey
            }, cancellationToken);

            return result;
        }
        catch
        {
            await client.ReleaseAsync(new ReleaseLeaseRequest
            {
                LeaseId = acquired.LeaseId,
                ActualPromptTokens = 0,
                ActualOutputTokens = 0,
                ActualCostCents = 0,
                ToolCallsCount = 0,
                BytesIn = 0,
                BytesOut = 0,
                Outcome = LeaseOutcome.UnknownError,
                IdempotencyKey = acquireRequest.IdempotencyKey
            }, cancellationToken);
            throw;
        }
    }

    public static async Task<ModelCallResult> ExecuteProviderCallAsync(
        LeaseGateClient client,
        IModelProvider provider,
        ModelCallSpec spec,
        AcquireLeaseRequest acquireRequest,
        CancellationToken cancellationToken)
    {
        acquireRequest.EstimatedCostCents = provider.EstimateCost(spec);
        var acquired = await client.AcquireAsync(acquireRequest, cancellationToken);
        if (!acquired.Granted)
        {
            if (string.Equals(acquired.DeniedReason, "approval_required", StringComparison.OrdinalIgnoreCase))
            {
                throw new ApprovalRequiredException(acquired);
            }

            throw new InvalidOperationException($"Lease denied: {acquired.DeniedReason}; recommendation: {acquired.Recommendation}");
        }

        try
        {
            var result = await provider.ExecuteAsync(spec, cancellationToken);
            await client.ReleaseAsync(new ReleaseLeaseRequest
            {
                LeaseId = acquired.LeaseId,
                ActualPromptTokens = result.ActualPromptTokens,
                ActualOutputTokens = result.ActualOutputTokens,
                ActualCostCents = result.ActualCostCents,
                ToolCallsCount = 0,
                BytesIn = result.ActualPromptTokens * 4L,
                BytesOut = result.ActualOutputTokens * 4L,
                LatencyMs = result.LatencyMs,
                ProviderErrorClassification = result.ErrorClassification,
                Outcome = result.Outcome,
                IdempotencyKey = acquireRequest.IdempotencyKey
            }, cancellationToken);

            return result;
        }
        catch (Exception)
        {
            await client.ReleaseAsync(new ReleaseLeaseRequest
            {
                LeaseId = acquired.LeaseId,
                ActualPromptTokens = 0,
                ActualOutputTokens = 0,
                ActualCostCents = 0,
                ToolCallsCount = 0,
                BytesIn = 0,
                BytesOut = 0,
                LatencyMs = 0,
                ProviderErrorClassification = ProviderErrorClassification.Unknown,
                Outcome = LeaseOutcome.UnknownError,
                IdempotencyKey = acquireRequest.IdempotencyKey
            }, cancellationToken);
            throw;
        }
    }
}
