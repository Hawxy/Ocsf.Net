using System.Text.Json;

namespace Ocsf.Generator;

public static class SchemaLoader
{
    /// <summary>Core profiles applied to the export. Excludes the linux/macos user profiles,
    /// which the server cannot include in the legacy export format.</summary>
    public static readonly string[] CoreProfiles =
    [
        "trace", "host", "datetime", "cloud", "container", "data_classification",
        "load_balancer", "osint", "network_proxy", "record_integrity",
        "security_control", "incident", "ai_operation",
    ];

    private static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static string BuildExportUrl(string version) =>
        $"https://schema.ocsf.io/{version}/export/schema?extensions=&profiles={string.Join(',', CoreProfiles)}";

    public static ExportSchema Load(string path)
    {
        using var stream = File.OpenRead(path);
        var schema = JsonSerializer.Deserialize<ExportSchema>(stream, Options)
            ?? throw new InvalidDataException($"Schema file '{path}' deserialized to null.");
        Validate(schema, path);
        return schema;
    }

    private static void Validate(ExportSchema schema, string path)
    {
        if (string.IsNullOrEmpty(schema.Version))
            throw new InvalidDataException($"Schema file '{path}' has no version.");
        if (schema.Classes.Count == 0 || schema.Objects.Count == 0 || schema.Types.Count == 0)
            throw new InvalidDataException($"Schema file '{path}' is missing classes, objects, or types.");

        // Extension entries are keyed "extension/name"; the export is fetched with extensions
        // disabled, so any such key indicates a bad snapshot.
        var extensionKeys = schema.Classes.Keys.Concat(schema.Objects.Keys)
            .Where(k => k.Contains('/'))
            .ToList();
        if (extensionKeys.Count > 0)
            throw new InvalidDataException(
                $"Schema file '{path}' contains extension entries: {string.Join(", ", extensionKeys)}");
    }
}
