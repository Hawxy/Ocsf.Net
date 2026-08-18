using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ocsf.Generator;

/// <summary>Root of the compiled schema export produced by schema.ocsf.io/export/v2/schema.</summary>
public sealed class ExportSchema
{
    [JsonPropertyName("version")]
    public required string Version { get; init; }

    /// <summary>All classes, including base_event (uid 0) and extension classes keyed "ext/name".</summary>
    [JsonPropertyName("classes")]
    public required Dictionary<string, SchemaClass> Classes { get; init; }

    /// <summary>All objects, including extension objects keyed "ext/name".</summary>
    [JsonPropertyName("objects")]
    public required Dictionary<string, SchemaObject> Objects { get; init; }

    [JsonPropertyName("dictionary")]
    public required SchemaDictionary Dictionary { get; init; }

    /// <summary>Schema extensions compiled into the export, keyed by extension name (linux, win, macos).</summary>
    [JsonPropertyName("extensions")]
    public Dictionary<string, SchemaExtension>? Extensions { get; init; }

    /// <summary>Scalar type definitions (string_t, timestamp_t, ...).</summary>
    [JsonIgnore]
    public Dictionary<string, SchemaType> Types => Dictionary.Types.Attributes;

    [JsonIgnore]
    public SchemaClass BaseEvent => Classes["base_event"];
}

public sealed class SchemaDictionary
{
    [JsonPropertyName("types")]
    public required SchemaTypeCollection Types { get; init; }
}

public sealed class SchemaTypeCollection
{
    [JsonPropertyName("attributes")]
    public required Dictionary<string, SchemaType> Attributes { get; init; }
}

public sealed class SchemaExtension
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("uid")]
    public int Uid { get; init; }

    [JsonPropertyName("caption")]
    public string? Caption { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }
}

public sealed class SchemaClass
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("caption")]
    public string? Caption { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("uid")]
    public int Uid { get; init; }

    [JsonPropertyName("extends")]
    public string? Extends { get; init; }

    [JsonPropertyName("category")]
    public string? Category { get; init; }

    [JsonPropertyName("category_name")]
    public string? CategoryName { get; init; }

    [JsonPropertyName("category_uid")]
    public int CategoryUid { get; init; }

    [JsonPropertyName("attributes")]
    public required Dictionary<string, SchemaAttribute> Attributes { get; init; }

    /// <summary>Constraint kind (at_least_one, just_one) to attribute names.</summary>
    [JsonPropertyName("constraints")]
    public Dictionary<string, List<string>>? Constraints { get; init; }

    [JsonPropertyName("profiles")]
    public List<string>? Profiles { get; init; }

    /// <summary>Owning extension name for extension classes (e.g. "win").</summary>
    [JsonPropertyName("extension")]
    public string? Extension { get; init; }

    [JsonPropertyName("extension_id")]
    public int? ExtensionId { get; init; }

    [JsonPropertyName("@deprecated")]
    public SchemaDeprecation? Deprecated { get; init; }
}

public sealed class SchemaObject
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("caption")]
    public string? Caption { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("extends")]
    public string? Extends { get; init; }

    [JsonPropertyName("attributes")]
    public required Dictionary<string, SchemaAttribute> Attributes { get; init; }

    [JsonPropertyName("constraints")]
    public Dictionary<string, List<string>>? Constraints { get; init; }

    /// <summary>Observable type_id when instances of this object are observables.</summary>
    [JsonPropertyName("observable")]
    public int? Observable { get; init; }

    [JsonPropertyName("profiles")]
    public List<string>? Profiles { get; init; }

    /// <summary>Owning extension name for extension objects (e.g. "win").</summary>
    [JsonPropertyName("extension")]
    public string? Extension { get; init; }

    [JsonPropertyName("extension_id")]
    public int? ExtensionId { get; init; }

    [JsonPropertyName("@deprecated")]
    public SchemaDeprecation? Deprecated { get; init; }
}

public sealed class SchemaAttribute
{
    [JsonPropertyName("caption")]
    public string? Caption { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>Scalar type name (string_t, integer_t, object_t, ...).</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("type_name")]
    public string? TypeName { get; init; }

    /// <summary>Referenced object name when Type is object_t.</summary>
    [JsonPropertyName("object_type")]
    public string? ObjectType { get; init; }

    [JsonPropertyName("object_name")]
    public string? ObjectName { get; init; }

    /// <summary>required | recommended | optional.</summary>
    [JsonPropertyName("requirement")]
    public string? Requirement { get; init; }

    [JsonPropertyName("group")]
    public string? Group { get; init; }

    /// <summary>Name of the companion label attribute for enum attributes.</summary>
    [JsonPropertyName("sibling")]
    public string? Sibling { get; init; }

    /// <summary>Profiles that mix this attribute in, when not part of the base definition.
    /// Usually a single profile; process.egid/euid belong to both OS user profiles.</summary>
    [JsonPropertyName("profiles")]
    public List<string>? Profiles { get; init; }

    /// <summary>Extension that patched this attribute onto a core entity (e.g. "win").</summary>
    [JsonPropertyName("extension")]
    public string? Extension { get; init; }

    [JsonPropertyName("extension_id")]
    public int? ExtensionId { get; init; }

    [JsonPropertyName("is_array")]
    public bool IsArray { get; init; }

    /// <summary>Enum members keyed by their stringified integer value.</summary>
    [JsonPropertyName("enum")]
    public Dictionary<string, SchemaEnumMember>? Enum { get; init; }

    [JsonPropertyName("observable")]
    public int? Observable { get; init; }

    [JsonPropertyName("@deprecated")]
    public SchemaDeprecation? Deprecated { get; init; }
}

public sealed class SchemaEnumMember
{
    [JsonPropertyName("caption")]
    public required string Caption { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("@deprecated")]
    public SchemaDeprecation? Deprecated { get; init; }
}

public sealed class SchemaType
{
    [JsonPropertyName("caption")]
    public string? Caption { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>Base scalar type for derived types (e.g. timestamp_t is a long_t).</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("type_name")]
    public string? TypeName { get; init; }

    [JsonPropertyName("max_len")]
    public int? MaxLen { get; init; }

    [JsonPropertyName("regex")]
    public string? Regex { get; init; }

    /// <summary>[min, max] for numeric types.</summary>
    [JsonPropertyName("range")]
    public List<long>? Range { get; init; }

    /// <summary>Allowed literal values, when constrained.</summary>
    [JsonPropertyName("values")]
    public List<JsonElement>? Values { get; init; }

    [JsonPropertyName("observable")]
    public int? Observable { get; init; }
}

public sealed class SchemaDeprecation
{
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("since")]
    public string? Since { get; init; }

    [JsonPropertyName("superseded_by")]
    public List<string>? SupersededBy { get; init; }
}
