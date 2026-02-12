using LeaseGate.Protocol;

namespace LeaseGate.Client;

public sealed class ApprovalRequiredException : InvalidOperationException
{
    public ApprovalRequiredException(AcquireLeaseResponse response)
        : base($"Approval required: {response.DeniedReason}. {response.Recommendation}")
    {
        Response = response;
    }

    public AcquireLeaseResponse Response { get; }
}
