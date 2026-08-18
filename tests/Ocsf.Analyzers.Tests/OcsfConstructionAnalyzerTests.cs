using static Ocsf.Analyzers.Tests.AnalyzerTestHelper;

namespace Ocsf.Analyzers.Tests;

public class OcsfConstructionAnalyzerTests
{
    private const string FullyPopulatedEvent = """
        using Ocsf;
        using Ocsf.Events.Iam;
        using Ocsf.Objects;

        class Producer
        {
            string Produce()
            {
                var evt = new Authentication
                {
                    Time = OcsfTimestamp.Now,
                    SeverityId = AuthenticationSeverityId.Informational,
                    Metadata = new Metadata
                    {
                        Version = "1.9.0",
                        Product = new Product { Name = "p", VendorName = "v" },
                    },
                    User = new User { Name = "j" },
                    DstEndpoint = new NetworkEndpoint { Ip = "10.0.0.1" },
                };
                evt.SetActivity(AuthenticationActivityId.Logon);
                return OcsfJson.Serialize(evt);
            }
        }
        """;

    [Test]
    public async Task FullyPopulatedEvent_HasNoDiagnostics()
    {
        await VerifyAsync(FullyPopulatedEvent);
    }

    [Test]
    public async Task EmptyMetadata_ReportsMissingRequired()
    {
        var source = """
            using Ocsf;
            using Ocsf.Objects;

            class Producer
            {
                string Make()
                {
                    var metadata = {|#0:new Metadata()|};
                    return OcsfJson.Serialize(metadata);
                }
            }
            """;

        await VerifyAsync(source,
            Diagnostic("OCSF001").WithLocation(0).WithArguments("product", "Product", "Metadata"),
            Diagnostic("OCSF001").WithLocation(0).WithArguments("version", "Version", "Metadata"));
    }

    [Test]
    public async Task NestedInitializer_MetadataWithoutVersion_Reports()
    {
        var source = """
            using Ocsf.Events.Iam;
            using Ocsf.Objects;

            class Producer
            {
                void Make()
                {
                    var evt = new Authentication
                    {
                        Metadata = {|#0:new Metadata { Product = new Product { Name = "p", VendorName = "v" } }|},
                    };
                    Use(evt);
                }

                void Use(object o) { }
            }
            """;

        await VerifyAsync(source,
            Diagnostic("OCSF001").WithLocation(0).WithArguments("version", "Version", "Metadata"));
    }

    [Test]
    public async Task LaterAssignmentsAndSetHelpers_SatisfyRequired()
    {
        var source = """
            using Ocsf;
            using Ocsf.Events.Iam;
            using Ocsf.Objects;

            class Producer
            {
                string Produce()
                {
                    var evt = new Authentication();
                    evt.Time = OcsfTimestamp.Now;
                    evt.SetSeverity(AuthenticationSeverityId.Informational);
                    evt.SetActivity(AuthenticationActivityId.Logon);
                    evt.Metadata = new Metadata
                    {
                        Version = "1.9.0",
                        Product = new Product { Name = "p", VendorName = "v" },
                    };
                    evt.User = new User { Name = "j" };
                    evt.DstEndpoint = new NetworkEndpoint { Ip = "10.0.0.1" };
                    return OcsfJson.Serialize(evt);
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Test]
    public async Task EscapedInstance_SuppressesRequiredDiagnostics()
    {
        var source = """
            using Ocsf.Events.Iam;

            class Producer
            {
                void Produce()
                {
                    var evt = new Authentication();
                    Populate(evt);
                }

                void Populate(Authentication evt) { }
            }
            """;

        await VerifyAsync(source);
    }

    [Test]
    public async Task TerminalSerializeUse_DoesNotSuppress()
    {
        var source = """
            using Ocsf;
            using Ocsf.Events.Iam;

            class Producer
            {
                string Produce()
                {
                    var evt = {|#0:new Authentication()|};
                    return OcsfJson.Serialize(evt);
                }
            }
            """;

        await VerifyAsync(source,
            Diagnostic("OCSF001").WithLocation(0).WithArguments("activity_id", "ActivityId", "Authentication"),
            Diagnostic("OCSF001").WithLocation(0).WithArguments("metadata", "Metadata", "Authentication"),
            Diagnostic("OCSF001").WithLocation(0).WithArguments("severity_id", "SeverityId", "Authentication"),
            Diagnostic("OCSF001").WithLocation(0).WithArguments("time", "Time", "Authentication"),
            Diagnostic("OCSF001").WithLocation(0).WithArguments("user", "User", "Authentication"),
            Diagnostic("OCSF004").WithLocation(0).WithArguments(
                "Authentication", "at least one", "DstEndpoint, Service", ""));
    }

    [Test]
    public async Task SetActivityOther_WithoutLabel_ReportsOcsf002()
    {
        var source = FullyPopulatedEvent.Replace(
            "evt.SetActivity(AuthenticationActivityId.Logon);",
            "{|#0:evt.SetActivity(AuthenticationActivityId.Other)|};");

        await VerifyAsync(source,
            Diagnostic("OCSF002").WithLocation(0).WithArguments("ActivityId", "ActivityName"));
    }

    [Test]
    public async Task SetActivityOther_WithLabel_IsClean()
    {
        var source = FullyPopulatedEvent.Replace(
            "evt.SetActivity(AuthenticationActivityId.Logon);",
            "evt.SetActivity(AuthenticationActivityId.Other, \"custom\");");

        await VerifyAsync(source);
    }

    [Test]
    public async Task InitializerOther_WithoutSibling_ReportsOcsf002()
    {
        var source = FullyPopulatedEvent.Replace(
            "SeverityId = AuthenticationSeverityId.Informational,",
            "SeverityId = AuthenticationSeverityId.Informational,\n            {|#0:StatusId = (AuthenticationStatusId)99|},");

        await VerifyAsync(source,
            Diagnostic("OCSF002").WithLocation(0).WithArguments("StatusId", "Status"));
    }

    [Test]
    public async Task DirectActivityAssignment_ReportsOcsf003()
    {
        var source = FullyPopulatedEvent.Replace(
            "evt.SetActivity(AuthenticationActivityId.Logon);",
            "{|#0:evt.ActivityId = AuthenticationActivityId.Logon|};");

        await VerifyAsync(source,
            Diagnostic("OCSF003").WithLocation(0));
    }

    [Test]
    public async Task MissingConstraintAttributes_ReportsOcsf004()
    {
        // No service or dst_endpoint: violates authentication's at_least_one constraint.
        var source = """
            using Ocsf;
            using Ocsf.Events.Iam;
            using Ocsf.Objects;

            class Producer
            {
                string Produce()
                {
                    var evt = {|#0:new Authentication
                    {
                        Time = OcsfTimestamp.Now,
                        SeverityId = AuthenticationSeverityId.Informational,
                        Metadata = new Metadata
                        {
                            Version = "1.9.0",
                            Product = new Product { Name = "p", VendorName = "v" },
                        },
                        User = new User { Name = "j" },
                    }|};
                    evt.SetActivity(AuthenticationActivityId.Logon);
                    return OcsfJson.Serialize(evt);
                }
            }
            """;

        await VerifyAsync(source,
            Diagnostic("OCSF004").WithLocation(0).WithArguments(
                "Authentication", "at least one", "DstEndpoint, Service", ""));
    }
}
