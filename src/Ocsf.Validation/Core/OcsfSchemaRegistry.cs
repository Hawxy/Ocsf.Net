namespace Ocsf.Validation;

/// <summary>
/// Compiled schema metadata used by the validator. The data lives in the generated
/// partial (Generated/OcsfSchemaRegistry.*.g.cs) and is built lazily on first use.
/// </summary>
public static partial class OcsfSchemaRegistry
{
    private static readonly Lazy<IReadOnlyDictionary<int, ClassSpec>> LazyClasses = new(BuildClasses);
    private static readonly Lazy<IReadOnlyDictionary<string, ObjectSpec>> LazyObjects = new(BuildObjects);
    private static readonly Lazy<IReadOnlyDictionary<string, TypeConstraint>> LazyTypes = new(BuildTypes);
    private static readonly Lazy<IReadOnlyCollection<string>> LazyProfiles = new(BuildProfiles);

    /// <summary>Event class specs keyed by class_uid.</summary>
    public static IReadOnlyDictionary<int, ClassSpec> Classes => LazyClasses.Value;

    /// <summary>Object specs keyed by schema object name.</summary>
    public static IReadOnlyDictionary<string, ObjectSpec> Objects => LazyObjects.Value;

    /// <summary>Scalar type constraints keyed by OCSF type name.</summary>
    public static IReadOnlyDictionary<string, TypeConstraint> Types => LazyTypes.Value;

    /// <summary>Names of profiles known to the schema.</summary>
    public static IReadOnlyCollection<string> Profiles => LazyProfiles.Value;
}
