using System.Text.Json;

namespace Ocsf;

/// <summary>
/// Serialization entry points backed by the source-generated <see cref="OcsfJsonContext"/>,
/// so all operations are reflection-free and NativeAOT/trim compatible.
/// </summary>
public static class OcsfJson
{
    /// <summary>Options wired to the generated context: snake_case wire names via
    /// per-property attributes, nulls omitted, OCSF timestamp handling.</summary>
    public static JsonSerializerOptions DefaultOptions => OcsfJsonContext.Default.Options;

    /// <summary>Serializes an event using its runtime type.</summary>
    public static string Serialize(OcsfEvent value) =>
        JsonSerializer.Serialize(value, value.GetType(), OcsfJsonContext.Default);

    /// <summary>Serializes an object using its runtime type.</summary>
    public static string Serialize(OcsfObject value) =>
        JsonSerializer.Serialize(value, value.GetType(), OcsfJsonContext.Default);

    /// <summary>Deserializes to a generated OCSF type. For events of unknown class, use
    /// <see cref="OcsfEventReader.Deserialize(string)"/> instead.</summary>
    public static T? Deserialize<T>(string json) where T : class =>
        (T?)JsonSerializer.Deserialize(json, typeof(T), OcsfJsonContext.Default);
}
