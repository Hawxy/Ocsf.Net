using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ocsf.Validation;

/// <summary>
/// Validates OCSF events against the compiled schema metadata, mirroring the rules and
/// severities of the schema server's /api/v2/validate endpoint.
/// </summary>
public sealed class OcsfValidator
{
    private static readonly Dictionary<string, Regex?> RegexCache = new(StringComparer.Ordinal);

    private readonly ValidationOptions _options;

    public OcsfValidator() : this(ValidationOptions.Default)
    {
    }

    public OcsfValidator(ValidationOptions options) => _options = options;

    /// <summary>Validates a typed event by serializing it to JSON first.</summary>
    public ValidationResult Validate(OcsfEvent evt)
    {
        using var document = JsonDocument.Parse(OcsfJson.Serialize(evt));
        return Validate(document.RootElement);
    }

    /// <summary>Validates an event, dispatching on its <c>class_uid</c> attribute.</summary>
    public ValidationResult Validate(JsonElement element)
    {
        var findings = new List<Finding>();

        if (element.ValueKind != JsonValueKind.Object)
        {
            Add(findings, Rules.AttributeWrongType, FindingSeverity.Error, "The event is not a JSON object.", "");
            return new ValidationResult(findings);
        }

        if (!element.TryGetProperty("class_uid", out var uidElement)
            || uidElement.ValueKind != JsonValueKind.Number
            || !uidElement.TryGetInt32(out var classUid))
        {
            Add(findings, Rules.AttributeRequiredMissing, FindingSeverity.Error,
                "The required attribute class_uid is missing or not an integer.", "class_uid");
            return new ValidationResult(findings);
        }

        return Validate(element, classUid, findings);
    }

    /// <summary>Validates an event against a specific event class.</summary>
    public ValidationResult Validate(JsonElement element, int classUid) =>
        Validate(element, classUid, []);

    private ValidationResult Validate(JsonElement element, int classUid, List<Finding> findings)
    {
        if (!OcsfSchemaRegistry.Classes.TryGetValue(classUid, out var cls))
        {
            Add(findings, Rules.ClassUidUnknown, FindingSeverity.Error,
                $"class_uid {classUid} is not a known event class.", "class_uid");
            return new ValidationResult(findings);
        }

        if (cls.DeprecatedSince is not null)
        {
            Add(findings, Rules.ClassDeprecated, FindingSeverity.Warning,
                $"The event class {cls.Name} ({cls.Uid}) is deprecated.", "class_uid", cls.DeprecatedSince);
        }

        var declaredProfiles = ReadDeclaredProfiles(element, findings);
        ValidateVersion(element, findings);
        ValidateTypeUid(element, classUid, findings);
        ValidateObservables(element, cls, findings);
        ValidateRecord(element, cls.Attributes, cls.Constraints, "", declaredProfiles, findings);

        return new ValidationResult(findings);
    }

    private HashSet<string> ReadDeclaredProfiles(JsonElement element, List<Finding> findings)
    {
        var declared = new HashSet<string>(StringComparer.Ordinal);
        if (!element.TryGetProperty("metadata", out var metadata)
            || metadata.ValueKind != JsonValueKind.Object
            || !metadata.TryGetProperty("profiles", out var profiles)
            || profiles.ValueKind != JsonValueKind.Array)
        {
            return declared;
        }

        var index = 0;
        foreach (var profile in profiles.EnumerateArray())
        {
            if (profile.ValueKind == JsonValueKind.String)
            {
                var name = profile.GetString()!;
                declared.Add(name);
                if (!OcsfSchemaRegistry.Profiles.Contains(name))
                {
                    Add(findings, Rules.ProfileUnknown, FindingSeverity.Error,
                        $"The profile '{name}' is not defined in the schema.", $"metadata.profiles[{index}]");
                }
            }
            index++;
        }
        return declared;
    }

