namespace Ocsf.Generator.Tests;

public class SchemaLoaderTests
{
    [Test]
    public async Task CheckedInSnapshot_ParsesWithExpectedCounts()
    {
        var schema = SchemaLoader.Load(TestPaths.SchemaFile);

        await Assert.That(schema.Version).IsEqualTo("1.9.0");
        await Assert.That(schema.Classes).Count().IsEqualTo(87);
        await Assert.That(schema.Objects).Count().IsEqualTo(194);
        await Assert.That(schema.Types).Count().IsEqualTo(24);
        await Assert.That(schema.BaseEvent.Attributes.Count).IsGreaterThan(50);
    }

    [Test]
    public async Task CheckedInSnapshot_AuthenticationClass_HasExpectedShape()
    {
        var schema = SchemaLoader.Load(TestPaths.SchemaFile);
        var auth = schema.Classes["authentication"];

        await Assert.That(auth.Uid).IsEqualTo(3002);
        await Assert.That(auth.CategoryUid).IsEqualTo(3);

        var activityId = auth.Attributes["activity_id"];
        await Assert.That(activityId.Enum).IsNotNull();
        await Assert.That(activityId.Enum!["1"].Caption).IsEqualTo("Logon");
        await Assert.That(activityId.Sibling).IsEqualTo("activity_name");

        var user = auth.Attributes["user"];
        await Assert.That(user.Type).IsEqualTo("object_t");
        await Assert.That(user.ObjectType).IsEqualTo("user");

        await Assert.That(auth.Constraints!).ContainsKey("at_least_one");
    }

    [Test]
    public async Task CheckedInSnapshot_WinExtensionEntities_HaveExpectedShape()
    {
        var schema = SchemaLoader.Load(TestPaths.SchemaFile);

        var serviceActivity = schema.Classes["win/windows_service_activity"];
        await Assert.That(serviceActivity.Name).IsEqualTo("windows_service_activity");
        await Assert.That(serviceActivity.Uid).IsEqualTo(201004);
        await Assert.That(serviceActivity.CategoryUid).IsEqualTo(1);
        await Assert.That(serviceActivity.Extension).IsEqualTo("win");
        await Assert.That(serviceActivity.ExtensionId).IsEqualTo(2);
        // Attribute keys are never extension-prefixed, only entity keys and object refs are.
        var winService = serviceActivity.Attributes["win_service"];
        await Assert.That(winService.ObjectType).IsEqualTo("win/win_service");

        var regKey = schema.Objects["win/reg_key"];
        await Assert.That(regKey.Name).IsEqualTo("reg_key");
        await Assert.That(regKey.Extension).IsEqualTo("win");

        // Extension patches to core objects are attribute-tagged.
        var hostedServices = schema.Objects["process"].Attributes["hosted_services"];
        await Assert.That(hostedServices.Extension).IsEqualTo("win");
        await Assert.That(hostedServices.ObjectType).IsEqualTo("win/win_service");
        await Assert.That(hostedServices.IsArray).IsTrue();

        await Assert.That(schema.Extensions!["win"].Uid).IsEqualTo(2);
    }

    [Test]
    public async Task CheckedInSnapshot_OsUserProfiles_ArePresentOnProcess()
    {
        var schema = SchemaLoader.Load(TestPaths.SchemaFile);
        var egid = schema.Objects["process"].Attributes["egid"];

        await Assert.That(egid.Profiles!).Contains("linux/linux_users");
        await Assert.That(egid.Profiles!).Contains("macos/macos_users");
    }

    [Test]
    public async Task BuildExportUrl_UsesV2ExportWithoutQueryParameters()
    {
        var url = SchemaLoader.BuildExportUrl("1.9.0");

        await Assert.That(url).IsEqualTo("https://schema.ocsf.io/1.9.0/export/v2/schema");
    }
}
