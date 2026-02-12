using LeaseGate.Audit;
using LeaseGate.Client;
using LeaseGate.Hub;
using LeaseGate.Policy;
using LeaseGate.Protocol;
using LeaseGate.Providers;
using LeaseGate.Receipt;
using LeaseGate.Service;
using LeaseGate.Service.Tools;

var command = args.Length == 0 ? "simulate-all" : args[0];
var baseDir = AppContext.BaseDirectory;
var policyPath = Path.Combine(baseDir, "policy.json");
var auditDir = Path.Combine(baseDir, "audit");

var policyEngine = new PolicyEngine(policyPath, hotReload: true);
var auditWriter = new JsonlAuditWriter(auditDir);
var toolRegistry = new ToolRegistry(new[]
{
    new ToolDefinition { ToolId = "fs.read", Category = ToolCategory.FileRead },
    new ToolDefinition { ToolId = "fs.write", Category = ToolCategory.FileWrite },
    new ToolDefinition { ToolId = "net.fetch", Category = ToolCategory.NetworkRead },
    new ToolDefinition { ToolId = "net.post", Category = ToolCategory.NetworkWrite },
    new ToolDefinition { ToolId = "shell.exec", Category = ToolCategory.Exec }
});
var policy = policyEngine.CurrentSnapshot.Policy;
var options = new LeaseGovernorOptions
{
	PipeName = "leasegate-sample-pipe",
	LeaseTtl = TimeSpan.FromSeconds(4),
	MaxInFlight = policy.MaxInFlight,
	DailyBudgetCents = policy.DailyBudgetCents,
	MaxRequestsPerMinute = policy.MaxRequestsPerMinute,
	MaxTokensPerMinute = policy.MaxTokensPerMinute,
	MaxContextTokens = policy.MaxContextTokens,
	MaxRetrievedChunks = policy.MaxRetrievedChunks,
	MaxToolOutputTokens = policy.MaxToolOutputTokens,
	MaxToolCallsPerLease = policy.MaxToolCallsPerLease,
	MaxComputeUnits = policy.MaxComputeUnits
};

using var governor = new LeaseGovernor(options, policyEngine, auditWriter, toolRegistry);
using var server = new NamedPipeGovernorServer(governor, options.PipeName);
server.Start();

var client = new LeaseGateClient(new LeaseGateClientOptions
{
	PipeName = options.PipeName,
	FallbackMode = FallbackMode.Prod
});
var provider = new DeterministicFakeProviderAdapter();

switch (command)
{
	case "simulate-concurrency":
		await SimulateConcurrencyAsync(client);
		break;
	case "simulate-adapter":
		await SimulateAdapterCallAsync(client, provider);
		break;
	case "simulate-approval":
		await SimulateApprovalFlowAsync(client, provider);
		break;
	case "simulate-stress":
		await SimulateStressAsync(client, provider);
		break;
	case "simulate-high-cost":
		await SimulateHighCostAsync(client);
		break;
	case "simulate-all":
		await SimulateConcurrencyAsync(client);
		await SimulateAdapterCallAsync(client, provider);
		await SimulateApprovalFlowAsync(client, provider);
		await SimulateHighCostAsync(client);
		await SimulatePolicyGateAsync(client);
		await SimulateStressAsync(client, provider);
		break;
	case "daily-report":
		await PrintDailyReportAsync(policyPath);
		break;
	case "export-proof":
		await ExportProofAsync(auditDir, args);
		break;
	case "verify-receipt":
		await VerifyReceiptAsync(auditDir, args);
		break;
	default:
		Console.WriteLine("Commands: simulate-concurrency | simulate-adapter | simulate-approval | simulate-high-cost | simulate-stress | simulate-all | daily-report | export-proof | verify-receipt");
		break;
}

var metrics = await client.GetMetricsAsync(CancellationToken.None);
Console.WriteLine($"Metrics: active={metrics.ActiveLeases}, spendToday={metrics.SpendTodayCents}, denies={metrics.DeniesByReason.Values.Sum()}");
Console.WriteLine($"Audit files: {auditDir}");
await server.StopAsync();

