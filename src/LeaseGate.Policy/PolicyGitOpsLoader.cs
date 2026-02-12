using System.Collections;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using LeaseGate.Protocol;

namespace LeaseGate.Policy;

public static class PolicyGitOpsLoader
{
    public static LeaseGatePolicy LoadFromDirectory(string policyDirectory)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        var org = DeserializeYaml(deserializer, Path.Combine(policyDirectory, "org.yml"));
        var models = DeserializeYaml(deserializer, Path.Combine(policyDirectory, "models.yml"));
        var tools = DeserializeYaml(deserializer, Path.Combine(policyDirectory, "tools.yml"));

        var workspacesDir = Path.Combine(policyDirectory, "workspaces");
        var workspaceFiles = Directory.Exists(workspacesDir)
            ? Directory.GetFiles(workspacesDir, "*.yml", SearchOption.TopDirectoryOnly)
            : Array.Empty<string>();

        var policy = new LeaseGatePolicy
        {
            PolicyVersion = StringValue(org, "version", "local"),
            DailyBudgetCents = IntValue(org, "defaults.dailyBudgetCents", 500),
            MaxInFlight = IntValue(org, "defaults.maxInFlight", 4),
            MaxRequestsPerMinute = IntValue(org, "defaults.maxRequestsPerMinute", 120),
            MaxTokensPerMinute = IntValue(org, "defaults.maxTokensPerMinute", 250_000),
            MaxContextTokens = IntValue(org, "defaults.maxContextTokens", 16_000),
            MaxRetrievedChunks = IntValue(org, "defaults.maxRetrievedChunks", 40),
            MaxToolOutputTokens = IntValue(org, "defaults.maxToolOutputTokens", 4_000),
            MaxToolCallsPerLease = IntValue(org, "defaults.maxToolCallsPerLease", 6),
            MaxComputeUnits = IntValue(org, "defaults.maxComputeUnits", 8),
            OrgDailyBudgetCents = IntValue(org, "defaults.dailyBudgetCents", 500),
            OrgMaxRequestsPerMinute = IntValue(org, "defaults.maxRequestsPerMinute", 120),
            OrgMaxTokensPerMinute = IntValue(org, "defaults.maxTokensPerMinute", 250_000),
            AllowedModels = StringList(models, "allowedModels"),
            DeniedToolCategories = ToolCategoryList(tools, "deniedToolCategories"),
            ApprovalRequiredToolCategories = ToolCategoryList(tools, "approvalRequiredToolCategories"),
            ApprovalReviewersByToolCategory = ToolCategoryIntMap(tools, "approvalReviewersByToolCategory"),
            AllowedModelsByWorkspace = StringListMap(models, "workspaceModelOverrides"),
            AllowedToolsByWorkspaceRole = StringListMap(tools, "allowedToolsByWorkspaceRole")
        };

        foreach (var workspaceFile in workspaceFiles)
        {
            var ws = DeserializeYaml(deserializer, workspaceFile);
            var workspaceId = StringValue(ws, "workspaceId", string.Empty);
            if (string.IsNullOrWhiteSpace(workspaceId))
            {
                continue;
            }

            policy.WorkspaceDailyBudgetCents[workspaceId] = IntValue(ws, "dailyBudgetCents", policy.DailyBudgetCents);
            policy.WorkspaceMaxRequestsPerMinute[workspaceId] = IntValue(ws, "maxRequestsPerMinute", policy.MaxRequestsPerMinute);
            policy.WorkspaceMaxTokensPerMinute[workspaceId] = IntValue(ws, "maxTokensPerMinute", policy.MaxTokensPerMinute);

            var capabilitiesByRole = NestedCapabilitiesByRole(ws, "allowedCapabilitiesByRole");
            foreach (var roleEntry in capabilitiesByRole)
            {
                if (!policy.AllowedCapabilitiesByRole.TryGetValue(roleEntry.Key, out var existing))
                {
                    policy.AllowedCapabilitiesByRole[roleEntry.Key] = roleEntry.Value;
                }
                else
                {
                    foreach (var actionEntry in roleEntry.Value)
                    {
                        existing[actionEntry.Key] = actionEntry.Value;
                    }
                }
            }
        }

        if (policy.AllowedCapabilitiesByRole.TryGetValue(Role.Member, out var memberCaps))
        {
            policy.AllowedCapabilities = memberCaps.ToDictionary(k => k.Key, v => v.Value);
        }

