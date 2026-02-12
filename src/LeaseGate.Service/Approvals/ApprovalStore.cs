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
    public int RequiredReviewers { get; set; } = 1;
    public List<ApprovalDecisionTrace> Reviews { get; set; } = new();
}

public sealed class ApprovalStore
{
    private readonly Dictionary<string, ApprovalRecord> _records = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ApprovalRecord> _byToken = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public ApprovalRequestResponse Create(ApprovalRequest request, int requiredReviewers = 1)
    {
        var ttl = TimeSpan.FromSeconds(Math.Max(30, request.TtlSeconds));
        var record = new ApprovalRecord
        {
            ApprovalId = Guid.NewGuid().ToString("N"),
            Request = request,
            ExpiresAtUtc = DateTimeOffset.UtcNow.Add(ttl),
            RequiredReviewers = Math.Max(1, requiredReviewers)
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
            IdempotencyKey = request.IdempotencyKey,
            RequiredReviewers = record.RequiredReviewers,
            CurrentApprovals = 0
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

            ApplyReview(record, request.GrantedBy, true, "legacy grant", DateTimeOffset.UtcNow);

            return new GrantApprovalResponse
            {
                Granted = record.Status == ApprovalDecisionStatus.Granted,
                ApprovalToken = record.Token,
                ExpiresAtUtc = record.ExpiresAtUtc,
                Message = record.Status == ApprovalDecisionStatus.Granted
                    ? "approval granted"
                    : "approval pending additional reviewers",
                IdempotencyKey = request.IdempotencyKey,
                RequiredReviewers = record.RequiredReviewers,
                CurrentApprovals = record.Reviews.Count(r => r.Decision == ApprovalDecisionStatus.Granted)
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

            ApplyReview(record, request.DeniedBy, false, "legacy deny", DateTimeOffset.UtcNow);

            return new DenyApprovalResponse
            {
                Denied = true,
                Message = "approval denied",
                IdempotencyKey = request.IdempotencyKey
            };
        }
    }

    public bool ValidateToken(string token, string actorId, string workspaceId, IEnumerable<ToolIntent> requestedTools, out ApprovalRecord? approvalRecord)
    {
        lock (_lock)
        {
            approvalRecord = null;
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

            approvalRecord = record;
            return true;
        }
    }

    public ApprovalQueueResponse ListPending(ApprovalQueueRequest request)
    {
        lock (_lock)
        {
            ExpirePending();
            var items = _records.Values
                .Where(record => record.Status == ApprovalDecisionStatus.Pending)
                .Where(record => string.IsNullOrWhiteSpace(request.WorkspaceId) || record.Request.WorkspaceId.Equals(request.WorkspaceId, StringComparison.OrdinalIgnoreCase))
                .Where(record => string.IsNullOrWhiteSpace(request.ToolId) || record.Request.ToolId.Equals(request.ToolId, StringComparison.OrdinalIgnoreCase))
                .Where(record => !request.ToolCategory.HasValue || record.Request.ToolCategory == request.ToolCategory)
                .Select(ToQueueItem)
                .ToList();

            return new ApprovalQueueResponse { Items = items };
        }
    }

    public ReviewApprovalResponse Review(ReviewApprovalRequest request)
    {
        lock (_lock)
        {
            if (!_records.TryGetValue(request.ApprovalId, out var record))
            {
                return new ReviewApprovalResponse
                {
                    Accepted = false,
                    Status = ApprovalDecisionStatus.Denied,
                    Message = "approval not found",
                    IdempotencyKey = request.IdempotencyKey
                };
            }

            if (record.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            {
                record.Status = ApprovalDecisionStatus.Expired;
                return new ReviewApprovalResponse
                {
                    Accepted = false,
                    Status = ApprovalDecisionStatus.Expired,
                    Message = "approval expired",
                    RequiredReviewers = record.RequiredReviewers,
                    CurrentApprovals = record.Reviews.Count(r => r.Decision == ApprovalDecisionStatus.Granted),
                    IdempotencyKey = request.IdempotencyKey
                };
            }

            ApplyReview(record, request.ReviewerId, request.Approve, request.Comment, DateTimeOffset.UtcNow);

            return new ReviewApprovalResponse
            {
                Accepted = true,
                Status = record.Status,
                ApprovalToken = record.Token,
                Message = record.Status switch
                {
                    ApprovalDecisionStatus.Granted => "approval granted",
                    ApprovalDecisionStatus.Denied => "approval denied",
                    _ => "approval pending additional reviewers"
                },
                RequiredReviewers = record.RequiredReviewers,
                CurrentApprovals = record.Reviews.Count(r => r.Decision == ApprovalDecisionStatus.Granted),
                IdempotencyKey = request.IdempotencyKey
            };
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
                Used = record.Used,
                RequiredReviewers = record.RequiredReviewers,
                Reviews = record.Reviews.Select(r => new ApprovalDecisionTrace
                {
                    ReviewerId = r.ReviewerId,
                    Decision = r.Decision,
                    ReviewedAtUtc = r.ReviewedAtUtc,
                    Comment = r.Comment,
                    Scope = r.Scope
                }).ToList()
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

    private void ApplyReview(ApprovalRecord record, string reviewerId, bool approve, string comment, DateTimeOffset reviewedAtUtc)
    {
        record.Reviews.RemoveAll(r => r.ReviewerId.Equals(reviewerId, StringComparison.OrdinalIgnoreCase));
        record.Reviews.Add(new ApprovalDecisionTrace
        {
            ReviewerId = reviewerId,
            Decision = approve ? ApprovalDecisionStatus.Granted : ApprovalDecisionStatus.Denied,
            ReviewedAtUtc = reviewedAtUtc,
            Comment = comment,
            Scope = BuildScope(record.Request)
        });

        if (!approve)
        {
            record.Status = ApprovalDecisionStatus.Denied;
            if (!string.IsNullOrWhiteSpace(record.Token))
            {
                _byToken.Remove(record.Token);
            }

            return;
        }

        var grantedReviews = record.Reviews.Count(r => r.Decision == ApprovalDecisionStatus.Granted);
        if (grantedReviews >= record.RequiredReviewers)
        {
            record.Status = ApprovalDecisionStatus.Granted;
            if (string.IsNullOrWhiteSpace(record.Token))
            {
                record.Token = $"appr-{Guid.NewGuid():N}";
            }

            _byToken[record.Token] = record;
        }
        else
        {
            record.Status = ApprovalDecisionStatus.Pending;
        }
    }

    private void ExpirePending()
    {
        foreach (var record in _records.Values)
        {
            if (record.Status == ApprovalDecisionStatus.Pending && record.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            {
                record.Status = ApprovalDecisionStatus.Expired;
            }
        }
    }

    private static ApprovalQueueItem ToQueueItem(ApprovalRecord record)
    {
        return new ApprovalQueueItem
        {
            ApprovalId = record.ApprovalId,
            ActorId = record.Request.ActorId,
            WorkspaceId = record.Request.WorkspaceId,
            Reason = record.Request.Reason,
            ToolId = record.Request.ToolId,
            ToolCategory = record.Request.ToolCategory,
            Status = record.Status,
            ExpiresAtUtc = record.ExpiresAtUtc,
            RequiredReviewers = record.RequiredReviewers,
            CurrentApprovals = record.Reviews.Count(r => r.Decision == ApprovalDecisionStatus.Granted),
            Reviews = record.Reviews.Select(r => new ApprovalDecisionTrace
            {
                ReviewerId = r.ReviewerId,
                Decision = r.Decision,
                ReviewedAtUtc = r.ReviewedAtUtc,
                Comment = r.Comment,
                Scope = r.Scope
            }).ToList()
        };
    }

    private static string BuildScope(ApprovalRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ToolId))
        {
            return $"tool:{request.ToolId}";
        }

        if (request.ToolCategory.HasValue)
        {
            return $"toolCategory:{request.ToolCategory.Value}";
        }

        return "general";
    }
}