static async Task SimulateConcurrencyAsync(LeaseGateClient client)
{
	Console.WriteLine("-- simulate 20 concurrent calls --");

	var tasks = Enumerable.Range(0, 20).Select(async i =>
	{
		var acquire = await client.AcquireAsync(new AcquireLeaseRequest
		{
			ActorId = "demo",
			WorkspaceId = "sample",
			ActionType = ActionType.ChatCompletion,
			ModelId = "gpt-4o-mini",
			ProviderId = "stub",
			EstimatedPromptTokens = 100,
			MaxOutputTokens = 100,
			EstimatedCostCents = 5,
			RequestedContextTokens = 100,
			RequestedRetrievedChunks = 2,
			EstimatedToolOutputTokens = 20,
			EstimatedComputeUnits = 1,
			RequestedCapabilities = new List<string> { "chat" },
			RequestedTools = new List<ToolIntent>(),
			RiskFlags = new List<string>(),
			IdempotencyKey = $"concurrency-{i}"
		}, CancellationToken.None);

		if (!acquire.Granted)
		{
			Console.WriteLine($"[{i}] denied: {acquire.DeniedReason} | rec: {acquire.Recommendation}");
			return;
		}

		await Task.Delay(300);
		await client.ReleaseAsync(new ReleaseLeaseRequest
		{
			LeaseId = acquire.LeaseId,
			ActualPromptTokens = 90,
			ActualOutputTokens = 60,
			ActualCostCents = 5,
			ToolCallsCount = 0,
			ToolCalls = new List<ToolCallUsage>(),
			BytesIn = 1024,
			BytesOut = 2048,
			LatencyMs = 250,
			ProviderErrorClassification = ProviderErrorClassification.None,
			Outcome = LeaseOutcome.Success,
			IdempotencyKey = $"release-concurrency-{i}"
		}, CancellationToken.None);
	});

	await Task.WhenAll(tasks);
}

static async Task SimulateAdapterCallAsync(LeaseGateClient client, IModelProvider provider)
{
	Console.WriteLine("-- simulate adapter governed call --");
	var result = await GovernedModelCall.ExecuteProviderCallAsync(
		client,
		provider,
		new ModelCallSpec
		{
			ProviderId = "fake",
			ModelId = "gpt-4o-mini",
			Prompt = "Summarize lease state in one sentence.",
			PromptTokens = 120,
			MaxOutputTokens = 80,
			Temperature = 0.1
		},
		new AcquireLeaseRequest
		{
			ActorId = "demo",
			WorkspaceId = "sample",
			ActionType = ActionType.ChatCompletion,
			ModelId = "gpt-4o-mini",
			ProviderId = "fake",
			EstimatedPromptTokens = 120,
			MaxOutputTokens = 80,
			RequestedContextTokens = 120,
			RequestedRetrievedChunks = 3,
			EstimatedToolOutputTokens = 0,
			EstimatedComputeUnits = 1,
			RequestedCapabilities = new List<string> { "chat" },
			RequestedTools = new List<ToolIntent>(),
			IdempotencyKey = $"adapter-{Guid.NewGuid():N}"
		},
		CancellationToken.None);

	Console.WriteLine($"adapter result: success={result.Success} prompt={result.ActualPromptTokens} output={result.ActualOutputTokens} cost={result.ActualCostCents}");
}

static async Task SimulateApprovalFlowAsync(LeaseGateClient client, IModelProvider provider)
{
	Console.WriteLine("-- simulate approval required flow --");
	var baseAcquire = new AcquireLeaseRequest
	{
		ActorId = "demo",
		WorkspaceId = "sample",
		ActionType = ActionType.ToolCall,
		ModelId = "gpt-4o-mini",
		ProviderId = "fake",
		EstimatedPromptTokens = 80,
		MaxOutputTokens = 40,
		RequestedContextTokens = 80,
		RequestedRetrievedChunks = 2,
		EstimatedToolOutputTokens = 30,
		EstimatedComputeUnits = 1,
		RequestedCapabilities = new List<string> { "chat", "read" },
		RequestedTools = new List<ToolIntent>
		{
			new() { ToolId = "net.post", Category = ToolCategory.NetworkWrite }
		},
		IdempotencyKey = $"approval-{Guid.NewGuid():N}"
	};

	try
	{
		await GovernedModelCall.ExecuteProviderCallAsync(
			client,
			provider,
			new ModelCallSpec
			{
				ProviderId = "fake",
				ModelId = "gpt-4o-mini",
				Prompt = "Run a risky network write tool call.",
				PromptTokens = 80,
				MaxOutputTokens = 40
			},
			baseAcquire,
			CancellationToken.None);
	}
	catch (ApprovalRequiredException ex)
	{
		Console.WriteLine($"approval blocked as expected: {ex.Message}");

		var req = await client.RequestApprovalAsync(new ApprovalRequest
		{
			ActorId = baseAcquire.ActorId,
			WorkspaceId = baseAcquire.WorkspaceId,
			Reason = "network write needed for integration",
			RequestedBy = "demo",
			ToolCategory = ToolCategory.NetworkWrite,
			TtlSeconds = 120,
			SingleUse = true,
			IdempotencyKey = $"approval-request-{Guid.NewGuid():N}"
		}, CancellationToken.None);

		var grant = await client.GrantApprovalAsync(new GrantApprovalRequest
		{
			ApprovalId = req.ApprovalId,
			GrantedBy = "admin",
			IdempotencyKey = $"approval-grant-{Guid.NewGuid():N}"
		}, CancellationToken.None);

		baseAcquire.ApprovalToken = grant.ApprovalToken;
		baseAcquire.IdempotencyKey = $"approval-retry-{Guid.NewGuid():N}";

		var allowed = await GovernedModelCall.ExecuteProviderCallAsync(
			client,
			provider,
			new ModelCallSpec
			{
				ProviderId = "fake",
				ModelId = "gpt-4o-mini",
				Prompt = "Retry risky tool call with approval token.",
				PromptTokens = 80,
				MaxOutputTokens = 40
			},
			baseAcquire,
			CancellationToken.None);

		Console.WriteLine($"approval retry success: {allowed.Success}");
	}
}

