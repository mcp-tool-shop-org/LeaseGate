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

        if (_options.FallbackMode == FallbackMode.Dev)
        {
            if (hasRiskyCapability)
            {
                return Denied(request, "service_unavailable_risky_capability", "disable risky capabilities in dev fallback");
            }

            if (request.MaxOutputTokens > _options.DevMaxOutputTokens)
            {
                return Denied(request, "service_unavailable_dev_cap", "reduce max output tokens for local fallback");
            }

            return GrantLocal(request, "dev fallback grant");
        }

        if (hasRiskyCapability)
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
