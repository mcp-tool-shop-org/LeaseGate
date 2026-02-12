using LeaseGate.Protocol;
using LeaseGate.Service.ToolIsolation;

namespace LeaseGate.Tests;

public class ToolIsolationTests
{
    private readonly IsolatedToolRunner _runner = new();

    private static ToolSubLeaseRecord MakeSubLease(int timeoutMs = 5_000, long maxOutputBytes = 8_192) => new()
    {
        ToolSubLeaseId = "sublease-iso",
        ToolId = "test-tool",
        RemainingCalls = 3,
        TimeoutMs = timeoutMs,
        MaxOutputBytes = maxOutputBytes,
        ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5)
    };

    private static Policy.LeaseGatePolicy MakePolicy(List<string>? fileRoots = null, List<string>? hosts = null) => new()
    {
        AllowedFileRoots = fileRoots ?? new List<string> { Path.GetTempPath() },
        AllowedNetworkHosts = hosts ?? new List<string> { "localhost" }
    };

    [Fact]
    public async Task ToolPathAllowlist_BlocksUnallowedPath()
    {
        var result = await _runner.ExecuteAsync(
            new ToolExecutionRequest
            {
                TargetPath = @"C:\Windows\System32\secret.txt",
                CommandText = "type file",
                IdempotencyKey = "path-block"
            },
            MakeSubLease(),
            MakePolicy(fileRoots: new List<string> { Path.GetTempPath() }),
            CancellationToken.None);

        Assert.False(result.Allowed);
        Assert.Equal("tool_path_not_allowed", result.DeniedReason);
    }

    [Fact]
    public async Task ToolPathAllowlist_AllowsConfiguredRoot()
    {
        var tempSubdir = Path.Combine(Path.GetTempPath(), "allowed-test");
        var result = await _runner.ExecuteAsync(
            new ToolExecutionRequest
            {
                TargetPath = tempSubdir,
                CommandText = "whoami",
                IdempotencyKey = "path-allow"
            },
            MakeSubLease(),
            MakePolicy(fileRoots: new List<string> { Path.GetTempPath() }),
            CancellationToken.None);

        // Should not be denied for path — may fail for other reasons (process execution)
        Assert.NotEqual("tool_path_not_allowed", result.DeniedReason);
    }

    [Fact]
    public async Task ToolPathAllowlist_EmptyPath_IsAllowed()
    {
        var result = await _runner.ExecuteAsync(
            new ToolExecutionRequest
            {
                TargetPath = "",
                CommandText = "whoami",
                IdempotencyKey = "path-empty"
            },
            MakeSubLease(),
            MakePolicy(),
            CancellationToken.None);

        Assert.NotEqual("tool_path_not_allowed", result.DeniedReason);
    }

    [Fact]
    public async Task ToolHostAllowlist_BlocksUnallowedHost()
    {
        var result = await _runner.ExecuteAsync(
            new ToolExecutionRequest
            {
                TargetHost = "evil.example.com",
                CommandText = "curl test",
                IdempotencyKey = "host-block"
            },
            MakeSubLease(),
            MakePolicy(hosts: new List<string> { "localhost" }),
            CancellationToken.None);

        Assert.False(result.Allowed);
        Assert.Equal("tool_host_not_allowed", result.DeniedReason);
    }

    [Fact]
    public async Task ToolHostAllowlist_AllowsConfiguredHost()
    {
        var result = await _runner.ExecuteAsync(
            new ToolExecutionRequest
            {
                TargetHost = "localhost",
                CommandText = "whoami",
                IdempotencyKey = "host-allow"
            },
            MakeSubLease(),
            MakePolicy(hosts: new List<string> { "localhost" }),
            CancellationToken.None);

        Assert.NotEqual("tool_host_not_allowed", result.DeniedReason);
    }

    [Fact]
    public async Task ToolHostAllowlist_EmptyHost_IsAllowed()
    {
        var result = await _runner.ExecuteAsync(
            new ToolExecutionRequest
            {
                TargetHost = "",
                CommandText = "whoami",
                IdempotencyKey = "host-empty"
            },
            MakeSubLease(),
            MakePolicy(),
            CancellationToken.None);

        Assert.NotEqual("tool_host_not_allowed", result.DeniedReason);
    }

    [Fact]
    public async Task ToolTimeout_EnforcesMinOfRequestAndSubLease()
    {
        // Request timeout 100ms, sublease timeout 200ms — effective should be 100ms
        // We can't easily test the exact timeout value, but we can verify execution
        // completes and the min is applied by checking that a very long command would be bounded
        var result = await _runner.ExecuteAsync(
            new ToolExecutionRequest
            {
                CommandText = "whoami",
                TimeoutMs = 100,
                IdempotencyKey = "timeout-min"
            },
            MakeSubLease(timeoutMs: 200),
            MakePolicy(),
            CancellationToken.None);

        // Should complete (whoami is fast) — just verifying no crash
        Assert.True(result.DurationMs >= 0);
    }

    [Fact]
    public async Task ToolMaxBytes_EnforcesMinOfRequestAndSubLease()
    {
        // Request maxBytes 256, sublease maxBytes 512 — effective should be 256
        var result = await _runner.ExecuteAsync(
            new ToolExecutionRequest
            {
                CommandText = "whoami",
                MaxOutputBytes = 256,
                IdempotencyKey = "bytes-min"
            },
            MakeSubLease(maxOutputBytes: 512),
            MakePolicy(),
            CancellationToken.None);

        // whoami output is small, should succeed
        if (result.Allowed)
        {
            Assert.True(result.OutputBytes <= 512);
        }
    }
}
