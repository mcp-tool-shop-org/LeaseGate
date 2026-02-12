using LeaseGate.Audit;
using LeaseGate.Protocol;
using LeaseGate.Service;
using LeaseGate.Service.Tools;
using LeaseGate.Policy;

namespace LeaseGate.Agent;

public interface ILeaseGateHubClient
{
    Task<AcquireLeaseResponse> AcquireAsync(AcquireLeaseRequest request, CancellationToken cancellationToken);
    Task<ReleaseLeaseResponse> ReleaseAsync(ReleaseLeaseRequest request, CancellationToken cancellationToken);
}

public sealed class LeaseGateAgent : IDisposable
{
    private readonly ILeaseGateHubClient? _hubClient;
    private readonly LeaseGovernor _localGovernor;
    private readonly IAuditWriter _auditWriter;
    private bool _degradedMode;

    public LeaseGateAgent(
        ILeaseGateHubClient? hubClient,
        LeaseGovernorOptions localOptions,
        string policyPath,
        IAuditWriter auditWriter,
        ToolRegistry? tools = null)
    {
        _hubClient = hubClient;
        _auditWriter = auditWriter;
        var policy = new PolicyEngine(policyPath, hotReload: false);
        _localGovernor = new LeaseGovernor(localOptions, policy, auditWriter, tools);
    }

    public bool IsDegradedMode => _degradedMode;

    public async Task<AcquireLeaseResponse> AcquireAsync(AcquireLeaseRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (_hubClient is null)
            {
                throw new InvalidOperationException("hub unavailable");
            }

            var hub = await _hubClient.AcquireAsync(request, cancellationToken);
            hub.LeaseLocality = LeaseLocality.HubIssued;
            hub.DegradedMode = false;
            return hub;
        }
        catch
        {
            if (!_degradedMode)
            {
                _degradedMode = true;
                await _auditWriter.WriteAsync(new AuditEvent
                {
                    EventType = "agent_degraded_mode",
                    TimestampUtc = DateTimeOffset.UtcNow,
                    ProtocolVersion = ProtocolVersionInfo.ProtocolVersion,
                    PolicyHash = string.Empty,
                    LeaseId = string.Empty,
                    OrgId = request.OrgId,
                    ActorId = request.ActorId,
                    WorkspaceId = request.WorkspaceId,
                    PrincipalType = request.PrincipalType,
                    Role = request.Role,
                    ActionType = request.ActionType.ToString(),
                    ModelId = request.ModelId,
                    Decision = "degraded"
                }, cancellationToken);
            }

            var local = await _localGovernor.AcquireAsync(request, cancellationToken);
            local.LeaseLocality = LeaseLocality.LocalIssued;
            local.DegradedMode = true;
            return local;
        }
    }

    public async Task<ReleaseLeaseResponse> ReleaseAsync(ReleaseLeaseRequest request, CancellationToken cancellationToken)
    {
        if (!_degradedMode && _hubClient is not null)
        {
            try
            {
                return await _hubClient.ReleaseAsync(request, cancellationToken);
            }
            catch
            {
            }
        }

        return await _localGovernor.ReleaseAsync(request, cancellationToken);
    }

    public void Dispose()
    {
        _localGovernor.Dispose();
    }
}
