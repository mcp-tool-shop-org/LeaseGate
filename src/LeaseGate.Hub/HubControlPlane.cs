using LeaseGate.Audit;
using LeaseGate.Policy;
using LeaseGate.Protocol;
using LeaseGate.Service;
using LeaseGate.Service.Tools;
using System.Text;

namespace LeaseGate.Hub;

public sealed class HubControlPlane : IDisposable
{
    private readonly LeaseGovernor _governor;
    private readonly IPolicyEngine _policy;
    private readonly DistributedQuotaManager _quotas = new();
    private readonly CostAttributionTracker _attribution = new();
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
            _attribution.RecordDenied(request, denyReason);
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
        else
        {
            _quotas.Rollback(request);
            _attribution.RecordDenied(request, response.DeniedReason);
        }

        return response;
    }

    public async Task<ReleaseLeaseResponse> ReleaseAsync(ReleaseLeaseRequest request, CancellationToken cancellationToken)
    {
        var response = await _governor.ReleaseAsync(request, cancellationToken);
        if (_leaseRequests.TryGetValue(request.LeaseId, out var acquireRequest))
        {
            _quotas.Release(acquireRequest);
            _attribution.RecordRelease(acquireRequest, request);
            _leaseRequests.Remove(request.LeaseId);
        }

        return response;
    }

    public DailyReportResponse GetDailyReport()
    {
        var budgetLimit = _policy.CurrentSnapshot.Policy.OrgDailyBudgetCents > 0
            ? _policy.CurrentSnapshot.Policy.OrgDailyBudgetCents
            : _policy.CurrentSnapshot.Policy.DailyBudgetCents;

        return _attribution.BuildDailyReport(budgetLimit);
    }

    public ExportSummaryResponse ExportDailySummary(ExportSummaryRequest request)
    {
        try
        {
            var report = GetDailyReport();
            var outputPath = string.IsNullOrWhiteSpace(request.OutputPath)
                ? Path.Combine(Path.GetTempPath(), $"leasegate-daily-report-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.{request.Format}")
                : request.OutputPath;

            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (request.Format.Equals("csv", StringComparison.OrdinalIgnoreCase))
            {
                var lines = new List<string> { "orgId,workspaceId,actorId,modelId,toolId,spendCents,count" };
                lines.AddRange(report.TopSpenders.Select(r =>
                    $"{r.OrgId},{r.WorkspaceId},{r.ActorId},{r.ModelId},{r.ToolId},{r.SpendCents},{r.Count}"));
                File.WriteAllLines(outputPath, lines, Encoding.UTF8);
            }
            else
            {
                File.WriteAllText(outputPath, ProtocolJson.Serialize(report), Encoding.UTF8);
            }

            return new ExportSummaryResponse
            {
                Exported = true,
                OutputPath = outputPath,
                Message = "daily summary exported",
                IdempotencyKey = request.IdempotencyKey
            };
        }
        catch (Exception ex)
        {
            return new ExportSummaryResponse
            {
                Exported = false,
                OutputPath = request.OutputPath,
                Message = ex.Message,
                IdempotencyKey = request.IdempotencyKey
            };
        }
    }

    public string PrintDailyReport()
    {
        var report = GetDailyReport();
        var lines = new List<string>
        {
            $"daily report @ {report.GeneratedAtUtc:O}",
            $"total spend: {report.TotalSpendCents} cents",
            "top costs:"
        };

        lines.AddRange(report.TopSpenders.Take(5).Select(item =>
            $"- {item.OrgId}/{item.WorkspaceId}/{item.ActorId} model={item.ModelId} tool={item.ToolId} spend={item.SpendCents} count={item.Count}"));

        lines.Add("top throttle causes:");
        lines.AddRange(report.TopDeniedReasons.Take(5).Select(item => $"- {item.Key}: {item.Value}"));

        if (report.Alerts.Count > 0)
        {
            lines.Add("alerts:");
            lines.AddRange(report.Alerts.Select(alert => $"- {alert.Code}: {alert.Message}"));
        }

        return string.Join(Environment.NewLine, lines);
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