static async Task SimulateHighCostAsync(LeaseGateClient client)
{
	Console.WriteLine("-- simulate high cost call --");
	var response = await client.AcquireAsync(new AcquireLeaseRequest
	{
		ActorId = "demo",
		WorkspaceId = "sample",
		ActionType = ActionType.ChatCompletion,
		ModelId = "gpt-4o-mini",
		ProviderId = "stub",
		EstimatedPromptTokens = 2000,
		MaxOutputTokens = 2000,
		EstimatedCostCents = 10_000,
		RequestedContextTokens = 2000,
		RequestedRetrievedChunks = 3,
		EstimatedToolOutputTokens = 0,
		EstimatedComputeUnits = 1,
		RequestedCapabilities = new List<string> { "chat" },
		RequestedTools = new List<ToolIntent>(),
		RiskFlags = new List<string>(),
		IdempotencyKey = "high-cost"
	}, CancellationToken.None);

	Console.WriteLine(response.Granted
		? "unexpected grant"
		: $"denied: {response.DeniedReason} | rec: {response.Recommendation}");
}

static async Task SimulatePolicyGateAsync(LeaseGateClient client)
{
	Console.WriteLine("-- simulate policy model/capability gate --");
	var response = await client.AcquireAsync(new AcquireLeaseRequest
	{
		ActorId = "demo",
		WorkspaceId = "sample",
		ActionType = ActionType.ToolCall,
		ModelId = "blocked-model",
		ProviderId = "stub",
		EstimatedPromptTokens = 20,
		MaxOutputTokens = 20,
		EstimatedCostCents = 2,
		RequestedContextTokens = 20,
		RequestedRetrievedChunks = 1,
		EstimatedToolOutputTokens = 10,
		EstimatedComputeUnits = 1,
		RequestedCapabilities = new List<string> { "exec" },
		RequestedTools = new List<ToolIntent>
		{
			new() { ToolId = "shell.exec", Category = ToolCategory.Exec }
		},
		RiskFlags = new List<string>(),
		IdempotencyKey = "policy-deny"
	}, CancellationToken.None);

	Console.WriteLine(response.Granted
		? "unexpected grant"
		: $"denied: {response.DeniedReason} | rec: {response.Recommendation}");
}

static async Task SimulateStressAsync(LeaseGateClient client, IModelProvider provider)
{
	Console.WriteLine("-- simulate stress: 220 concurrent governed calls --");

	var tasks = Enumerable.Range(0, 220).Select(async i =>
	{
		var model = i % 35 == 0 ? "rate-limit-model" : "gpt-4o-mini";
		try
		{
			await GovernedModelCall.ExecuteProviderCallAsync(
				client,
				provider,
				new ModelCallSpec
				{
					ProviderId = "fake",
					ModelId = model,
					Prompt = $"stress request {i}",
					PromptTokens = 70 + (i % 40),
					MaxOutputTokens = 40
				},
				new AcquireLeaseRequest
				{
					ActorId = "stress",
					WorkspaceId = "sample",
					ActionType = ActionType.ChatCompletion,
					ModelId = model,
					ProviderId = "fake",
					EstimatedPromptTokens = 90,
					MaxOutputTokens = 40,
					RequestedContextTokens = 120,
					RequestedRetrievedChunks = 2,
					EstimatedToolOutputTokens = 0,
					EstimatedComputeUnits = 1,
					RequestedCapabilities = new List<string> { "chat" },
					RequestedTools = new List<ToolIntent>(),
					IdempotencyKey = $"stress-{i}-{Guid.NewGuid():N}"
				},
				CancellationToken.None);
		}
		catch
		{
		}
	});

	await Task.WhenAll(tasks);
	await Task.Delay(1200);

	var metrics = await client.GetMetricsAsync(CancellationToken.None);
	var topDenies = metrics.DeniesByReason
		.OrderByDescending(x => x.Value)
		.Take(5)
		.Select(x => $"{x.Key}={x.Value}");

	Console.WriteLine($"stress report: grants={metrics.GrantsByReason.Values.Sum()} denies={metrics.DeniesByReason.Values.Sum()} active={metrics.ActiveLeases}");
	Console.WriteLine($"top deny reasons: {string.Join(", ", topDenies)}");
}

