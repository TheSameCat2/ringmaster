using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ringmaster.Core.Serialization;

public static class RingmasterJsonSerializer
{
    private static readonly JsonSerializerOptions PrettyOptions = CreateOptions(writeIndented: true);
    private static readonly JsonSerializerOptions CompactOptions = CreateOptions(writeIndented: false);

    public static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, PrettyOptions);
    }

    public static string SerializeCompact<T>(T value)
    {
        return JsonSerializer.Serialize(value, CompactOptions);
    }

    public static T Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, CompactOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize JSON into {typeof(T).FullName}.");
    }

    private static JsonSerializerOptions CreateOptions(bool writeIndented)
    {
        JsonSerializerOptions options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = writeIndented,
        };

        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