        return policy;
    }

    public static List<string> Lint(LeaseGatePolicy policy)
    {
        var errors = new List<string>();

        if (policy.DailyBudgetCents <= 0)
        {
            errors.Add("daily budget must be greater than zero");
        }

        if (policy.MaxInFlight <= 0)
        {
            errors.Add("maxInFlight must be greater than zero");
        }

        if (policy.MaxRequestsPerMinute <= 0 || policy.MaxTokensPerMinute <= 0)
        {
            errors.Add("rate limits must be greater than zero");
        }

        if (policy.AllowedModels.Count == 0)
        {
            errors.Add("at least one allowed model is required");
        }

        if (!policy.DeniedToolCategories.Contains(ToolCategory.NetworkWrite) || !policy.DeniedToolCategories.Contains(ToolCategory.Exec))
        {
            errors.Add("deny-by-default requires denied categories to include networkWrite and exec");
        }

        foreach (var (key, value) in policy.ApprovalReviewersByToolCategory)
        {
            if (value <= 0)
            {
                errors.Add($"approval reviewers for {key} must be >= 1");
            }
        }

        return errors;
    }

    private static Dictionary<object, object> DeserializeYaml(IDeserializer deserializer, string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<object, object>();
        }

        var raw = File.ReadAllText(path);
        var data = deserializer.Deserialize<Dictionary<object, object>>(raw);
        return data ?? new Dictionary<object, object>();
    }

    private static object? ResolvePath(Dictionary<object, object> map, string dottedPath)
    {
        var parts = dottedPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
        object? current = map;
        foreach (var part in parts)
        {
            if (current is Dictionary<object, object> dict && dict.TryGetValue(part, out var next))
            {
                current = next;
                continue;
            }

            return null;
        }

        return current;
    }

    private static string StringValue(Dictionary<object, object> map, string path, string fallback)
    {
        var value = ResolvePath(map, path)?.ToString();
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static int IntValue(Dictionary<object, object> map, string path, int fallback)
    {
        var value = ResolvePath(map, path)?.ToString();
        return int.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static List<string> StringList(Dictionary<object, object> map, string path)
    {
        var resolved = ResolvePath(map, path);
        if (resolved is not IEnumerable seq)
        {
            return new List<string>();
        }

        return seq.Cast<object>().Select(item => item.ToString() ?? string.Empty).Where(item => !string.IsNullOrWhiteSpace(item)).ToList();
    }

    private static Dictionary<string, List<string>> StringListMap(Dictionary<object, object> map, string path)
    {
        var resolved = ResolvePath(map, path);
        if (resolved is not Dictionary<object, object> dict)
        {
            return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        }

        return dict.ToDictionary(
            key => key.Key.ToString() ?? string.Empty,
            val =>
            {
                if (val.Value is IEnumerable seq)
                {
                    return seq.Cast<object>().Select(item => item.ToString() ?? string.Empty).Where(item => !string.IsNullOrWhiteSpace(item)).ToList();
                }

                return new List<string>();
            },
            StringComparer.OrdinalIgnoreCase);
    }

    private static List<ToolCategory> ToolCategoryList(Dictionary<object, object> map, string path)
    {
        return StringList(map, path)
            .Select(value => Enum.TryParse<ToolCategory>(value, true, out var parsed) ? parsed : ToolCategory.Other)
            .ToList();
    }

    private static Dictionary<ToolCategory, int> ToolCategoryIntMap(Dictionary<object, object> map, string path)
    {
        var resolved = ResolvePath(map, path);
        if (resolved is not Dictionary<object, object> dict)
        {
            return new Dictionary<ToolCategory, int>();
        }

        var result = new Dictionary<ToolCategory, int>();
        foreach (var (key, value) in dict)
        {
            if (!Enum.TryParse<ToolCategory>(key.ToString(), true, out var category))
            {
                continue;
            }

            if (int.TryParse(value?.ToString(), out var count))
            {
                result[category] = count;
            }
        }

        return result;
    }

    private static Dictionary<Role, Dictionary<ActionType, List<string>>> NestedCapabilitiesByRole(Dictionary<object, object> map, string path)
    {
        var resolved = ResolvePath(map, path);
        if (resolved is not Dictionary<object, object> byRole)
        {
            return new Dictionary<Role, Dictionary<ActionType, List<string>>>();
        }

        var output = new Dictionary<Role, Dictionary<ActionType, List<string>>>();
        foreach (var roleEntry in byRole)
        {
            if (!Enum.TryParse<Role>(roleEntry.Key.ToString(), true, out var role))
            {
                continue;
            }

            if (roleEntry.Value is not Dictionary<object, object> actionMap)
            {
                continue;
            }

            var actions = new Dictionary<ActionType, List<string>>();
            foreach (var actionEntry in actionMap)
            {
                if (!Enum.TryParse<ActionType>(actionEntry.Key.ToString(), true, out var actionType))
                {
                    continue;
                }

                if (actionEntry.Value is IEnumerable seq)
                {
                    actions[actionType] = seq.Cast<object>()
                        .Select(item => item.ToString() ?? string.Empty)
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .ToList();
                }
            }

            output[role] = actions;
        }

        return output;
    }
}
