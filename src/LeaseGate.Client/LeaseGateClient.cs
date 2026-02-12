using System.IO.Pipes;
using LeaseGate.Protocol;

namespace LeaseGate.Client;

public sealed class LeaseGateClient
{
    private readonly LeaseGateClientOptions _options;
    private readonly HashSet<string> _localLeaseIds = new(StringComparer.Ordinal);

    public LeaseGateClient(LeaseGateClientOptions options)
    {
        _options = options;
    }

    public async Task<AcquireLeaseResponse> AcquireAsync(AcquireLeaseRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await SendAsync<AcquireLeaseRequest, AcquireLeaseResponse>("Acquire", request, cancellationToken);
        }
        catch
        {
            return ApplyFallbackAcquire(request);
        }
    }

    public Task<ApprovalRequestResponse> RequestApprovalAsync(ApprovalRequest request, CancellationToken cancellationToken)
    {
        return SendAsync<ApprovalRequest, ApprovalRequestResponse>("RequestApproval", request, cancellationToken);
    }

    public Task<GrantApprovalResponse> GrantApprovalAsync(GrantApprovalRequest request, CancellationToken cancellationToken)
    {
        return SendAsync<GrantApprovalRequest, GrantApprovalResponse>("GrantApproval", request, cancellationToken);
    }

    public Task<DenyApprovalResponse> DenyApprovalAsync(DenyApprovalRequest request, CancellationToken cancellationToken)
    {
        return SendAsync<DenyApprovalRequest, DenyApprovalResponse>("DenyApproval", request, cancellationToken);
    }

    public Task<MetricsSnapshot> GetMetricsAsync(CancellationToken cancellationToken)
    {
        return SendAsync<object, MetricsSnapshot>("GetMetrics", new { }, cancellationToken);
    }

    public Task<GovernorStatusResponse> GetStatusAsync(CancellationToken cancellationToken)
    {
        return SendAsync<object, GovernorStatusResponse>("GetStatus", new { }, cancellationToken);
    }

    public Task<ExportDiagnosticsResponse> ExportDiagnosticsAsync(ExportDiagnosticsRequest request, CancellationToken cancellationToken)
    {
        return SendAsync<ExportDiagnosticsRequest, ExportDiagnosticsResponse>("ExportDiagnostics", request, cancellationToken);
    }

    public Task<StagePolicyBundleResponse> StagePolicyBundleAsync(PolicyBundle bundle, CancellationToken cancellationToken)
    {
        return SendAsync<PolicyBundle, StagePolicyBundleResponse>("StagePolicyBundle", bundle, cancellationToken);
    }

    public Task<ActivatePolicyResponse> ActivatePolicyAsync(ActivatePolicyRequest request, CancellationToken cancellationToken)
    {
        return SendAsync<ActivatePolicyRequest, ActivatePolicyResponse>("ActivatePolicy", request, cancellationToken);
    }

    public Task<ToolSubLeaseResponse> RequestToolSubLeaseAsync(ToolSubLeaseRequest request, CancellationToken cancellationToken)
    {
        return SendAsync<ToolSubLeaseRequest, ToolSubLeaseResponse>("RequestToolSubLease", request, cancellationToken);
    }

    public Task<ToolExecutionResponse> ExecuteToolCallAsync(ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        return SendAsync<ToolExecutionRequest, ToolExecutionResponse>("ExecuteToolCall", request, cancellationToken);
    }

    public async Task<ReleaseLeaseResponse> ReleaseAsync(ReleaseLeaseRequest request, CancellationToken cancellationToken)
    {
        if (_localLeaseIds.Remove(request.LeaseId))
        {
            return new ReleaseLeaseResponse
            {
                Classification = ReleaseClassification.Recorded,
                Recommendation = "local fallback release recorded",
                IdempotencyKey = request.IdempotencyKey
            };
        }

        try
        {
            return await SendAsync<ReleaseLeaseRequest, ReleaseLeaseResponse>("Release", request, cancellationToken);
        }
        catch
        {
            return new ReleaseLeaseResponse
            {
                Classification = ReleaseClassification.LeaseNotFound,
                Recommendation = "service unavailable during release",
                IdempotencyKey = request.IdempotencyKey
            };
        }
    }

    private AcquireLeaseResponse ApplyFallbackAcquire(AcquireLeaseRequest request)
    {
        var hasRiskyCapability = request.RequestedCapabilities.Any(cap => _options.RiskyCapabilities.Contains(cap));
        var hasRiskyTool = request.RequestedTools.Any(t =>
            t.Category is ToolCategory.NetworkWrite or ToolCategory.FileWrite or ToolCategory.Exec);
        var hasRisky = hasRiskyCapability || hasRiskyTool;

        if (_options.FallbackMode == FallbackMode.Dev)
        {
            if (hasRisky)
            {
                return Denied(request, "service_unavailable_risky_capability", "disable risky capabilities in dev fallback");
            }

            if (request.MaxOutputTokens > _options.DevMaxOutputTokens)
            {
                return Denied(request, "service_unavailable_dev_cap", "reduce max output tokens for local fallback");
            }

            return GrantLocal(request, "dev fallback grant");
        }

        if (hasRisky)
        {
            return Denied(request, "service_unavailable_prod_deny_risky", "retry when governor service is available");
        }

        if (request.ActionType != ActionType.ChatCompletion)
        {
            return Denied(request, "service_unavailable_prod_readonly_only", "only read-only chat is allowed in prod fallback");
        }

        if (request.MaxOutputTokens > _options.ProdReadOnlyMaxOutputTokens)
        {
            return Denied(request, "service_unavailable_prod_cap", "reduce output tokens for prod fallback");
        }

        return GrantLocal(request, "prod read-only fallback grant");
    }

    private AcquireLeaseResponse GrantLocal(AcquireLeaseRequest request, string recommendation)
    {
        var leaseId = $"local-{Guid.NewGuid():N}";
        _localLeaseIds.Add(leaseId);

        return new AcquireLeaseResponse
        {
            Granted = true,
            LeaseId = leaseId,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(15),
            Constraints = new LeaseConstraints(),
            Recommendation = recommendation,
            IdempotencyKey = request.IdempotencyKey
        };
    }

    private static AcquireLeaseResponse Denied(AcquireLeaseRequest request, string deniedReason, string recommendation)
    {
        return new AcquireLeaseResponse
        {
            Granted = false,
            DeniedReason = deniedReason,
            Recommendation = recommendation,
            RetryAfterMs = 1000,
            IdempotencyKey = request.IdempotencyKey,
            Constraints = new LeaseConstraints()
        };
    }

    private async Task<TResponse> SendAsync<TRequest, TResponse>(string command, TRequest payload, CancellationToken cancellationToken)
    {
        using var client = new NamedPipeClientStream(".", _options.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(1000, cancellationToken);

        var request = new PipeCommandRequest
        {
            Command = command,
            PayloadJson = ProtocolJson.Serialize(payload)
        };
        await PipeMessageFraming.WriteAsync(client, request, cancellationToken);

        var response = await PipeMessageFraming.ReadAsync<PipeCommandResponse>(client, cancellationToken);
        if (!response.Success)
        {
            throw new InvalidOperationException(response.Error);
        }

        return ProtocolJson.Deserialize<TResponse>(response.PayloadJson);
    }
}
