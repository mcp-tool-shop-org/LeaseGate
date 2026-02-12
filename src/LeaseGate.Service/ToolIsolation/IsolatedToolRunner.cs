using System.Diagnostics;
using LeaseGate.Protocol;

namespace LeaseGate.Service.ToolIsolation;

public sealed class IsolatedToolRunner
{
    private static readonly char[] BlockedMetacharacters = ['&', '|', '>', '<', ';', '`', '$', '(', ')', '{', '}', '%', '^', '!'];

    public async Task<ToolExecutionResponse> ExecuteAsync(ToolExecutionRequest request, ToolSubLeaseRecord subLease, LeaseGate.Policy.LeaseGatePolicy policy, CancellationToken cancellationToken)
    {
        if (!IsPathAllowed(request.TargetPath, policy.AllowedFileRoots))
        {
            return Denied(request, "tool_path_not_allowed", "use an allowed file root");
        }

        if (!IsHostAllowed(request.TargetHost, policy.AllowedNetworkHosts))
        {
            return Denied(request, "tool_host_not_allowed", "use an allowed network host");
        }

        if (!ValidateCommandText(request.CommandText))
        {
            return Denied(request, "tool_command_rejected", "command contains blocked shell metacharacters");
        }

        var effectiveTimeout = Math.Min(Math.Max(100, request.TimeoutMs), Math.Max(100, subLease.TimeoutMs));
        var effectiveMaxBytes = Math.Min(Math.Max(256, request.MaxOutputBytes), Math.Max(256, subLease.MaxOutputBytes));

        var (fileName, arguments) = ParseCommand(request.CommandText);

        var start = Stopwatch.StartNew();
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(effectiveTimeout);

        var output = await process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var error = await process.StandardError.ReadToEndAsync(timeoutCts.Token);
        await process.WaitForExitAsync(timeoutCts.Token);

        var merged = string.IsNullOrWhiteSpace(error) ? output : output + Environment.NewLine + error;
        var bytes = System.Text.Encoding.UTF8.GetByteCount(merged);
        if (bytes > effectiveMaxBytes)
        {
            return new ToolExecutionResponse
            {
                Allowed = false,
                Outcome = LeaseOutcome.ToolError,
                DeniedReason = "tool_output_bytes_exceeded",
                Recommendation = "reduce output or increase approved max bytes",
                DurationMs = start.ElapsedMilliseconds,
                OutputBytes = bytes,
                IdempotencyKey = request.IdempotencyKey
            };
        }

        return new ToolExecutionResponse
        {
            Allowed = true,
            Outcome = LeaseOutcome.Success,
            OutputBytes = bytes,
            DurationMs = start.ElapsedMilliseconds,
            OutputPreview = merged.Length > 256 ? merged[..256] : merged,
            Recommendation = "ok",
            IdempotencyKey = request.IdempotencyKey
        };
    }

    private static bool IsPathAllowed(string path, IEnumerable<string> roots)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        var normalized = Path.GetFullPath(path);
        var allowed = roots.Any(root =>
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                return false;
            }

            var allowedRoot = Path.GetFullPath(root);
            return normalized.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase);
        });

        return allowed;
    }

    private static bool IsHostAllowed(string host, IEnumerable<string> allowlist)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return true;
        }

        return allowlist.Any(allowed => host.Equals(allowed, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ValidateCommandText(string commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return false;
        }

        return commandText.IndexOfAny(BlockedMetacharacters) < 0;
    }

    private static (string FileName, string Arguments) ParseCommand(string commandText)
    {
        var trimmed = commandText.Trim();
        var spaceIndex = trimmed.IndexOf(' ');
        if (spaceIndex < 0)
        {
            return (trimmed, string.Empty);
        }

        return (trimmed[..spaceIndex], trimmed[(spaceIndex + 1)..]);
    }

    private static ToolExecutionResponse Denied(ToolExecutionRequest request, string reason, string recommendation)
    {
        return new ToolExecutionResponse
        {
            Allowed = false,
            Outcome = LeaseOutcome.PolicyDenied,
            DeniedReason = reason,
            Recommendation = recommendation,
            IdempotencyKey = request.IdempotencyKey
        };
    }
}
