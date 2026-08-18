using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ocsf.Validation;

/// <summary>
/// Validates OCSF events against the compiled schema metadata, mirroring the rules and
/// severities of the schema server's /api/v2/validate endpoint.
/// </summary>
public sealed class OcsfValidator
{
    private static readonly ConcurrentDictionary<string, Regex?> RegexCache = new(StringComparer.Ordinal);
    private static readonly Version SchemaVersion = Version.Parse(OcsfSchemaRegistry.SchemaVersion);
    private static readonly HashSet<string> NoProfiles = [];

    private readonly ValidationOptions _options;
    private readonly FrozenSet<string> _ignoredRules;

    public OcsfValidator() : this(ValidationOptions.Default)
    {
    }

    public OcsfValidator(ValidationOptions options)
    {
        _options = options;
        _ignoredRules = options.IgnoredRules.Count == 0
            ? FrozenSet<string>.Empty
            : options.IgnoredRules.ToFrozenSet(StringComparer.Ordinal);
    }

    /// <summary>Validates a typed event by serializing it to JSON first.</summary>
    public ValidationResult Validate(OcsfEvent evt)
    {
        using var document = JsonSerializer.SerializeToDocument(evt, evt.GetType(), OcsfJsonContext.Default);
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
        ValidateRecord(element, cls.Attributes, cls.AttributesByName, cls.Constraints, "", declaredProfiles, findings);

        return new ValidationResult(findings);
    }

