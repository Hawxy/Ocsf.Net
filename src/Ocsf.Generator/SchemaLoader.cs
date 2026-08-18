using System.Text.Json;

namespace Ocsf.Generator;

public static class SchemaLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>The v2 export always returns the full compiled schema: every profile and every
    /// extension. The legacy export cannot include the linux/macos user profiles (both patch
    /// process.egid, which its single-profile-per-attribute format rejects).</summary>
    public static string BuildExportUrl(string version) =>
        $"https://schema.ocsf.io/{version}/export/v2/schema";

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
        if (!schema.Classes.ContainsKey("base_event"))
            throw new InvalidDataException($"Schema file '{path}' has no base_event class.");

        // Extension entities are keyed "extension/name" and must self-identify via their
        // extension field; a mismatch indicates a malformed snapshot.
        foreach (var (key, extension) in schema.Classes.Select(c => (c.Key, c.Value.Extension))
                     .Concat(schema.Objects.Select(o => (o.Key, o.Value.Extension))))
        {
            var slash = key.IndexOf('/');
            var prefix = slash < 0 ? null : key[..slash];
            if (prefix != extension)
                throw new InvalidDataException(
                    $"Schema file '{path}': entry '{key}' declares extension '{extension}', expected '{prefix}'.");
        }

        // Every concrete object reference must resolve, extension objects included. A dangling
        // reference means the export was produced without the owning extension compiled in.
        foreach (var (owner, attributes) in schema.Classes.Select(c => (c.Key, c.Value.Attributes))
                     .Concat(schema.Objects.Select(o => (o.Key, o.Value.Attributes))))
        {
            foreach (var (attrName, attr) in attributes)
            {
                if (attr.Type == "object_t"
                    && attr.ObjectType is not (null or "object")
                    && !schema.Objects.ContainsKey(attr.ObjectType))
                    throw new InvalidDataException(
                        $"Schema file '{path}': '{owner}.{attrName}' references unknown object '{attr.ObjectType}'.");
            }
        }
    }
}
