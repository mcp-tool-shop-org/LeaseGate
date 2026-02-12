using LeaseGate.Policy;
using LeaseGate.Protocol;

namespace LeaseGate.Tests;

public class Phase5GitOpsPolicyTests
{
    [Fact]
    public void GitOpsPolicies_LoadAndLintSuccessfully()
    {
        var policyDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "policies");
        var full = Path.GetFullPath(policyDir);

        var policy = PolicyGitOpsLoader.LoadFromDirectory(full);
        var errors = PolicyGitOpsLoader.Lint(policy);

        Assert.Empty(errors);
        Assert.NotEmpty(policy.AllowedModels);
        Assert.Contains(ToolCategory.NetworkWrite, policy.DeniedToolCategories);
        Assert.Contains(ToolCategory.Exec, policy.DeniedToolCategories);
    }

    [Fact]
    public void Linter_FailsWhenRiskyDenyByDefaultMissing()
    {
        var policy = new LeaseGatePolicy
        {
            PolicyVersion = "bad",
            DailyBudgetCents = 100,
            MaxInFlight = 1,
            MaxRequestsPerMinute = 10,
            MaxTokensPerMinute = 100,
            AllowedModels = new List<string> { "gpt-4o-mini" },
            DeniedToolCategories = new List<ToolCategory> { ToolCategory.FileWrite }
        };

        var errors = PolicyGitOpsLoader.Lint(policy);
        Assert.Contains(errors, e => e.Contains("deny-by-default", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SignedPolicyVerification_RejectsTamperedBundle()
    {
        using var ecdsa = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var publicKey = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());

        var json = "{\"policyVersion\":\"v5\",\"allowedModels\":[\"gpt-4o-mini\"],\"allowedCapabilities\":{},\"riskRequiresApproval\":[]}";
        var bundle = new PolicyBundle
        {
            Version = "v5",
            Author = "ci",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            PolicyContentJson = json
        };

        var payload = System.Text.Encoding.UTF8.GetBytes($"{bundle.Version}|{bundle.CreatedAtUtc:O}|{bundle.Author}|{bundle.PolicyContentJson}");
        bundle.SignatureBase64 = Convert.ToBase64String(ecdsa.SignData(payload, System.Security.Cryptography.HashAlgorithmName.SHA256));

        var policyFile = Path.Combine(Path.GetTempPath(), $"leasegate-phase5-gitops-{Guid.NewGuid():N}.json");
        File.WriteAllText(policyFile, json);

        var engine = new PolicyEngine(policyFile, options: new PolicyEngineOptions
        {
            RequireSignedBundles = true,
            AllowedPublicKeysBase64 = new List<string> { publicKey }
        });

        var accepted = engine.StageBundle(bundle);
        Assert.True(accepted.Accepted);

        bundle.PolicyContentJson = json.Replace("gpt-4o-mini", "blocked-model", StringComparison.Ordinal);
        var tampered = engine.StageBundle(bundle);
        Assert.False(tampered.Accepted);
    }

    [Fact]
    public void Regression_RoleDifferentiation_StaysStable()
    {
        var policy = new LeaseGatePolicy
        {
            AllowedModels = new List<string> { "gpt-4o-mini" },
            AllowedCapabilitiesByRole = new Dictionary<Role, Dictionary<ActionType, List<string>>>
            {
                [Role.Member] = new()
                {
                    [ActionType.ChatCompletion] = new List<string> { "chat" }
                },
                [Role.Viewer] = new()
                {
                    [ActionType.ChatCompletion] = new List<string> { "read" }
                }
            }
        };

        var policyFile = Path.Combine(Path.GetTempPath(), $"leasegate-phase5-regression-{Guid.NewGuid():N}.json");
        File.WriteAllText(policyFile, ProtocolJson.Serialize(policy));
        var engine = new PolicyEngine(policyFile);

        var member = engine.Evaluate(new AcquireLeaseRequest
        {
            Role = Role.Member,
            ActionType = ActionType.ChatCompletion,
            ModelId = "gpt-4o-mini",
            RequestedCapabilities = new List<string> { "chat" }
        });
        var viewer = engine.Evaluate(new AcquireLeaseRequest
        {
            Role = Role.Viewer,
            ActionType = ActionType.ChatCompletion,
            ModelId = "gpt-4o-mini",
            RequestedCapabilities = new List<string> { "chat" }
        });

        Assert.True(member.Allowed);
        Assert.False(viewer.Allowed);
    }
}