    private HashSet<string> ReadDeclaredProfiles(JsonElement element, List<Finding> findings)
    {
        if (!element.TryGetProperty("metadata", out var metadata)
            || metadata.ValueKind != JsonValueKind.Object
            || !metadata.TryGetProperty("profiles", out var profiles)
            || profiles.ValueKind != JsonValueKind.Array)
        {
            return NoProfiles;
        }

        var declared = new HashSet<string>(StringComparer.Ordinal);
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
        var dash = version.IndexOf('-');
        var isPrerelease = dash >= 0;
        if (!Version.TryParse(isPrerelease ? version.AsSpan(0, dash) : version, out var parsed))
        {
            Add(findings, Rules.VersionIncompatible, FindingSeverity.Error,
                $"The event version '{version}' is not a valid semantic version.", path);
            return;
        }

        if (parsed.Major == 0)
        {
            Add(findings, Rules.VersionIncompatible, FindingSeverity.Error,
                $"The event version '{version}' is an initial development version and is incompatible with schema {OcsfSchemaRegistry.SchemaVersion}.", path);
        }
        else if (isPrerelease)
        {
            Add(findings, Rules.VersionIncompatible, FindingSeverity.Error,
                $"The event version '{version}' is a prerelease and is incompatible with schema {OcsfSchemaRegistry.SchemaVersion}.", path);
        }
        else if (parsed.Major > SchemaVersion.Major
            || (parsed.Major == SchemaVersion.Major && parsed.Minor > SchemaVersion.Minor))
        {
            Add(findings, Rules.VersionIncompatible, FindingSeverity.Error,
                $"The event version '{version}' is newer than schema {OcsfSchemaRegistry.SchemaVersion}; validation may produce false results.", path);
        }
        else if (parsed.Major < SchemaVersion.Major || parsed.Minor < SchemaVersion.Minor)
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
        // Producers may use JSONPath-style references (e.g. "$.resources[1].uid").
        if (path.StartsWith("$.", StringComparison.Ordinal))
            path = path[2..];

        var attrsByName = cls.AttributesByName;
        var segments = path.Split('.');
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            var bracket = segment.IndexOf('[');
            if (bracket >= 0)
                segment = segment[..bracket];

            if (!attrsByName.TryGetValue(segment, out var spec))
                return false;
            if (i == segments.Length - 1)
                return true;

            if (spec.ObjectType is null || !OcsfSchemaRegistry.Objects.TryGetValue(spec.ObjectType, out var objectSpec))
                return false;
            attrsByName = objectSpec.AttributesByName;
        }
        return true;
    }

    private void ValidateRecord(
        JsonElement element,
        IReadOnlyList<AttributeSpec> attributes,
        FrozenDictionary<string, AttributeSpec> attributesByName,
        IReadOnlyList<SchemaConstraint> constraints,
        string parentPath,
        HashSet<string> declaredProfiles,
        List<Finding> findings)
    {
        // Names present with a non-null value, collected during the property walk so the
        // requirement and constraint passes below never rescan the JSON object.
        HashSet<string>? present = null;

        foreach (var property in element.EnumerateObject())
        {
            var name = property.Name;
            var attrPath = new AttrPath(parentPath, name);
            var isNull = property.Value.ValueKind == JsonValueKind.Null;
            if (!isNull)
                (present ??= new HashSet<string>(StringComparer.Ordinal)).Add(name);

            // Attributes of profiles the event did not declare in metadata.profiles
            // are outside the effective schema and count as unknown.
            if (!attributesByName.TryGetValue(name, out var spec)
                || (spec.Profile is not null && !declaredProfiles.Contains(spec.Profile)))
            {
                Add(findings, Rules.AttributeUnknown, FindingSeverity.Error,
                    $"The attribute {name} is not defined here in the schema.", attrPath);
                continue;
            }

            if (isNull)
                continue; // Treated as absent; requirement checks below handle it.

            if (spec.DeprecatedSince is not null)
            {
                Add(findings, Rules.AttributeDeprecated, FindingSeverity.Warning,
                    $"The attribute {name} is deprecated.", attrPath, spec.DeprecatedSince);
            }

            ValidateValue(spec, property.Value, attrPath, declaredProfiles, findings);
            ValidateEnumSibling(spec, element, property.Value, attrPath, findings);
        }

        foreach (var attr in attributes)
        {
            if (attr.Profile is not null && !declaredProfiles.Contains(attr.Profile))
                continue;
            if (present is not null && present.Contains(attr.Name))
                continue;

            if (attr.Requirement == OcsfRequirement.Required)
            {
                Add(findings, Rules.AttributeRequiredMissing, FindingSeverity.Error,
                    $"The required attribute {attr.Name} is missing.", new AttrPath(parentPath, attr.Name));
            }
            else if (attr.Requirement == OcsfRequirement.Recommended && _options.WarnOnMissingRecommended)
            {
                Add(findings, Rules.AttributeRecommendedMissing, FindingSeverity.Warning,
                    $"The recommended attribute {attr.Name} is missing.", new AttrPath(parentPath, attr.Name));
            }
        }

        foreach (var constraint in constraints)
        {
            var presentCount = 0;
            foreach (var name in constraint.Attributes)
            {
                if (present is not null && present.Contains(name))
                    presentCount++;
            }

            if (constraint.Kind == ConstraintKind.AtLeastOne && presentCount == 0)
            {
                Add(findings, Rules.ConstraintAtLeastOneFailed, FindingSeverity.Error,
                    $"At least one of [{string.Join(", ", constraint.Attributes)}] must be present.", parentPath);
            }
            else if (constraint.Kind == ConstraintKind.JustOne && presentCount != 1)
            {
                Add(findings, Rules.ConstraintJustOneFailed, FindingSeverity.Error,
                    $"Exactly one of [{string.Join(", ", constraint.Attributes)}] must be present, found {presentCount}.", parentPath);
            }
        }
    }

    private void ValidateValue(
        AttributeSpec spec, JsonElement value, in AttrPath path, HashSet<string> declaredProfiles, List<Finding> findings)
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
                ValidateSingleValue(spec, item, path.WithIndex(index), declaredProfiles, findings);
                index++;
            }
            return;
        }

        ValidateSingleValue(spec, value, path, declaredProfiles, findings);
    }

    private void ValidateSingleValue(
        AttributeSpec spec, JsonElement value, in AttrPath path, HashSet<string> declaredProfiles, List<Finding> findings)
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
                    ValidateRecord(value, objectSpec.Attributes, objectSpec.AttributesByName,
                        objectSpec.Constraints, path.Resolve(), declaredProfiles, findings);
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
                if (spec.Constraint is not null)
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

    private void ValidateStringConstraints(AttributeSpec spec, string value, in AttrPath path, List<Finding> findings)
    {
        var constraint = spec.Constraint!;

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

    private void ValidateNumberConstraints(AttributeSpec spec, long value, in AttrPath path, List<Finding> findings)
    {
        if (spec.Constraint is not { } constraint)
            return;

        if ((constraint.RangeMin is { } min && value < min) || (constraint.RangeMax is { } max && value > max))
        {
            Add(findings, Rules.AttributeValueOutOfRange, FindingSeverity.Error,
                $"The value {value} of {spec.Name} is outside the range of type {spec.ScalarType}.", path);
        }
    }

    private void ValidateEnumValue(AttributeSpec spec, long value, in AttrPath path, List<Finding> findings)
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
        AttributeSpec spec, JsonElement parent, JsonElement value, in AttrPath path, List<Finding> findings)
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
            && !sibling.ValueEquals(caption))
        {
            Add(findings, Rules.EnumSiblingMismatch, FindingSeverity.Warning,
                $"The sibling attribute {spec.Sibling} value '{sibling.GetString()}' does not match the enum caption '{caption}'.", path);
        }
    }

    private void AddWrongType(
        List<Finding> findings, AttributeSpec spec, JsonElement value, in AttrPath path, string expected)
    {
        Add(findings, Rules.AttributeWrongType, FindingSeverity.Error,
            $"The attribute {spec.Name} must be {expected}, found {value.ValueKind}.", path);
    }

    private void Add(
        List<Finding> findings, string ruleId, FindingSeverity severity, string message, in AttrPath path,
        string? since = null)
    {
        if (_ignoredRules.Count > 0 && _ignoredRules.Contains(ruleId))
            return;
        findings.Add(new Finding(ruleId, severity, message, path.Resolve()) { Since = since });
    }

    private static Regex? GetRegex(string pattern) =>
        RegexCache.GetOrAdd(pattern, static p =>
        {
            try
            {
                return new Regex(p, RegexOptions.None, TimeSpan.FromSeconds(1));
            }
            catch (ArgumentException)
            {
                return null; // Schema-side regex bug; skip rather than fail events.
            }
        });

    /// <summary>
    /// A deferred attribute path: the string form is only built when a finding is emitted,
    /// so validating a clean event does not allocate per visited attribute.
    /// </summary>
    private readonly struct AttrPath
    {
        private readonly string _parent;
        private readonly string _name;
        private readonly int _index;

        public AttrPath(string parent, string name, int index = -1)
        {
            _parent = parent;
            _name = name;
            _index = index;
        }

        public static implicit operator AttrPath(string leaf) => new("", leaf);

        public AttrPath WithIndex(int index) => new(_parent, _name, index);

        public string Resolve()
        {
            var name = _index < 0 ? _name : $"{_name}[{_index}]";
            if (name.Length == 0)
                return _parent;
            return _parent.Length == 0 ? name : $"{_parent}.{name}";
        }
    }
}
