using LeaseGate.Client;
using LeaseGate.Protocol;
using LeaseGate.Service;
using LeaseGate.Service.Tools;

namespace LeaseGate.Tests;

public class ThreadSafetyTests
{
    [Fact]
    public void ToolRegistry_ConcurrentRegisterAndGet()
    {
        var registry = new ToolRegistry();
        var exceptions = new List<Exception>();

        var tasks = Enumerable.Range(0, 10).Select(i => Task.Run(() =>
        {
            try
            {
                for (var j = 0; j < 100; j++)
                {
                    registry.Register(new ToolDefinition
                    {
                        ToolId = $"tool-{i}-{j}",
                        Category = ToolCategory.FileRead
                    });

                    registry.TryGet($"tool-{i}-{j}", out _);
                }
            }
            catch (Exception ex)
            {
                lock (exceptions) exceptions.Add(ex);
            }
        })).ToArray();

        Task.WaitAll(tasks);
        Assert.Empty(exceptions);

        // All tools should be registered
        var all = registry.GetAll();
        Assert.Equal(1000, all.Count);
    }

    [Fact]
    public void ToolRegistry_ConcurrentGetAll_ReturnsConsistentSnapshot()
    {
        var registry = new ToolRegistry(Enumerable.Range(0, 50).Select(i =>
            new ToolDefinition { ToolId = $"seed-{i}", Category = ToolCategory.NetworkRead }));

        var snapshots = new List<int>();
        var exceptions = new List<Exception>();

        var tasks = Enumerable.Range(0, 20).Select(i => Task.Run(() =>
        {
            try
            {
                registry.Register(new ToolDefinition { ToolId = $"new-{i}", Category = ToolCategory.Exec });
                var all = registry.GetAll();
                lock (snapshots) snapshots.Add(all.Count);
            }
            catch (Exception ex)
            {
                lock (exceptions) exceptions.Add(ex);
            }
        })).ToArray();

        Task.WaitAll(tasks);
        Assert.Empty(exceptions);
        // All snapshots should be >= 50 (seed) and each should be a valid count
        Assert.All(snapshots, count => Assert.True(count >= 50));
    }

    [Fact]
    public void LeaseGateClient_ConcurrentLocalLeases_NoLeaks()
    {
        var client = new LeaseGateClient(new LeaseGateClientOptions
        {
            PipeName = "nonexistent-pipe-" + Guid.NewGuid().ToString("N"),
            FallbackMode = FallbackMode.Dev,
            DevMaxOutputTokens = 500
        });

        var leaseIds = new System.Collections.Concurrent.ConcurrentBag<string>();
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        // Acquire 20 leases in parallel (will hit fallback since pipe doesn't exist)
        var acquireTasks = Enumerable.Range(0, 20).Select(i => Task.Run(async () =>
        {
            try
            {
                var response = await client.AcquireAsync(new AcquireLeaseRequest
                {
                    ActorId = $"actor-{i}",
                    WorkspaceId = "ws",
                    ActionType = ActionType.ChatCompletion,
                    ModelId = "gpt-4o-mini",
                    MaxOutputTokens = 100,
                    EstimatedCostCents = 1,
                    RequestedCapabilities = new() { "chat" },
                    IdempotencyKey = $"concurrent-{i}"
                }, CancellationToken.None);

                if (response.Granted)
                    leaseIds.Add(response.LeaseId);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        })).ToArray();

        Task.WaitAll(acquireTasks);
        Assert.Empty(exceptions);
        Assert.Equal(20, leaseIds.Count);

        // Release all in parallel
        var releaseTasks = leaseIds.Select(id => Task.Run(async () =>
        {
            try
            {
                await client.ReleaseAsync(new ReleaseLeaseRequest
                {
                    LeaseId = id,
                    Outcome = LeaseOutcome.Success,
                    IdempotencyKey = $"rel-{id}"
                }, CancellationToken.None);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        })).ToArray();

        Task.WaitAll(releaseTasks);
        Assert.Empty(exceptions);
    }

    [Fact]
    public async Task NamedPipeServer_MultipleConcurrentClients_AllServed()
    {
        // This test verifies the server can accept multiple connections concurrently.
        // We test through the governor pipe server with real pipes.
        var pipeName = $"leasegate-test-{Guid.NewGuid():N}";
        var policyJson = ProtocolJson.Serialize(new Policy.LeaseGatePolicy
        {
            PolicyVersion = "pipe-test",
            MaxInFlight = 10,
            DailyBudgetCents = 1000,
            AllowedModels = new List<string> { "gpt-4o-mini" },
            AllowedCapabilities = new Dictionary<ActionType, List<string>>
            {
                [ActionType.ChatCompletion] = new() { "chat" }
            },
            DeniedToolCategories = new(),
            ApprovalRequiredToolCategories = new(),
            RiskRequiresApproval = new()
        });

        var policyPath = Path.Combine(Path.GetTempPath(), $"pipe-test-{Guid.NewGuid():N}.json");
        File.WriteAllText(policyPath, policyJson);

        try
        {
            var policy = new Policy.PolicyEngine(policyPath, hotReload: false);
            var audit = new NoopAuditWriter();
            using var governor = new LeaseGovernor(new LeaseGovernorOptions
            {
                MaxInFlight = 10, DailyBudgetCents = 1000,
                LeaseTtl = TimeSpan.FromSeconds(30),
                MaxRequestsPerMinute = 100, MaxTokensPerMinute = 10000,
                MaxContextTokens = 400, MaxRetrievedChunks = 8,
                MaxToolOutputTokens = 200, MaxToolCallsPerLease = 3, MaxComputeUnits = 5,
                EnableDurableState = false
            }, policy, audit);

            using var server = new Service.NamedPipeGovernorServer(governor, pipeName);
            server.Start();

            // 5 concurrent clients
            var clientTasks = Enumerable.Range(0, 5).Select(i => Task.Run(async () =>
            {
                var client = new LeaseGateClient(new LeaseGateClientOptions { PipeName = pipeName });
                var status = await client.GetStatusAsync(CancellationToken.None);
                return status.Healthy;
            })).ToArray();

            var results = await Task.WhenAll(clientTasks);
            Assert.All(results, healthy => Assert.True(healthy));

            await server.StopAsync();
        }
        finally
        {
            File.Delete(policyPath);
        }
    }

    private sealed class NoopAuditWriter : Audit.IAuditWriter
    {
        public Task<Audit.AuditWriteResult> WriteAsync(Audit.AuditEvent auditEvent, CancellationToken cancellationToken)
            => Task.FromResult(new Audit.AuditWriteResult());
    }
}
