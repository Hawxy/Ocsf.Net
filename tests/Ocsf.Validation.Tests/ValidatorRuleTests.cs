using System.Text.Json;

namespace Ocsf.Validation.Tests;

public class ValidatorRuleTests
{
    private static ValidationResult Validate(string json, ValidationOptions? options = null)
    {
        using var doc = JsonDocument.Parse(json);
        return new OcsfValidator(options ?? ValidationOptions.Default).Validate(doc.RootElement);
    }

    private static string MinimalAuthentication(string extra = "") => $$"""
        {
            "class_uid": 3002,
            "category_uid": 3,
            "activity_id": 1,
            "type_uid": 300201,
            "severity_id": 1,
            "time": 1618524549901,
            "metadata": { "version": "1.9.0", "product": { "name": "test", "vendor_name": "test" } },
            "user": { "name": "janedoe1" },
            "dst_endpoint": { "ip": "10.0.0.1" }
            {{extra}}
        }
        """;

    [Test]
    public async Task MinimalValidEvent_HasNoErrors()
    {
        var result = Validate(MinimalAuthentication());

        await Assert.That(result.Findings.Select(f => $"{f.RuleId} {f.AttributePath}").ToList()).IsEmpty();
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task TypedEvent_ValidatesClean()
    {
        var evt = new Ocsf.Events.Iam.Authentication
        {
            Time = new OcsfTimestamp(1618524549901),
            SeverityId = Ocsf.Events.Iam.AuthenticationSeverityId.Informational,
            Metadata = new Ocsf.Objects.Metadata
            {
                Version = "1.9.0",
                Product = new Ocsf.Objects.Product { Name = "test", VendorName = "test" },
            },
            User = new Ocsf.Objects.User { Name = "janedoe1" },
            DstEndpoint = new Ocsf.Objects.NetworkEndpoint { Ip = "10.0.0.1" },
        };
        evt.SetActivity(Ocsf.Events.Iam.AuthenticationActivityId.Logon);

        var result = new OcsfValidator().Validate(evt);

        await Assert.That(result.Findings.Select(f => $"{f.RuleId} {f.AttributePath}").ToList()).IsEmpty();
    }

    [Test]
    public async Task UnknownClassUid_IsError()
    {
        var result = Validate("""{"class_uid": 999999}""");

        await Assert.That(result.Errors.Select(f => f.RuleId)).Contains(Rules.ClassUidUnknown);
    }

    [Test]
    public async Task MissingRequiredAttribute_IsError()
    {
        // severity_id removed
        var result = Validate("""
            {"class_uid": 3002, "category_uid": 3, "activity_id": 1, "type_uid": 300201,
             "time": 1618524549901, "metadata": {"version": "1.9.0", "product": {"vendor_name": "t"}},
             "user": {"name": "j"}}
            """);

        var missing = result.Errors.Where(f => f.RuleId == Rules.AttributeRequiredMissing).Select(f => f.AttributePath);
        await Assert.That(missing).Contains("severity_id");
    }

    [Test]
    public async Task RequiredMissing_InNestedObject_IsError()
    {
        // metadata.version and metadata.product are required
        var result = Validate("""
            {"class_uid": 3002, "category_uid": 3, "activity_id": 1, "type_uid": 300201,
             "severity_id": 1, "time": 1618524549901, "metadata": {}, "user": {"name": "j"}}
            """);

        var missing = result.Errors.Where(f => f.RuleId == Rules.AttributeRequiredMissing).Select(f => f.AttributePath).ToList();
        await Assert.That(missing).Contains("metadata.version");
        await Assert.That(missing).Contains("metadata.product");
    }

    [Test]
    public async Task UnknownAttribute_IsError()
    {
        var result = Validate(MinimalAuthentication(""", "not_in_schema": 1"""));

        var unknown = result.Errors.Single(f => f.RuleId == Rules.AttributeUnknown);
        await Assert.That(unknown.AttributePath).IsEqualTo("not_in_schema");
    }

    [Test]
    public async Task ProfileAttribute_WithoutDeclaredProfile_IsUnknown()
    {
        var result = Validate(MinimalAuthentication(", \"start_time_dt\": \"2021-04-15T21:29:09Z\""));

        var unknown = result.Errors.Single(f => f.RuleId == Rules.AttributeUnknown);
        await Assert.That(unknown.AttributePath).IsEqualTo("start_time_dt");
    }

    [Test]
    public async Task ProfileAttribute_WithDeclaredProfile_IsKnown()
    {
        var json = """
            {"class_uid": 3002, "category_uid": 3, "activity_id": 1, "type_uid": 300201,
             "severity_id": 1, "time": 1618524549901,
             "metadata": {"version": "1.9.0", "product": {"vendor_name": "t"}, "profiles": ["datetime"]},
             "user": {"name": "j"}, "start_time_dt": "2021-04-15T21:29:09Z"}
            """;

        var result = Validate(json);

        await Assert.That(result.Errors.Select(f => f.RuleId)).DoesNotContain(Rules.AttributeUnknown);
    }

    [Test]
    public async Task UnknownProfile_IsError()
    {
        var json = """
            {"class_uid": 3002, "category_uid": 3, "activity_id": 1, "type_uid": 300201,
             "severity_id": 1, "time": 1618524549901,
             "metadata": {"version": "1.9.0", "product": {"vendor_name": "t"}, "profiles": ["nope"]},
             "user": {"name": "j"}}
            """;

        var result = Validate(json);

        await Assert.That(result.Errors.Select(f => f.RuleId)).Contains(Rules.ProfileUnknown);
    }

    [Test]
    public async Task WrongJsonKind_IsError()
    {
        var result = Validate(MinimalAuthentication(""", "message": 42"""));

        var wrong = result.Errors.Single(f => f.RuleId == Rules.AttributeWrongType);
        await Assert.That(wrong.AttributePath).IsEqualTo("message");
    }

    [Test]
    public async Task TypeUidMismatch_IsError()
    {
        var result = Validate(MinimalAuthentication().Replace("300201", "300299"));

        // 300299 also implies activity 99 without sibling, so filter for the uid rule.
        await Assert.That(result.Errors.Select(f => f.RuleId)).Contains(Rules.TypeUidMismatch);
    }

    [Test]
    public async Task UndefinedEnumValue_IsWarning()
    {
        var result = Validate(MinimalAuthentication().Replace("\"severity_id\": 1", "\"severity_id\": 42"));

        var warning = result.Warnings.Single(f => f.RuleId == Rules.EnumValueUnknown);
        await Assert.That(warning.AttributePath).IsEqualTo("severity_id");
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task EnumOther_WithoutSibling_IsError()
    {
        var result = Validate(MinimalAuthentication()
            .Replace("\"activity_id\": 1", "\"activity_id\": 99")
            .Replace("300201", "300299"));

        var missing = result.Errors.Single(f => f.RuleId == Rules.EnumSiblingMissing);
        await Assert.That(missing.AttributePath).IsEqualTo("activity_id");
    }

    [Test]
    public async Task EnumSiblingCaptionMismatch_IsWarning()
    {
        var result = Validate(MinimalAuthentication(", \"activity_name\": \"Wrong Caption\""));

        var mismatch = result.Warnings.Single(f => f.RuleId == Rules.EnumSiblingMismatch);
        await Assert.That(mismatch.AttributePath).IsEqualTo("activity_id");
    }

    [Test]
    public async Task ConstraintAtLeastOne_IsEnforced()
    {
        // authentication constrains at_least_one(service, dst_endpoint); the minimal event
        // satisfies it with dst_endpoint, so removing it must fail the constraint.
        var satisfied = Validate(MinimalAuthentication());
        await Assert.That(satisfied.Errors.Select(f => f.RuleId)).DoesNotContain(Rules.ConstraintAtLeastOneFailed);

        var violated = Validate(MinimalAuthentication()
            .Replace("\"dst_endpoint\": { \"ip\": \"10.0.0.1\" }", "\"unmapped\": {}"));

        await Assert.That(violated.Errors.Select(f => f.RuleId)).Contains(Rules.ConstraintAtLeastOneFailed);
    }

    [Test]
    public async Task DeprecatedAttribute_IsWarning()
    {
        var result = Validate(MinimalAuthentication()
            .Replace("\"user\": { \"name\": \"janedoe1\" }", "\"user\": { \"name\": \"j\", \"credential_uid\": \"abc\" }"));

        var deprecated = result.Warnings.Single(f => f.RuleId == Rules.AttributeDeprecated);
        await Assert.That(deprecated.AttributePath).IsEqualTo("user.credential_uid");
        await Assert.That(deprecated.Since).IsEqualTo("1.6.0");
    }

    [Test]
    public async Task DeprecatedClass_IsWarning()
    {
        var result = Validate("""{"class_uid": 2001, "metadata": {"version": "1.9.0"}}""");

        await Assert.That(result.Warnings.Select(f => f.RuleId)).Contains(Rules.ClassDeprecated);
    }

    [Test]
    public async Task OlderVersion_IsWarning_NewerIsError()
    {
        var older = Validate(MinimalAuthentication().Replace("1.9.0", "1.5.0"));
        await Assert.That(older.Warnings.Select(f => f.RuleId)).Contains(Rules.VersionOlderThanSchema);

        var newer = Validate(MinimalAuthentication().Replace("1.9.0", "2.0.0"));
        await Assert.That(newer.Errors.Select(f => f.RuleId)).Contains(Rules.VersionIncompatible);

        var dev = Validate(MinimalAuthentication().Replace("1.9.0", "0.9.0"));
        await Assert.That(dev.Errors.Select(f => f.RuleId)).Contains(Rules.VersionIncompatible);
    }

    [Test]
    public async Task InvalidObservableReference_IsError()
    {
        var result = Validate(MinimalAuthentication(
            """, "observables": [{"name": "no.such.path", "type_id": 1}]"""));

        var invalid = result.Errors.Single(f => f.RuleId == Rules.ObservableNameUnresolved);
        await Assert.That(invalid.AttributePath).IsEqualTo("observables[0].name");
    }

    [Test]
    public async Task ValidObservableReference_IsAccepted()
    {
        var result = Validate(MinimalAuthentication(
            """, "observables": [{"name": "user.name", "type_id": 4}]"""));

        await Assert.That(result.Errors.Select(f => f.RuleId)).DoesNotContain(Rules.ObservableNameUnresolved);
    }

    [Test]
    public async Task PortOutOfRange_IsError()
    {
        var result = Validate(MinimalAuthentication(
            """, "dst_endpoint": {"ip": "10.0.0.1", "port": 99999}"""));

        var range = result.Errors.Single(f => f.RuleId == Rules.AttributeValueOutOfRange);
        await Assert.That(range.AttributePath).IsEqualTo("dst_endpoint.port");
    }

    [Test]
    public async Task RecommendedMissing_WarnsOnlyWhenOptedIn()
    {
        var quiet = Validate(MinimalAuthentication());
        await Assert.That(quiet.Warnings.Select(f => f.RuleId)).DoesNotContain(Rules.AttributeRecommendedMissing);

        var verbose = Validate(MinimalAuthentication(), new ValidationOptions { WarnOnMissingRecommended = true });
        await Assert.That(verbose.Warnings.Select(f => f.RuleId)).Contains(Rules.AttributeRecommendedMissing);
    }

    [Test]
    public async Task IgnoredRules_AreSuppressed()
    {
        var options = new ValidationOptions { IgnoredRules = [Rules.AttributeUnknown] };
        var result = Validate(MinimalAuthentication(""", "not_in_schema": 1"""), options);

        await Assert.That(result.Findings.Select(f => f.RuleId)).DoesNotContain(Rules.AttributeUnknown);
    }
}
