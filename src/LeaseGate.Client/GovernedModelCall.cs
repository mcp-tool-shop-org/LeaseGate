using LeaseGate.Protocol;

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
}
