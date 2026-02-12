using System.Text.Json;
using System.Text.Json.Serialization;

namespace LeaseGate.Protocol;

public static class ProtocolJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, Options);
    }

    public static T Deserialize<T>(string json)
    {
        var value = JsonSerializer.Deserialize<T>(json, Options);
        if (value is null)
        {
            throw new InvalidOperationException("Failed to deserialize protocol payload.");
        }

        return value;
    }
}
