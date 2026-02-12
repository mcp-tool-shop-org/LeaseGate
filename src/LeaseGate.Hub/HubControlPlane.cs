using LeaseGate.Audit;
using LeaseGate.Policy;
using LeaseGate.Protocol;
using LeaseGate.Service;
using LeaseGate.Service.Tools;

namespace LeaseGate.Hub;

public sealed class HubControlPlane : IDisposable
{
    private readonly LeaseGovernor _governor;

    public HubControlPlane(LeaseGovernorOptions options, string policyPath, IAuditWriter? auditWriter = null, ToolRegistry? tools = null)
    {
        var policy = new PolicyEngine(policyPath, hotReload: false);
        _governor = new LeaseGovernor(options, policy, auditWriter ?? new HubNoopAuditWriter(), tools);
    }

    public async Task<AcquireLeaseResponse> AcquireAsync(AcquireLeaseRequest request, CancellationToken cancellationToken)
    {
        var response = await _governor.AcquireAsync(request, cancellationToken);
        response.LeaseLocality = LeaseLocality.HubIssued;
        return response;
    }

    public Task<ReleaseLeaseResponse> ReleaseAsync(ReleaseLeaseRequest request, CancellationToken cancellationToken)
    {
        return _governor.ReleaseAsync(request, cancellationToken);
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
