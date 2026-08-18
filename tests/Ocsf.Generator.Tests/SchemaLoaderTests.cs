namespace Ocsf.Generator.Tests;

public class SchemaLoaderTests
{
    [Test]
    public async Task CheckedInSnapshot_ParsesWithExpectedCounts()
    {
        var schema = SchemaLoader.Load(TestPaths.SchemaFile);

        await Assert.That(schema.Version).IsEqualTo("1.9.0");
        await Assert.That(schema.Classes).Count().IsEqualTo(80);
        await Assert.That(schema.Objects).Count().IsEqualTo(190);
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
    public async Task BuildExportUrl_ExcludesExtensionsAndOsUserProfiles()
    {
        var url = SchemaLoader.BuildExportUrl("1.9.0");

        await Assert.That(url).Contains("extensions=&");
        await Assert.That(url).DoesNotContain("linux_users");
        await Assert.That(url).DoesNotContain("macos_users");
    }
}
