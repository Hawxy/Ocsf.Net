namespace Ocsf.Generator;

/// <summary>Maps OCSF scalar types to C# types by resolving each type to its primitive root.</summary>
public sealed class TypeMapper
{
    private readonly Dictionary<string, string> _resolved = new(StringComparer.Ordinal);

    public TypeMapper(ExportSchema schema)
    {
        foreach (var name in schema.Types.Keys)
            _resolved[name] = Resolve(schema, name);
    }

    /// <summary>Returns the C# type (without nullable suffix) for an OCSF scalar type.</summary>
    public string MapScalar(string ocsfType) =>
        _resolved.TryGetValue(ocsfType, out var mapped)
            ? mapped
            : throw new InvalidOperationException($"Unknown OCSF type '{ocsfType}'.");

    /// <summary>True when the type's primitive root is integer_t, i.e. eligible for C# enum generation.</summary>
    public bool IsIntegerBased(string ocsfType) => MapScalar(ocsfType) == "int";

    private static string Resolve(ExportSchema schema, string name)
    {
        // Wire-format preserving special cases: timestamps get the helper struct,
        // json_t stays raw. Everything else resolves through its base type chain.
        var current = name;
        for (var depth = 0; depth < 10; depth++)
        {
            switch (current)
            {
                case "timestamp_t": return "OcsfTimestamp";
                case "json_t": return "JsonElement";
                case "string_t": return "string";
                case "integer_t": return "int";
                case "long_t": return "long";
                case "float_t": return "double";
                case "boolean_t": return "bool";
            }

            if (!schema.Types.TryGetValue(current, out var type) || type.Type is null)
                throw new InvalidOperationException($"OCSF type '{name}' does not resolve to a primitive (stopped at '{current}').");
            current = type.Type;
        }
        throw new InvalidOperationException($"OCSF type '{name}' has a base type cycle.");
    }
}
