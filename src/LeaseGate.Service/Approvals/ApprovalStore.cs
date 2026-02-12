using LeaseGate.Protocol;

namespace LeaseGate.Service.Approvals;

public sealed class ApprovalRecord
{
    public string ApprovalId { get; init; } = string.Empty;
    public ApprovalRequest Request { get; init; } = new();
    public ApprovalDecisionStatus Status { get; set; } = ApprovalDecisionStatus.Pending;
    public DateTimeOffset ExpiresAtUtc { get; init; }
    public string Token { get; set; } = string.Empty;
    public bool Used { get; set; }
}

public sealed class ApprovalStore
{
    private readonly Dictionary<string, ApprovalRecord> _records = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ApprovalRecord> _byToken = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public ApprovalRequestResponse Create(ApprovalRequest request)
    {
        var ttl = TimeSpan.FromSeconds(Math.Max(30, request.TtlSeconds));
        var record = new ApprovalRecord
        {
            ApprovalId = Guid.NewGuid().ToString("N"),
            Request = request,
            ExpiresAtUtc = DateTimeOffset.UtcNow.Add(ttl)
        };

        lock (_lock)
        {
            _records[record.ApprovalId] = record;
        }

        return new ApprovalRequestResponse
        {
            ApprovalId = record.ApprovalId,
            Status = ApprovalDecisionStatus.Pending,
            ExpiresAtUtc = record.ExpiresAtUtc,
            Message = "approval request created",
            IdempotencyKey = request.IdempotencyKey
        };
    }

    public GrantApprovalResponse Grant(GrantApprovalRequest request)
    {
        lock (_lock)
        {
            if (!_records.TryGetValue(request.ApprovalId, out var record))
            {
                return new GrantApprovalResponse
                {
                    Granted = false,
                    Message = "approval not found",
                    IdempotencyKey = request.IdempotencyKey
                };
            }

            if (record.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            {
                record.Status = ApprovalDecisionStatus.Expired;
                return new GrantApprovalResponse
                {
                    Granted = false,
                    Message = "approval expired",
                    IdempotencyKey = request.IdempotencyKey
                };
            }

            if (record.Status == ApprovalDecisionStatus.Denied)
            {
                return new GrantApprovalResponse
                {
                    Granted = false,
                    Message = "approval denied",
                    IdempotencyKey = request.IdempotencyKey
                };
            }

            if (string.IsNullOrWhiteSpace(record.Token))
            {
                record.Token = $"appr-{Guid.NewGuid():N}";
                record.Status = ApprovalDecisionStatus.Granted;
                _byToken[record.Token] = record;
            }

            return new GrantApprovalResponse
            {
                Granted = true,
                ApprovalToken = record.Token,
                ExpiresAtUtc = record.ExpiresAtUtc,
                Message = "approval granted",
                IdempotencyKey = request.IdempotencyKey
            };
        }
    }

    public DenyApprovalResponse Deny(DenyApprovalRequest request)
    {
        lock (_lock)
        {
            if (!_records.TryGetValue(request.ApprovalId, out var record))
            {
                return new DenyApprovalResponse
                {
                    Denied = false,
                    Message = "approval not found",
                    IdempotencyKey = request.IdempotencyKey
                };
            }

            record.Status = ApprovalDecisionStatus.Denied;
            if (!string.IsNullOrWhiteSpace(record.Token))
            {
                _byToken.Remove(record.Token);
            }

            return new DenyApprovalResponse
            {
                Denied = true,
                Message = "approval denied",
                IdempotencyKey = request.IdempotencyKey
            };
        }
    }

    public bool ValidateToken(string token, string actorId, string workspaceId, IEnumerable<ToolIntent> requestedTools)
    {
        lock (_lock)
        {
            if (!_byToken.TryGetValue(token, out var record))
            {
                return false;
            }

            if (record.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            {
                record.Status = ApprovalDecisionStatus.Expired;
                _byToken.Remove(token);
                return false;
            }

            var req = record.Request;
            if (!actorId.Equals(req.ActorId, StringComparison.OrdinalIgnoreCase) ||
                !workspaceId.Equals(req.WorkspaceId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var scopeSatisfied = requestedTools.Any(tool =>
                (!string.IsNullOrWhiteSpace(req.ToolId) && tool.ToolId.Equals(req.ToolId, StringComparison.OrdinalIgnoreCase)) ||
                (req.ToolCategory.HasValue && tool.Category == req.ToolCategory));

            if (!scopeSatisfied && (req.ToolCategory.HasValue || !string.IsNullOrWhiteSpace(req.ToolId)))
            {
                return false;
            }

            if (req.SingleUse)
            {
                if (record.Used)
                {
                    return false;
                }

                record.Used = true;
            }

            return true;
        }
    }

    public List<ApprovalRecord> Snapshot()
    {
        lock (_lock)
        {
            return _records.Values.Select(record => new ApprovalRecord
            {
                ApprovalId = record.ApprovalId,
                Request = record.Request,
                Status = record.Status,
                ExpiresAtUtc = record.ExpiresAtUtc,
                Token = record.Token,
                Used = record.Used
            }).ToList();
        }
    }

    public void Restore(IEnumerable<ApprovalRecord> approvals)
    {
        lock (_lock)
        {
            _records.Clear();
            _byToken.Clear();

            foreach (var approval in approvals)
            {
                _records[approval.ApprovalId] = approval;
                if (!string.IsNullOrWhiteSpace(approval.Token))
                {
                    _byToken[approval.Token] = approval;
                }
            }
        }
    }
}
