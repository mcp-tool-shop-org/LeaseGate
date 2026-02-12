using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using LeaseGate.Protocol;

namespace LeaseGate.Policy;

public interface IPolicyEngine
{
    PolicySnapshot CurrentSnapshot { get; }
    PolicyDecision Evaluate(AcquireLeaseRequest request);
}

public sealed class PolicyEngine : IPolicyEngine, IDisposable
{
    private readonly string _policyFilePath;
    private readonly bool _hotReload;
    private readonly object _lock = new();
    private FileSystemWatcher? _watcher;
    private PolicySnapshot _snapshot;

    public PolicyEngine(string policyFilePath, bool hotReload = false)
    {
        _policyFilePath = policyFilePath;
        _hotReload = hotReload;
        _snapshot = LoadSnapshot(policyFilePath);

        if (_hotReload)
        {
            var directory = Path.GetDirectoryName(policyFilePath)!;
            var fileName = Path.GetFileName(policyFilePath);
            _watcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
            };
            _watcher.Changed += (_, _) => TryReload();
            _watcher.Created += (_, _) => TryReload();
            _watcher.Renamed += (_, _) => TryReload();
            _watcher.EnableRaisingEvents = true;
        }
    }

    public PolicySnapshot CurrentSnapshot
    {
        get
        {
            lock (_lock)
            {
                return _snapshot;
            }
        }
    }

    public PolicyDecision Evaluate(AcquireLeaseRequest request)
    {
        var policy = CurrentSnapshot.Policy;

        if (policy.AllowedModels.Count > 0 && !policy.AllowedModels.Contains(request.ModelId, StringComparer.OrdinalIgnoreCase))
        {
            return PolicyDecision.Deny("model_not_allowed", "select an allowed model");
        }

        if (policy.AllowedCapabilities.TryGetValue(request.ActionType, out var allowedForAction) &&
            allowedForAction.Count > 0)
        {
            var allowed = allowedForAction.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
            var deniedCapability = request.RequestedCapabilities.FirstOrDefault(cap => !allowed.Contains(cap));
            if (!string.IsNullOrWhiteSpace(deniedCapability))
            {
                return PolicyDecision.Deny("capability_not_allowed", "remove restricted capabilities");
            }
        }

        var approvalRequired = policy.RiskRequiresApproval.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        var risky = request.RiskFlags.FirstOrDefault(flag => approvalRequired.Contains(flag));
        if (!string.IsNullOrWhiteSpace(risky))
        {
            return PolicyDecision.Deny("risk_requires_approval", "request approval for risky operation");
        }

        return PolicyDecision.Allow();
    }

    private static PolicySnapshot LoadSnapshot(string path)
    {
        var raw = File.ReadAllText(path);
        var policy = ProtocolJson.Deserialize<LeaseGatePolicy>(raw);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        return new PolicySnapshot
        {
            Policy = policy,
            RawText = raw,
            PolicyHash = hash
        };
    }

    private void TryReload()
    {
        try
        {
            var loaded = LoadSnapshot(_policyFilePath);
            lock (_lock)
            {
                _snapshot = loaded;
            }
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
    }
}
