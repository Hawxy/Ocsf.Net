using System.Collections.Frozen;

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
    IReadOnlyList<string>? Profiles,
    string? Sibling,
    IReadOnlyDictionary<long, string>? EnumMembers,
    IReadOnlyCollection<long>? DeprecatedEnumValues,
    string? DeprecatedSince)
{
    private TypeConstraint? _constraint;
    private bool _constraintResolved;

    /// <summary>The constraint of this attribute's scalar type, or null when the type is
    /// unconstrained. Resolved once so value validation skips the type table lookup.</summary>
    public TypeConstraint? Constraint
    {
        get
        {
            if (!_constraintResolved)
            {
                _constraint = ScalarType is not null
                    && OcsfSchemaRegistry.Types.TryGetValue(ScalarType, out var constraint)
                    && constraint.HasAnyConstraint
                    ? constraint
                    : null;
                _constraintResolved = true;
            }
            return _constraint;
        }
    }
}

/// <summary>Constraints attached to an OCSF scalar type.</summary>
public sealed record TypeConstraint(
    string Name,
    int? MaxLen,
    string? Regex,
    long? RangeMin,
    long? RangeMax,
    IReadOnlyCollection<string>? StringValues)
{
    /// <summary>True when at least one constraint is defined.</summary>
    public bool HasAnyConstraint =>
        MaxLen is not null || Regex is not null || RangeMin is not null || RangeMax is not null
        || StringValues is { Count: > 0 };
}

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
    string? DeprecatedSince)
{
    private FrozenDictionary<string, AttributeSpec>? _attributesByName;

    /// <summary>Attribute specs keyed by schema name; built once on first use.</summary>
    public FrozenDictionary<string, AttributeSpec> AttributesByName =>
        _attributesByName ??= Attributes.ToFrozenDictionary(a => a.Name, StringComparer.Ordinal);
}

/// <summary>Schema metadata for an object.</summary>
public sealed record ObjectSpec(
    string Name,
    IReadOnlyList<AttributeSpec> Attributes,
    IReadOnlyList<SchemaConstraint> Constraints,
    string? DeprecatedSince)
{
    private FrozenDictionary<string, AttributeSpec>? _attributesByName;

    /// <summary>Attribute specs keyed by schema name; built once on first use.</summary>
    public FrozenDictionary<string, AttributeSpec> AttributesByName =>
        _attributesByName ??= Attributes.ToFrozenDictionary(a => a.Name, StringComparer.Ordinal);
}

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
    public const string EnumSiblingIncorrect = "attribute_enum_sibling_incorrect";
    public const string EnumValueDeprecated = "attribute_enum_value_deprecated";
    public const string AttributeDeprecated = "attribute_deprecated";
    public const string ClassDeprecated = "class_deprecated";
    public const string ObjectDeprecated = "object_deprecated";
    public const string ClassUidUnknown = "class_uid_unknown";
    public const string TypeUidMismatch = "type_uid_mismatch";
    /// <summary>The server reports every constraint kind under one rule id;
    /// the finding message names the failed kind.</summary>
    public const string ConstraintFailed = "constraint_failed";
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
    public ValidationResult(IReadOnlyList<Finding> findings)
    {
        Findings = findings;
        foreach (var finding in findings)
        {
            if (finding.Severity == FindingSeverity.Error)
                ErrorCount++;
            else
                WarningCount++;
        }
    }

    public IReadOnlyList<Finding> Findings { get; }

    public IEnumerable<Finding> Errors => Findings.Where(f => f.Severity == FindingSeverity.Error);

    public IEnumerable<Finding> Warnings => Findings.Where(f => f.Severity == FindingSeverity.Warning);

    public int ErrorCount { get; }

    public int WarningCount { get; }

    /// <summary>True when no error-severity findings exist. Warnings do not affect validity.</summary>
    public bool IsValid => ErrorCount == 0;
}