static async Task PrintDailyReportAsync(string policyPath)
{
	Console.WriteLine("-- daily report --");
	using var hub = new HubControlPlane(new LeaseGovernorOptions
	{
		MaxInFlight = 4,
		DailyBudgetCents = 500,
		MaxRequestsPerMinute = 200,
		MaxTokensPerMinute = 20000,
		EnableDurableState = false
	}, policyPath);

	var acquire = await hub.AcquireAsync(new AcquireLeaseRequest
	{
		SessionId = Guid.NewGuid().ToString("N"),
		ClientInstanceId = "sample-cli",
		OrgId = "org-sample",
		ActorId = "demo",
		WorkspaceId = "sample",
		PrincipalType = PrincipalType.Human,
		Role = Role.Member,
		ActionType = ActionType.ChatCompletion,
		ModelId = "gpt-4o-mini",
		ProviderId = "fake",
		EstimatedPromptTokens = 20,
		MaxOutputTokens = 20,
		EstimatedCostCents = 2,
		RequestedCapabilities = new List<string> { "chat" },
		RequestedTools = new List<ToolIntent>(),
		IdempotencyKey = "daily-report-seed"
	}, CancellationToken.None);

	if (acquire.Granted)
	{
		await hub.ReleaseAsync(new ReleaseLeaseRequest
		{
			LeaseId = acquire.LeaseId,
			ActualPromptTokens = 10,
			ActualOutputTokens = 10,
			ActualCostCents = 2,
			Outcome = LeaseOutcome.Success,
			IdempotencyKey = "daily-report-release"
		}, CancellationToken.None);
	}

	Console.WriteLine(hub.PrintDailyReport());
}

static async Task ExportProofAsync(string auditDir, string[] cliArgs)
{
	var service = new GovernanceReceiptService();
	var auditFile = GetLatestAuditFile(auditDir);
	if (string.IsNullOrWhiteSpace(auditFile))
	{
		Console.WriteLine("no audit file found");
		return;
	}

	var from = 1;
	var to = File.ReadAllLines(auditFile).Length;
	var fromArg = cliArgs.FirstOrDefault(a => a.StartsWith("--from", StringComparison.OrdinalIgnoreCase));
	if (!string.IsNullOrWhiteSpace(fromArg))
	{
		var range = fromArg.Split('=', 2).LastOrDefault() ?? string.Empty;
		var parts = range.Split('-', 2, StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length == 2 && int.TryParse(parts[0], out var parsedFrom) && int.TryParse(parts[1], out var parsedTo))
		{
			from = parsedFrom;
			to = parsedTo;
		}
	}

	var bundle = service.ExportProof(auditFile, from, to, "policy-bundle-hash-local", "local-signature-info");
	var output = Path.Combine(auditDir, $"governance-receipt-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.json");
	service.SaveBundle(bundle, output);

	Console.WriteLine($"proof exported: {output}");
	await Task.CompletedTask;
}

static async Task VerifyReceiptAsync(string auditDir, string[] cliArgs)
{
	var service = new GovernanceReceiptService();
	var receiptPath = cliArgs.Length > 1 ? cliArgs[1] : string.Empty;
	if (string.IsNullOrWhiteSpace(receiptPath) || !File.Exists(receiptPath))
	{
		Console.WriteLine("usage: verify-receipt <file>");
		return;
	}

	var auditFile = GetLatestAuditFile(auditDir);
	if (string.IsNullOrWhiteSpace(auditFile))
	{
		Console.WriteLine("no audit file found");
		return;
	}

	var result = service.VerifyReceipt(receiptPath, auditFile);
	Console.WriteLine(result.Valid ? "receipt valid" : $"receipt invalid: {result.Message}");
	await Task.CompletedTask;
}

static string GetLatestAuditFile(string auditDir)
{
	if (!Directory.Exists(auditDir))
	{
		return string.Empty;
	}

	return Directory.GetFiles(auditDir, "*.jsonl")
		.OrderByDescending(File.GetLastWriteTimeUtc)
		.FirstOrDefault() ?? string.Empty;
}
