using System.Collections.Concurrent;
using LeaseGate.Protocol;

namespace LeaseGate.Service.Tools;

public sealed class ToolDefinition
{
    public string ToolId { get; init; } = string.Empty;
    public ToolCategory Category { get; init; }
    public int FixedCostWeight { get; init; } = 1;
    public double VariableCostWeight { get; init; } = 1.0;
}

public sealed class ToolRegistry
{
    private readonly ConcurrentDictionary<string, ToolDefinition> _tools;

    public ToolRegistry(IEnumerable<ToolDefinition>? seed = null)
    {
        _tools = new ConcurrentDictionary<string, ToolDefinition>(
            (seed ?? Array.Empty<ToolDefinition>()).Select(t => new KeyValuePair<string, ToolDefinition>(t.ToolId, t)),
            StringComparer.OrdinalIgnoreCase);
    }

    public bool TryGet(string toolId, out ToolDefinition? definition)
    {
        var ok = _tools.TryGetValue(toolId, out var found);
        definition = found;
        return ok;
    }

    public void Register(ToolDefinition definition)
    {
        _tools[definition.ToolId] = definition;
    }

    public IReadOnlyCollection<ToolDefinition> GetAll()
    {
        return _tools.Values.ToList();
    }
}
