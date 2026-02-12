using LeaseGate.Policy;
using LeaseGate.Protocol;

namespace LeaseGate.Tests;

public class TokenHashTests
{
    [Fact]
    public void HashToken_ReturnsDeterministicSha256()
    {
        var hash1 = ServiceAccountPolicy.HashToken("my-secret-token");
        var hash2 = ServiceAccountPolicy.HashToken("my-secret-token");
        Assert.Equal(hash1, hash2);

        var different = ServiceAccountPolicy.HashToken("other-token");
        Assert.NotEqual(hash1, different);
    }

    [Fact]
    public void HashToken_ReturnLowercaseHex()
    {
        var hash = ServiceAccountPolicy.HashToken("test-token");
        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, hash.ToLowerInvariant());
        Assert.True(hash.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')));
    }

    [Fact]
    public void TryResolveServiceAccount_MatchesByTokenHash()
    {
        var plainToken = "svc-secret-123";
        var hash = ServiceAccountPolicy.HashToken(plainToken);
        var policyJson = ProtocolJson.Serialize(new LeaseGatePolicy
        {
            PolicyVersion = "token-hash-test",
            AllowedModels = new List<string> { "gpt-4o-mini" },
            ServiceAccounts = new List<ServiceAccountPolicy>
            {
                new()
                {
                    Name = "svc-hashed",
                    TokenHash = hash,
                    OrgId = "org-a",
                    WorkspaceId = "ws-a",
                    Role = Role.ServiceAccount
                }
            }
        });

        var path = Path.Combine(Path.GetTempPath(), $"token-hash-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, policyJson);
        try
        {
            var engine = new PolicyEngine(path, hotReload: false);
            var found = engine.TryResolveServiceAccount(plainToken, "org-a", "ws-a", out var account);
            Assert.True(found);
            Assert.NotNull(account);
            Assert.Equal("svc-hashed", account!.Name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryResolveServiceAccount_FallsBackToPlaintextToken()
    {
        var policyJson = ProtocolJson.Serialize(new LeaseGatePolicy
        {
            PolicyVersion = "token-plain-test",
            AllowedModels = new List<string> { "gpt-4o-mini" },
            ServiceAccounts = new List<ServiceAccountPolicy>
            {
                new()
                {
                    Name = "svc-plain",
                    Token = "plain-text-token",
                    OrgId = "org-b",
                    WorkspaceId = "ws-b",
                    Role = Role.ServiceAccount
                }
            }
        });

        var path = Path.Combine(Path.GetTempPath(), $"token-plain-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, policyJson);
        try
        {
            var engine = new PolicyEngine(path, hotReload: false);
            var found = engine.TryResolveServiceAccount("plain-text-token", "org-b", "ws-b", out var account);
            Assert.True(found);
            Assert.NotNull(account);
            Assert.Equal("svc-plain", account!.Name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryResolveServiceAccount_RejectsWrongToken()
    {
        var hash = ServiceAccountPolicy.HashToken("correct-token");
        var policyJson = ProtocolJson.Serialize(new LeaseGatePolicy
        {
            PolicyVersion = "token-reject-test",
            ServiceAccounts = new List<ServiceAccountPolicy>
            {
                new()
                {
                    Name = "svc-reject",
                    TokenHash = hash,
                    OrgId = "org-c",
                    WorkspaceId = "ws-c",
                    Role = Role.ServiceAccount
                }
            }
        });

        var path = Path.Combine(Path.GetTempPath(), $"token-reject-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, policyJson);
        try
        {
            var engine = new PolicyEngine(path, hotReload: false);
            var found = engine.TryResolveServiceAccount("wrong-token", "org-c", "ws-c", out _);
            Assert.False(found);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryResolveServiceAccount_RejectsWrongOrgOrWorkspace()
    {
        var hash = ServiceAccountPolicy.HashToken("right-token");
        var policyJson = ProtocolJson.Serialize(new LeaseGatePolicy
        {
            PolicyVersion = "token-scope-test",
            ServiceAccounts = new List<ServiceAccountPolicy>
            {
                new()
                {
                    Name = "svc-scoped",
                    TokenHash = hash,
                    OrgId = "org-d",
                    WorkspaceId = "ws-d",
                    Role = Role.ServiceAccount
                }
            }
        });

        var path = Path.Combine(Path.GetTempPath(), $"token-scope-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, policyJson);
        try
        {
            var engine = new PolicyEngine(path, hotReload: false);
            Assert.False(engine.TryResolveServiceAccount("right-token", "wrong-org", "ws-d", out _));
            Assert.False(engine.TryResolveServiceAccount("right-token", "org-d", "wrong-ws", out _));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
