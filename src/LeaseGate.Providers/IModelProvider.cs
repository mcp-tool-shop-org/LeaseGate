using LeaseGate.Protocol;

namespace LeaseGate.Providers;

public interface IModelProvider
{
    int EstimateCost(ModelCallSpec spec);
    Task<ModelCallResult> ExecuteAsync(ModelCallSpec spec, CancellationToken cancellationToken);
}