    private void ValidateVersion(JsonElement element, List<Finding> findings)
    {
        if (!element.TryGetProperty("metadata", out var metadata)
            || metadata.ValueKind != JsonValueKind.Object
            || !metadata.TryGetProperty("version", out var versionElement)
            || versionElement.ValueKind != JsonValueKind.String)
        {
            return; // Absence is reported by the required-attribute rule.
        }

        const string path = "metadata.version";
        var version = versionElement.GetString()!;
        var core = version.Split('-', 2);
        if (!Version.TryParse(core[0], out var parsed))
        {
            Add(findings, Rules.VersionIncompatible, FindingSeverity.Error,
                $"The event version '{version}' is not a valid semantic version.", path);
            return;
        }

        var schema = Version.Parse(OcsfSchemaRegistry.SchemaVersion);
        if (parsed.Major == 0)
        {
            Add(findings, Rules.VersionIncompatible, FindingSeverity.Error,
                $"The event version '{version}' is an initial development version and is incompatible with schema {OcsfSchemaRegistry.SchemaVersion}.", path);
        }
        else if (core.Length > 1)
        {
            Add(findings, Rules.VersionIncompatible, FindingSeverity.Error,
                $"The event version '{version}' is a prerelease and is incompatible with schema {OcsfSchemaRegistry.SchemaVersion}.", path);
        }
        else if (parsed.Major > schema.Major || (parsed.Major == schema.Major && parsed.Minor > schema.Minor))
        {
            Add(findings, Rules.VersionIncompatible, FindingSeverity.Error,
                $"The event version '{version}' is newer than schema {OcsfSchemaRegistry.SchemaVersion}; validation may produce false results.", path);
        }
        else if (parsed.Major < schema.Major || parsed.Minor < schema.Minor)
        {
            Add(findings, Rules.VersionOlderThanSchema, FindingSeverity.Warning,
                $"The event version '{version}' is older than schema {OcsfSchemaRegistry.SchemaVersion}.", path);
        }
    }

    private void ValidateTypeUid(JsonElement element, int classUid, List<Finding> findings)
    {
        if (!element.TryGetProperty("type_uid", out var typeUidElement)
            || typeUidElement.ValueKind != JsonValueKind.Number
            || !typeUidElement.TryGetInt64(out var typeUid)
            || !element.TryGetProperty("activity_id", out var activityElement)
            || activityElement.ValueKind != JsonValueKind.Number
            || !activityElement.TryGetInt64(out var activityId))
        {
            return;
        }

        var expected = classUid * 100L + activityId;
        if (typeUid != expected)
        {
            Add(findings, Rules.TypeUidMismatch, FindingSeverity.Error,
                $"type_uid {typeUid} does not equal class_uid * 100 + activity_id ({expected}).", "type_uid");
        }
    }

    private void ValidateObservables(JsonElement element, ClassSpec cls, List<Finding> findings)
    {
        if (!element.TryGetProperty("observables", out var observables)
            || observables.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var index = 0;
        foreach (var observable in observables.EnumerateArray())
        {
            if (observable.ValueKind == JsonValueKind.Object
                && observable.TryGetProperty("name", out var nameElement)
                && nameElement.ValueKind == JsonValueKind.String)
            {
                var name = nameElement.GetString()!;
                if (!ResolvesToAttribute(cls, name))
                {
                    Add(findings, Rules.ObservableNameUnresolved, FindingSeverity.Error,
                        $"The observable name '{name}' does not reference an attribute of class {cls.Name}.",
                        $"observables[{index}].name");
                }
            }
            index++;
        }
    }

    private static bool ResolvesToAttribute(ClassSpec cls, string path)
    {
        IReadOnlyList<AttributeSpec> current = cls.Attributes;
        var segments = path.Split('.');
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            var bracket = segment.IndexOf('[');
            if (bracket >= 0)
                segment = segment[..bracket];

            var spec = current.FirstOrDefault(a => a.Name == segment);
            if (spec is null)
                return false;
            if (i == segments.Length - 1)
                return true;

            if (spec.ObjectType is null || !OcsfSchemaRegistry.Objects.TryGetValue(spec.ObjectType, out var objectSpec))
                return false;
            current = objectSpec.Attributes;
        }
        return true;
    }

