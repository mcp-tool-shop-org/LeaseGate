using LeaseGate.Protocol;

namespace LeaseGate.Service.TokenPools;

public sealed class ContextPool
{
    private readonly int _maxPromptTokens;
    private readonly int _maxRetrievedChunks;
    private readonly int _maxToolOutputTokens;

    public ContextPool(int maxPromptTokens, int maxRetrievedChunks, int maxToolOutputTokens)
    {
        _maxPromptTokens = maxPromptTokens;
        _maxRetrievedChunks = maxRetrievedChunks;
        _maxToolOutputTokens = maxToolOutputTokens;
    }

    public bool TryEvaluate(AcquireLeaseRequest request, out string denyReason, out string recommendation)
    {
        denyReason = string.Empty;
        recommendation = string.Empty;

        if (request.RequestedContextTokens > _maxPromptTokens)
        {
            denyReason = "context_prompt_tokens_exceeded";
            recommendation = "reduce prompt/context tokens";
            return false;
        }

        if (request.RequestedRetrievedChunks > _maxRetrievedChunks)
        {
            denyReason = "context_retrieved_chunks_exceeded";
            recommendation = "reduce retrieval chunk count";
            return false;
        }

        if (request.EstimatedToolOutputTokens > _maxToolOutputTokens)
        {
            denyReason = "tool_output_tokens_exceeded";
            recommendation = "reduce tool output token budget";
            return false;
        }

        return true;
    }

    public int MaxPromptTokens => _maxPromptTokens;
    public int MaxToolOutputTokens => _maxToolOutputTokens;

    public double Utilization(AcquireLeaseRequest request)
    {
        if (_maxPromptTokens == 0)
        {
            return 0;
        }

        return Math.Clamp((double)request.RequestedContextTokens / _maxPromptTokens, 0, 1);
    }
}
