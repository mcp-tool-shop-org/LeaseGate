using LeaseGate.Audit;
using LeaseGate.Client;
using LeaseGate.Policy;
using LeaseGate.Protocol;
using LeaseGate.Service;

var command = args.Length == 0 ? "simulate-all" : args[0];
var baseDir = AppContext.BaseDirectory;
var policyPath = Path.Combine(baseDir, "policy.json");
var auditDir = Path.Combine(baseDir, "audit");

var policyEngine = new PolicyEngine(policyPath, hotReload: true);
var auditWriter = new JsonlAuditWriter(auditDir);
var options = new LeaseGovernorOptions
{
	PipeName = "leasegate-sample-pipe",
	LeaseTtl = TimeSpan.FromSeconds(4),
	MaxInFlight = policyEngine.CurrentSnapshot.Policy.MaxInFlight,
	DailyBudgetCents = policyEngine.CurrentSnapshot.Policy.DailyBudgetCents
};

using var governor = new LeaseGovernor(options, policyEngine, auditWriter);
using var server = new NamedPipeGovernorServer(governor, options.PipeName);
server.Start();

var client = new LeaseGateClient(new LeaseGateClientOptions
{
	PipeName = options.PipeName,
	FallbackMode = FallbackMode.Prod
});

switch (command)
{
	case "simulate-concurrency":
		await SimulateConcurrencyAsync(client);
		break;
	case "simulate-high-cost":
		await SimulateHighCostAsync(client);
		break;
	case "simulate-all":
		await SimulateConcurrencyAsync(client);
		await SimulateHighCostAsync(client);
		await SimulatePolicyGateAsync(client);
		break;
	default:
		Console.WriteLine("Commands: simulate-concurrency | simulate-high-cost | simulate-all");
		break;
}

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
			RequestedCapabilities = new List<string> { "chat" },
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
			BytesIn = 1024,
			BytesOut = 2048,
			Outcome = LeaseOutcome.Success,
			IdempotencyKey = $"release-concurrency-{i}"
		}, CancellationToken.None);
	});

	await Task.WhenAll(tasks);
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
		RequestedCapabilities = new List<string> { "chat" },
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
		RequestedCapabilities = new List<string> { "exec" },
		RiskFlags = new List<string>(),
		IdempotencyKey = "policy-deny"
	}, CancellationToken.None);

	Console.WriteLine(response.Granted
		? "unexpected grant"
		: $"denied: {response.DeniedReason} | rec: {response.Recommendation}");
}
