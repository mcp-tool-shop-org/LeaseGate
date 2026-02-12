using LeaseGate.Audit;
using LeaseGate.Policy;
using LeaseGate.Protocol;
using LeaseGate.Service;
using LeaseGate.Service.Tools;

namespace LeaseGate.Hub;

public sealed class HubControlPlane : IDisposable
{
    private readonly LeaseGovernor _governor;
    private readonly IPolicyEngine _policy;
    private readonly DistributedQuotaManager _quotas = new();
    private readonly Dictionary<string, AcquireLeaseRequest> _leaseRequests = new(StringComparer.Ordinal);

    public HubControlPlane(LeaseGovernorOptions options, string policyPath, IAuditWriter? auditWriter = null, ToolRegistry? tools = null)
    {
        _policy = new PolicyEngine(policyPath, hotReload: false);
        _governor = new LeaseGovernor(options, _policy, auditWriter ?? new HubNoopAuditWriter(), tools);
    }

    public async Task<AcquireLeaseResponse> AcquireAsync(AcquireLeaseRequest request, CancellationToken cancellationToken)
    {
        if (!_quotas.TryAcquire(request, _policy.CurrentSnapshot.Policy, out var denyReason, out var retryAfterMs, out var nextRefillUtc))
        {
            return new AcquireLeaseResponse
            {
                Granted = false,
                DeniedReason = denyReason,
                RetryAfterMs = retryAfterMs,
                Recommendation = nextRefillUtc is null
                    ? "retry after budget/rate replenishes"
                    : $"next refill at {nextRefillUtc.Value:O}",
                IdempotencyKey = request.IdempotencyKey,
                PolicyVersion = _policy.CurrentSnapshot.Policy.PolicyVersion,
                PolicyHash = _policy.CurrentSnapshot.PolicyHash,
                OrgId = request.OrgId,
                PrincipalType = request.PrincipalType,
                Role = request.Role,
                LeaseLocality = LeaseLocality.HubIssued
            };
        }

        var response = await _governor.AcquireAsync(request, cancellationToken);
        response.LeaseLocality = LeaseLocality.HubIssued;
        if (response.Granted)
        {
            _leaseRequests[response.LeaseId] = request;
        }
        return response;
    }

    public async Task<ReleaseLeaseResponse> ReleaseAsync(ReleaseLeaseRequest request, CancellationToken cancellationToken)
    {
        var response = await _governor.ReleaseAsync(request, cancellationToken);
        if (_leaseRequests.TryGetValue(request.LeaseId, out var acquireRequest))
        {
            _quotas.Release(acquireRequest);
            _leaseRequests.Remove(request.LeaseId);
        }

        return response;
    }

    public MetricsSnapshot GetMetrics() => _governor.GetMetricsSnapshot();

    public ApprovalRequestResponse RequestApproval(ApprovalRequest request) => _governor.RequestApproval(request);

    public GrantApprovalResponse GrantApproval(GrantApprovalRequest request) => _governor.GrantApproval(request);

    public DenyApprovalResponse DenyApproval(DenyApprovalRequest request) => _governor.DenyApproval(request);

    public StagePolicyBundleResponse StagePolicyBundle(PolicyBundle bundle) => _governor.StagePolicyBundle(bundle);

    public Task<ActivatePolicyResponse> ActivatePolicyAsync(ActivatePolicyRequest request, CancellationToken cancellationToken) =>
        _governor.ActivatePolicyAsync(request, cancellationToken);

    public void Dispose()
    {
        _governor.Dispose();
    }

    private sealed class HubNoopAuditWriter : IAuditWriter
    {
        public Task<AuditWriteResult> WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AuditWriteResult());
        }
    }
}
