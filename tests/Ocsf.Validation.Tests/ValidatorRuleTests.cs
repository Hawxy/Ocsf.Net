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
    public async Task UndefinedEnumValue_IsError()
    {
        var result = Validate(MinimalAuthentication().Replace("\"severity_id\": 1", "\"severity_id\": 42"));

        var error = result.Errors.Single(f => f.RuleId == Rules.EnumValueUnknown);
        await Assert.That(error.AttributePath).IsEqualTo("severity_id");
        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task EnumOther_WithOrWithoutSibling_IsAccepted()
    {
        // The server accepts Other (99) with any label or none; the OCSF002 analyzer
        // covers the authoring-time requirement to supply one.
        var without = Validate(MinimalAuthentication()
            .Replace("\"activity_id\": 1", "\"activity_id\": 99")
            .Replace("300201", "300299"));
        await Assert.That(without.Findings.Select(f => f.RuleId).ToList()).IsEmpty();

        var withLabel = Validate(MinimalAuthentication(", \"activity_name\": \"custom-logon\"")
            .Replace("\"activity_id\": 1", "\"activity_id\": 99")
            .Replace("300201", "300299"));
        await Assert.That(withLabel.Findings.Select(f => f.RuleId).ToList()).IsEmpty();
    }

    [Test]
    public async Task SetActivityWithOtherAndLabel_ProducesNoSiblingFinding()
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
            User = new Ocsf.Objects.User { Name = "j" },
            DstEndpoint = new Ocsf.Objects.NetworkEndpoint { Ip = "10.0.0.1" },
        };
        evt.SetActivity(Ocsf.Events.Iam.AuthenticationActivityId.Other, "custom-logon");

        var result = new OcsfValidator().Validate(evt);

        await Assert.That(result.Findings.Select(f => f.RuleId).ToList()).IsEmpty();
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task EnumSiblingCaptionMismatch_IsWarning()
    {
        var result = Validate(MinimalAuthentication(", \"activity_name\": \"Wrong Caption\""));

        var mismatch = result.Warnings.Single(f => f.RuleId == Rules.EnumSiblingIncorrect);
        await Assert.That(mismatch.AttributePath).IsEqualTo("activity_id");
    }

    [Test]
    public async Task ConstraintAtLeastOne_IsEnforced()
    {
        // authentication constrains at_least_one(service, dst_endpoint); the minimal event
        // satisfies it with dst_endpoint, so removing it must fail the constraint.
        var satisfied = Validate(MinimalAuthentication());
        await Assert.That(satisfied.Errors.Select(f => f.RuleId)).DoesNotContain(Rules.ConstraintFailed);

        var violated = Validate(MinimalAuthentication()
            .Replace("\"dst_endpoint\": { \"ip\": \"10.0.0.1\" }", "\"unmapped\": {}"));

        await Assert.That(violated.Errors.Select(f => f.RuleId)).Contains(Rules.ConstraintFailed);
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
    public async Task JsonPathObservableReference_IsAccepted()
    {
        var result = Validate(MinimalAuthentication(
            """, "observables": [{"name": "$.user.name", "type_id": 4}]"""));

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

    private static string MinimalWinServiceActivity(string winService, string extra = "") => $$"""
        {
            "class_uid": 201004,
            "category_uid": 1,
            "activity_id": 1,
            "type_uid": 20100401,
            "severity_id": 1,
            "time": 1618524549901,
            "metadata": { "version": "1.9.0", "product": { "name": "test", "vendor_name": "test" } },
            "device": { "type_id": 6, "hostname": "host-1" },
            "actor": { "process": { "pid": 42 } },
            "win_service": {{winService}}
            {{extra}}
        }
        """;

    [Test]
    public async Task WinExtensionClass_IsValidated()
    {
        var result = Validate(MinimalWinServiceActivity(
            """{ "name": "wuauserv", "service_type_id": 2 }"""));

        await Assert.That(result.Findings.Select(f => $"{f.RuleId} {f.AttributePath}").ToList()).IsEmpty();
    }

    [Test]
    public async Task WinExtensionObject_SubtreeIsValidated()
    {
        var result = Validate(MinimalWinServiceActivity(
            """{ "cmd_line": 42, "service_type_id": 77 }"""));

        var paths = result.Errors.Select(f => $"{f.RuleId} {f.AttributePath}").ToList();
        await Assert.That(paths).Contains($"{Rules.AttributeRequiredMissing} win_service.name");
        await Assert.That(paths).Contains($"{Rules.AttributeWrongType} win_service.cmd_line");
        await Assert.That(paths).Contains($"{Rules.EnumValueUnknown} win_service.service_type_id");
    }

    [Test]
    public async Task OsUserProfileAttribute_IsGatedOnDeclaredProfiles()
    {
        var json = MinimalWinServiceActivity("""{ "name": "wuauserv", "service_type_id": 2 }""")
            .Replace("\"process\": { \"pid\": 42 }", "\"process\": { \"pid\": 42, \"egid\": 1000 }");

        var undeclared = Validate(json);
        var unknown = undeclared.Errors.Single(f => f.RuleId == Rules.AttributeUnknown);
        await Assert.That(unknown.AttributePath).IsEqualTo("actor.process.egid");

        // egid belongs to both OS user profiles; declaring either one admits it.
        var declared = Validate(json.Replace("\"product\"", "\"profiles\": [\"macos/macos_users\"], \"product\""));
        await Assert.That(declared.Errors.Select(f => f.RuleId)).DoesNotContain(Rules.AttributeUnknown);
    }

    [Test]
    public async Task IgnoredRules_AreSuppressed()
    {
        var options = new ValidationOptions { IgnoredRules = [Rules.AttributeUnknown] };
        var result = Validate(MinimalAuthentication(""", "not_in_schema": 1"""), options);

        await Assert.That(result.Findings.Select(f => f.RuleId)).DoesNotContain(Rules.AttributeUnknown);
    }
}