    private void ValidateRecord(
        JsonElement element,
        IReadOnlyList<AttributeSpec> attributes,
        IReadOnlyList<SchemaConstraint> constraints,
        string path,
        HashSet<string> declaredProfiles,
        List<Finding> findings)
    {
        // The effective attribute set excludes attributes of profiles the event
        // did not declare in metadata.profiles.
        var effective = new Dictionary<string, AttributeSpec>(StringComparer.Ordinal);
        foreach (var attr in attributes)
        {
            if (attr.Profile is null || declaredProfiles.Contains(attr.Profile))
                effective[attr.Name] = attr;
        }

        foreach (var property in element.EnumerateObject())
        {
            var attrPath = Combine(path, property.Name);
            if (!effective.TryGetValue(property.Name, out var spec))
            {
                Add(findings, Rules.AttributeUnknown, FindingSeverity.Error,
                    $"The attribute {property.Name} is not defined here in the schema.", attrPath);
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Null)
                continue; // Treated as absent; requirement checks below handle it.

            if (spec.DeprecatedSince is not null)
            {
                Add(findings, Rules.AttributeDeprecated, FindingSeverity.Warning,
                    $"The attribute {property.Name} is deprecated.", attrPath, spec.DeprecatedSince);
            }

            ValidateValue(spec, property.Value, attrPath, declaredProfiles, findings);
            ValidateEnumSibling(spec, element, property.Value, attrPath, findings);
        }

        foreach (var attr in effective.Values)
        {
            var present = element.TryGetProperty(attr.Name, out var value)
                && value.ValueKind != JsonValueKind.Null;
            if (present)
                continue;

            if (attr.Requirement == OcsfRequirement.Required)
            {
                Add(findings, Rules.AttributeRequiredMissing, FindingSeverity.Error,
                    $"The required attribute {attr.Name} is missing.", Combine(path, attr.Name));
            }
            else if (attr.Requirement == OcsfRequirement.Recommended && _options.WarnOnMissingRecommended)
            {
                Add(findings, Rules.AttributeRecommendedMissing, FindingSeverity.Warning,
                    $"The recommended attribute {attr.Name} is missing.", Combine(path, attr.Name));
            }
        }

        foreach (var constraint in constraints)
        {
            var presentCount = constraint.Attributes.Count(name =>
                element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null);

            if (constraint.Kind == ConstraintKind.AtLeastOne && presentCount == 0)
            {
                Add(findings, Rules.ConstraintAtLeastOneFailed, FindingSeverity.Error,
                    $"At least one of [{string.Join(", ", constraint.Attributes)}] must be present.", path);
            }
            else if (constraint.Kind == ConstraintKind.JustOne && presentCount != 1)
            {
                Add(findings, Rules.ConstraintJustOneFailed, FindingSeverity.Error,
                    $"Exactly one of [{string.Join(", ", constraint.Attributes)}] must be present, found {presentCount}.", path);
            }
        }
    }

    private void ValidateValue(
        AttributeSpec spec, JsonElement value, string path, HashSet<string> declaredProfiles, List<Finding> findings)
    {
        if (spec.IsArray)
        {
            if (value.ValueKind != JsonValueKind.Array)
            {
                Add(findings, Rules.AttributeWrongType, FindingSeverity.Error,
                    $"The attribute {spec.Name} must be an array.", path);
                return;
            }

            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                ValidateSingleValue(spec, item, $"{path}[{index}]", declaredProfiles, findings);
                index++;
            }
            return;
        }

