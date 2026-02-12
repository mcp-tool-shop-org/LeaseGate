using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using LeaseGate.Protocol;

namespace LeaseGate.Policy;

public interface IPolicyEngine
{
    PolicySnapshot CurrentSnapshot { get; }
    PolicyDecision Evaluate(AcquireLeaseRequest request);
    bool TryResolveServiceAccount(string token, string orgId, string workspaceId, out ServiceAccountPolicy? account);
    StagePolicyBundleResponse StageBundle(PolicyBundle bundle);
    ActivatePolicyResponse ActivateStaged(ActivatePolicyRequest request);
}

public sealed class PolicyEngine : IPolicyEngine, IDisposable
{
    private readonly string _policyFilePath;
    private readonly bool _hotReload;
    private readonly PolicyEngineOptions _options;
    private readonly object _lock = new();
    private FileSystemWatcher? _watcher;
    private PolicySnapshot _snapshot;
    private PolicySnapshot? _stagedSnapshot;

    public PolicyEngine(string policyFilePath, bool hotReload = false, PolicyEngineOptions? options = null)
    {
        _policyFilePath = policyFilePath;
        _hotReload = hotReload;
        _options = options ?? new PolicyEngineOptions();
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
        var key = $"{request.ActorId}|{request.WorkspaceId}";
        var workspaceRoleKey = $"{request.WorkspaceId}|{request.Role}";

        if (policy.AllowedModelsByWorkspace.TryGetValue(request.WorkspaceId, out var workspaceModels) &&
            workspaceModels.Count > 0 &&
            !workspaceModels.Contains(request.ModelId, StringComparer.OrdinalIgnoreCase))
        {
            return PolicyDecision.Deny("workspace_model_not_allowed", "select a model allowed for this workspace");
        }

        if (policy.AllowedModels.Count > 0 && !policy.AllowedModels.Contains(request.ModelId, StringComparer.OrdinalIgnoreCase))
        {
            return PolicyDecision.Deny("model_not_allowed", "select an allowed model");
        }

        var roleCapabilitiesFound = policy.AllowedCapabilitiesByRole.TryGetValue(request.Role, out var roleCapabilitiesByAction) &&
                                    roleCapabilitiesByAction is not null;
        if (roleCapabilitiesFound &&
            roleCapabilitiesByAction!.TryGetValue(request.ActionType, out var allowedForRoleAction) &&
            allowedForRoleAction.Count > 0)
        {
            var allowed = allowedForRoleAction.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
            var deniedCapability = request.RequestedCapabilities.FirstOrDefault(cap => !allowed.Contains(cap));
            if (!string.IsNullOrWhiteSpace(deniedCapability))
            {
                return PolicyDecision.Deny("capability_not_allowed_for_role", "remove restricted capabilities for this role");
            }
        }
        else if (policy.AllowedCapabilities.TryGetValue(request.ActionType, out var allowedForAction) &&
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

        var deniedCategories = policy.DeniedToolCategories.ToImmutableHashSet();
        var blockedByCategory = request.RequestedTools.FirstOrDefault(t => deniedCategories.Contains(t.Category));
        if (blockedByCategory is not null)
        {
            return PolicyDecision.Deny(
                $"tool_category_denied:{blockedByCategory.Category}",
                $"remove restricted tool category {blockedByCategory.Category}");
        }

        if (policy.AllowedToolsByActorWorkspace.TryGetValue(key, out var allowedTools) && allowedTools.Count > 0)
        {
            var allowlist = allowedTools.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
            var blockedTool = request.RequestedTools.FirstOrDefault(t => !allowlist.Contains(t.ToolId));
            if (blockedTool is not null)
            {
                return PolicyDecision.Deny(
                    $"tool_not_allowed:{blockedTool.ToolId}",
                    "request an allowed tool for this actor/workspace");
            }
        }

        if (policy.AllowedToolsByWorkspaceRole.TryGetValue(workspaceRoleKey, out var roleAllowedTools) && roleAllowedTools.Count > 0)
        {
            var allowlist = roleAllowedTools.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
            var blockedTool = request.RequestedTools.FirstOrDefault(t => !allowlist.Contains(t.ToolId));
            if (blockedTool is not null)
            {
                return PolicyDecision.Deny(
                    $"tool_not_allowed_for_role:{blockedTool.ToolId}",
                    "request an allowed tool for this workspace role");
            }
        }

        return PolicyDecision.Allow();
    }

    public bool TryResolveServiceAccount(string token, string orgId, string workspaceId, out ServiceAccountPolicy? account)
    {
        var policy = CurrentSnapshot.Policy;
        account = policy.ServiceAccounts.FirstOrDefault(sa =>
            !string.IsNullOrWhiteSpace(sa.Token) &&
            sa.Token.Equals(token, StringComparison.Ordinal) &&
            sa.OrgId.Equals(orgId, StringComparison.OrdinalIgnoreCase) &&
            sa.WorkspaceId.Equals(workspaceId, StringComparison.OrdinalIgnoreCase));

        return account is not null;
    }

    public StagePolicyBundleResponse StageBundle(PolicyBundle bundle)
    {
        try
        {
            if (_options.RequireSignedBundles)
            {
                if (!VerifyBundleSignature(bundle))
                {
                    return new StagePolicyBundleResponse
                    {
                        Accepted = false,
                        Message = "invalid policy bundle signature"
                    };
                }
            }

            var policy = ProtocolJson.Deserialize<LeaseGatePolicy>(bundle.PolicyContentJson);
            policy.PolicyVersion = bundle.Version;
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(bundle.PolicyContentJson))).ToLowerInvariant();

            lock (_lock)
            {
                _stagedSnapshot = new PolicySnapshot
                {
                    Policy = policy,
                    RawText = bundle.PolicyContentJson,
                    PolicyHash = hash
                };
            }

            return new StagePolicyBundleResponse
            {
                Accepted = true,
                Message = "policy staged",
                StagedPolicyHash = hash,
                StagedPolicyVersion = policy.PolicyVersion
            };
        }
        catch (Exception ex)
        {
            return new StagePolicyBundleResponse
            {
                Accepted = false,
                Message = ex.Message
            };
        }
    }

    public ActivatePolicyResponse ActivateStaged(ActivatePolicyRequest request)
    {
        lock (_lock)
        {
            if (_stagedSnapshot is null)
            {
                return new ActivatePolicyResponse
                {
                    Activated = false,
                    Message = "no staged policy"
                };
            }

            if (!string.IsNullOrWhiteSpace(request.Version) &&
                !_stagedSnapshot.Policy.PolicyVersion.Equals(request.Version, StringComparison.OrdinalIgnoreCase))
            {
                return new ActivatePolicyResponse
                {
                    Activated = false,
                    Message = "staged policy version mismatch"
                };
            }

            _snapshot = _stagedSnapshot;
            _stagedSnapshot = null;

            return new ActivatePolicyResponse
            {
                Activated = true,
                Message = "policy activated",
                ActivePolicyHash = _snapshot.PolicyHash,
                ActivePolicyVersion = _snapshot.Policy.PolicyVersion
            };
        }
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

    private bool VerifyBundleSignature(PolicyBundle bundle)
    {
        if (_options.AllowedPublicKeysBase64.Count == 0)
        {
            return false;
        }

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(bundle.SignatureBase64);
        }
        catch
        {
            return false;
        }

        var payload = Encoding.UTF8.GetBytes($"{bundle.Version}|{bundle.CreatedAtUtc:O}|{bundle.Author}|{bundle.PolicyContentJson}");

        foreach (var keyBase64 in _options.AllowedPublicKeysBase64)
        {
            try
            {
                var publicKey = Convert.FromBase64String(keyBase64);
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(publicKey, out _);
                if (ecdsa.VerifyData(payload, signature, HashAlgorithmName.SHA256))
                {
                    return true;
                }
            }
            catch
            {
            }
        }

        return false;
    }

    public void Dispose()
    {
        _watcher?.Dispose();
    }
}
