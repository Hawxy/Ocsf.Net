namespace Ocsf.Validation;

/// <summary>The JSON shape expected for an attribute.</summary>
public enum AttrKind
{
    String,
    Integer,
    Long,
    Float,
    Boolean,
    Timestamp,
    Datetime,
    Json,
    Object,
}

/// <summary>Schema metadata for one attribute of a class or object.</summary>
public sealed record AttributeSpec(
    string Name,
    AttrKind Kind,
    string? ScalarType,
    string? ObjectType,
    bool IsArray,
    OcsfRequirement Requirement,
    string? Profile,
    string? Sibling,
    IReadOnlyDictionary<long, string>? EnumMembers,
    IReadOnlyCollection<long>? DeprecatedEnumValues,
    string? DeprecatedSince);

/// <summary>Constraints attached to an OCSF scalar type.</summary>
public sealed record TypeConstraint(
    string Name,
    int? MaxLen,
    string? Regex,
    long? RangeMin,
    long? RangeMax,
    IReadOnlyCollection<string>? StringValues);

public enum ConstraintKind
{
    AtLeastOne,
    JustOne,
}

/// <summary>A class or object level constraint over a set of attribute names.</summary>
public sealed record SchemaConstraint(ConstraintKind Kind, IReadOnlyList<string> Attributes);

/// <summary>Schema metadata for an event class.</summary>
public sealed record ClassSpec(
    string Name,
    string Caption,
    int Uid,
    int CategoryUid,
    IReadOnlyList<AttributeSpec> Attributes,
    IReadOnlyList<SchemaConstraint> Constraints,
    string? DeprecatedSince);

/// <summary>Schema metadata for an object.</summary>
public sealed record ObjectSpec(
    string Name,
    IReadOnlyList<AttributeSpec> Attributes,
    IReadOnlyList<SchemaConstraint> Constraints,
    string? DeprecatedSince);

public enum FindingSeverity
{
    Warning,
    Error,
}

/// <summary>One validation finding. Rule ids follow the schema server's validator naming.</summary>
public sealed record Finding(
    string RuleId,
    FindingSeverity Severity,
    string Message,
    string AttributePath)
{
    /// <summary>Schema version in which the flagged element was deprecated, when applicable.</summary>
    public string? Since { get; init; }
}

/// <summary>Rule ids produced by <see cref="OcsfValidator"/>.</summary>
public static class Rules
{
    public const string AttributeRequiredMissing = "attribute_required_missing";
    public const string AttributeRecommendedMissing = "attribute_recommended_missing";
    public const string AttributeUnknown = "attribute_unknown";
    public const string AttributeWrongType = "attribute_wrong_type";
    public const string AttributeValueExceedsMaxLen = "attribute_value_exceeds_max_len";
    public const string AttributeValueOutOfRange = "attribute_value_out_of_range";
    public const string AttributeValueNotInTypeValues = "attribute_value_not_in_type_values";
    public const string AttributeValueRegexMismatch = "attribute_value_regex_not_matched";
    public const string EnumValueUnknown = "attribute_enum_value_unknown";
    public const string EnumSiblingMissing = "attribute_enum_sibling_missing";
    public const string EnumSiblingMismatch = "attribute_enum_sibling_mismatch";
    public const string EnumValueDeprecated = "attribute_enum_value_deprecated";
    public const string AttributeDeprecated = "attribute_deprecated";
    public const string ClassDeprecated = "class_deprecated";
    public const string ObjectDeprecated = "object_deprecated";
    public const string ClassUidUnknown = "class_uid_unknown";
    public const string TypeUidMismatch = "type_uid_mismatch";
    public const string ConstraintAtLeastOneFailed = "constraint_at_least_one_failed";
    public const string ConstraintJustOneFailed = "constraint_just_one_failed";
    public const string ProfileUnknown = "profile_unknown";
    public const string VersionIncompatible = "version_incompatible";
    public const string VersionOlderThanSchema = "version_older_than_schema";
    public const string ObservableNameUnresolved = "observable_name_invalid_reference";
}

/// <summary>Options controlling validation strictness.</summary>
public sealed class ValidationOptions
{
    public static ValidationOptions Default { get; } = new();

    /// <summary>Warn when recommended attributes are absent. Off by default,
    /// matching the schema server's validator.</summary>
    public bool WarnOnMissingRecommended { get; init; }

    /// <summary>Rule ids to suppress entirely.</summary>
    public IReadOnlyCollection<string> IgnoredRules { get; init; } = [];
}

/// <summary>The outcome of validating one event.</summary>
public sealed class ValidationResult
{
    public ValidationResult(IReadOnlyList<Finding> findings) => Findings = findings;

    public IReadOnlyList<Finding> Findings { get; }

    public IEnumerable<Finding> Errors => Findings.Where(f => f.Severity == FindingSeverity.Error);

    public IEnumerable<Finding> Warnings => Findings.Where(f => f.Severity == FindingSeverity.Warning);

    public int ErrorCount => Errors.Count();

    public int WarningCount => Warnings.Count();

    /// <summary>True when no error-severity findings exist. Warnings do not affect validity.</summary>
    public bool IsValid => ErrorCount == 0;
}