        ValidateSingleValue(spec, value, path, declaredProfiles, findings);
    }

    private void ValidateSingleValue(
        AttributeSpec spec, JsonElement value, string path, HashSet<string> declaredProfiles, List<Finding> findings)
    {
        switch (spec.Kind)
        {
            case AttrKind.Json:
                return;

            case AttrKind.Object:
                if (value.ValueKind != JsonValueKind.Object)
                {
                    AddWrongType(findings, spec, value, path, "an object");
                    return;
                }
                if (spec.ObjectType is not null
                    && OcsfSchemaRegistry.Objects.TryGetValue(spec.ObjectType, out var objectSpec))
                {
                    if (objectSpec.DeprecatedSince is not null)
                    {
                        Add(findings, Rules.ObjectDeprecated, FindingSeverity.Warning,
                            $"The object {objectSpec.Name} is deprecated.", path, objectSpec.DeprecatedSince);
                    }
                    ValidateRecord(value, objectSpec.Attributes, objectSpec.Constraints, path, declaredProfiles, findings);
                }
                return;

            case AttrKind.Boolean:
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    AddWrongType(findings, spec, value, path, "a boolean");
                return;

            case AttrKind.String:
            case AttrKind.Datetime:
                if (value.ValueKind != JsonValueKind.String)
                {
                    AddWrongType(findings, spec, value, path, "a string");
                    return;
                }
                ValidateStringConstraints(spec, value.GetString()!, path, findings);
                return;

            case AttrKind.Integer:
            case AttrKind.Long:
            case AttrKind.Timestamp:
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number))
                {
                    AddWrongType(findings, spec, value, path, "an integer");
                    return;
                }
                ValidateNumberConstraints(spec, number, path, findings);
                ValidateEnumValue(spec, number, path, findings);
                return;

            case AttrKind.Float:
                if (value.ValueKind != JsonValueKind.Number)
                    AddWrongType(findings, spec, value, path, "a number");
                return;
        }
    }

    private void ValidateStringConstraints(AttributeSpec spec, string value, string path, List<Finding> findings)
    {
        if (spec.ScalarType is null
            || !OcsfSchemaRegistry.Types.TryGetValue(spec.ScalarType, out var constraint))
        {
            return;
        }

        if (constraint.MaxLen is { } maxLen && value.Length > maxLen)
        {
            Add(findings, Rules.AttributeValueExceedsMaxLen, FindingSeverity.Error,
                $"The attribute {spec.Name} exceeds the maximum length of {maxLen} for type {spec.ScalarType}.", path);
        }

        if (constraint.StringValues is { Count: > 0 } values && !values.Contains(value))
        {
            Add(findings, Rules.AttributeValueNotInTypeValues, FindingSeverity.Error,
                $"The value '{value}' is not in the allowed values of type {spec.ScalarType}.", path);
        }

        if (constraint.Regex is { Length: > 0 } pattern && GetRegex(pattern) is { } regex && !regex.IsMatch(value))
        {
            Add(findings, Rules.AttributeValueRegexMismatch, FindingSeverity.Warning,
                $"The value of {spec.Name} does not match the {spec.ScalarType} pattern.", path);
        }
    }

    private void ValidateNumberConstraints(AttributeSpec spec, long value, string path, List<Finding> findings)
    {
        if (spec.ScalarType is null
            || !OcsfSchemaRegistry.Types.TryGetValue(spec.ScalarType, out var constraint))
        {
            return;
        }

        if ((constraint.RangeMin is { } min && value < min) || (constraint.RangeMax is { } max && value > max))
        {
            Add(findings, Rules.AttributeValueOutOfRange, FindingSeverity.Error,
                $"The value {value} of {spec.Name} is outside the range of type {spec.ScalarType}.", path);
        }
    }

    private void ValidateEnumValue(AttributeSpec spec, long value, string path, List<Finding> findings)
    {
        if (spec.EnumMembers is null)
            return;

        if (!spec.EnumMembers.ContainsKey(value))
        {
            Add(findings, Rules.EnumValueUnknown, FindingSeverity.Warning,
                $"The value {value} is not defined for the enum attribute {spec.Name}.", path);
        }
        else if (spec.DeprecatedEnumValues is { } deprecated && deprecated.Contains(value))
        {
            Add(findings, Rules.EnumValueDeprecated, FindingSeverity.Warning,
                $"The enum value {value} of {spec.Name} is deprecated.", path);
        }
    }

    private void ValidateEnumSibling(
        AttributeSpec spec, JsonElement parent, JsonElement value, string path, List<Finding> findings)
    {
        if (spec.EnumMembers is null || spec.Sibling is null || spec.IsArray)
            return;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var code))
            return;

        var siblingPresent = parent.TryGetProperty(spec.Sibling, out var sibling)
            && sibling.ValueKind == JsonValueKind.String;

        if (code == 99)
        {
            // "Other" requires the source-specific label in the sibling attribute.
            if (!siblingPresent)
            {
                Add(findings, Rules.EnumSiblingMissing, FindingSeverity.Error,
                    $"The attribute {spec.Name} is 99 (Other) but the sibling attribute {spec.Sibling} is missing.", path);
            }
            return;
        }

        if (siblingPresent
            && spec.EnumMembers.TryGetValue(code, out var caption)
            && !string.Equals(sibling.GetString(), caption, StringComparison.Ordinal))
        {
            Add(findings, Rules.EnumSiblingMismatch, FindingSeverity.Warning,
                $"The sibling attribute {spec.Sibling} value '{sibling.GetString()}' does not match the enum caption '{caption}'.", path);
        }
    }

    private void AddWrongType(
        List<Finding> findings, AttributeSpec spec, JsonElement value, string path, string expected)
    {
        Add(findings, Rules.AttributeWrongType, FindingSeverity.Error,
            $"The attribute {spec.Name} must be {expected}, found {value.ValueKind}.", path);
    }

    private static string Combine(string path, string name) => path.Length == 0 ? name : $"{path}.{name}";

    private void Add(
        List<Finding> findings, string ruleId, FindingSeverity severity, string message, string path,
        string? since = null)
    {
        if (_options.IgnoredRules.Contains(ruleId))
            return;
        findings.Add(new Finding(ruleId, severity, message, path) { Since = since });
    }

    private static Regex? GetRegex(string pattern)
    {
        lock (RegexCache)
        {
            if (!RegexCache.TryGetValue(pattern, out var regex))
            {
                try
                {
                    regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
                }
                catch (ArgumentException)
                {
                    regex = null; // Schema-side regex bug; skip rather than fail events.
                }
                RegexCache[pattern] = regex;
            }
            return regex;
        }
    }
}
