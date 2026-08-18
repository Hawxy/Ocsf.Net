using System.Text.Json;
using System.Text.Json.Nodes;
using Ocsf.Events.Iam;
using Ocsf.Objects;

namespace Ocsf.Tests;

public class SerializationTests
{
    [Test]
    public async Task NewEvent_HasClassificationDefaults()
    {
        var evt = new Authentication();

        await Assert.That(evt.ClassUid).IsEqualTo(3002);
        await Assert.That(evt.CategoryUid).IsEqualTo(3);
        await Assert.That(evt.TypeUid).IsEqualTo(300200L);
        await Assert.That(evt.ClassName).IsEqualTo("Authentication");
    }

    [Test]
    public async Task SetActivity_RecomputesTypeUid()
    {
        var evt = new Authentication();
        evt.SetActivity(AuthenticationActivityId.Logon);

        await Assert.That(evt.ActivityId).IsEqualTo(AuthenticationActivityId.Logon);
        await Assert.That(evt.TypeUid).IsEqualTo(300201L);
    }

    [Test]
    public async Task SetActivity_WithOtherAndLabel_SetsSiblingTypeUidAndTypeName()
    {
        var evt = new Authentication();
        evt.SetActivity(AuthenticationActivityId.Other, "custom-logon");

        await Assert.That(evt.ActivityId).IsEqualTo(AuthenticationActivityId.Other);
        await Assert.That(evt.ActivityName).IsEqualTo("custom-logon");
        await Assert.That(evt.TypeUid).IsEqualTo(300299L);
        await Assert.That(evt.TypeName).IsEqualTo("Authentication: Other");

        var node = JsonNode.Parse(OcsfJson.Serialize(evt))!;
        await Assert.That(node["activity_id"]!.GetValue<int>()).IsEqualTo(99);
        await Assert.That(node["activity_name"]!.GetValue<string>()).IsEqualTo("custom-logon");
    }

    [Test]
    public async Task SetActivity_DefaultsLabelAndTypeNameToCaptions()
    {
        var evt = new Authentication();
        evt.SetActivity(AuthenticationActivityId.Logon);

        await Assert.That(evt.ActivityName).IsEqualTo("Logon");
        await Assert.That(evt.TypeName).IsEqualTo("Authentication: Logon");
        await Assert.That(evt.TypeUid).IsEqualTo(300201L);
    }

    [Test]
    public async Task SiblingSetters_DefaultLabelToCaption()
    {
        var evt = new Authentication();
        evt.SetStatus(AuthenticationStatusId.Success);

        await Assert.That(evt.StatusId).IsEqualTo(AuthenticationStatusId.Success);
        await Assert.That(evt.Status).IsEqualTo("Success");

        evt.SetStatus(AuthenticationStatusId.Failure, "denied by policy");
        await Assert.That(evt.Status).IsEqualTo("denied by policy");
    }

    [Test]
    public async Task SiblingSetters_LeaveSiblingUntouchedForUndefinedCodes()
    {
        var evt = new Authentication();
        evt.SetStatus((AuthenticationStatusId)47);

        await Assert.That((int)evt.StatusId!.Value).IsEqualTo(47);
        await Assert.That(evt.Status).IsNull();
    }

    [Test]
    public async Task Caption_ReturnsSchemaCaptionOrNull()
    {
        await Assert.That(AuthenticationActivityId.Logon.Caption()).IsEqualTo("Logon");
        await Assert.That(AuthenticationActivityId.Other.Caption()).IsEqualTo("Other");
        await Assert.That(((AuthenticationActivityId)47).Caption()).IsNull();
        await Assert.That(Objects.UserTypeId.Admin.Caption()).IsEqualTo("Admin");
    }

    [Test]
    public async Task ObjectSiblingSetters_AssignBothProperties()
    {
        var user = new Objects.User();
        user.SetType(Objects.UserTypeId.Admin);

        await Assert.That(user.TypeId).IsEqualTo(Objects.UserTypeId.Admin);
        await Assert.That(user.Type).IsEqualTo("Admin");
    }

    [Test]
    public async Task Serialize_UsesSnakeCaseAndOmitsNulls()
    {
        var evt = new Authentication
        {
            Time = new OcsfTimestamp(1618524549901),
            SeverityId = AuthenticationSeverityId.Informational,
            User = new Objects.User { Name = "janedoe1" },
        };

        var node = JsonNode.Parse(OcsfJson.Serialize(evt))!;

        await Assert.That(node["class_uid"]!.GetValue<int>()).IsEqualTo(3002);
        await Assert.That(node["time"]!.GetValue<long>()).IsEqualTo(1618524549901);
        await Assert.That(node["severity_id"]!.GetValue<int>()).IsEqualTo(1);
        await Assert.That(node["user"]!["name"]!.GetValue<string>()).IsEqualTo("janedoe1");
        await Assert.That(node.AsObject().ContainsKey("message")).IsFalse();
    }

    [Test]
    public async Task UnknownEnumCodes_RoundTripWithoutThrowing()
    {
        var json = """{"class_uid":3002,"activity_id":47,"severity_id":1}""";

        var evt = (Authentication)OcsfEventReader.Deserialize(json)!;
        await Assert.That((int)evt.ActivityId!.Value).IsEqualTo(47);

        var node = JsonNode.Parse(OcsfJson.Serialize(evt))!;
        await Assert.That(node["activity_id"]!.GetValue<int>()).IsEqualTo(47);
    }

    [Test]
    public async Task UnknownAttributes_ArePreservedViaExtensionData()
    {
        var json = """{"class_uid":3002,"custom_ext":{"a":1},"another":"x"}""";

        var evt = OcsfEventReader.Deserialize(json)!;
        await Assert.That(evt.AdditionalProperties!).ContainsKey("custom_ext");

        var node = JsonNode.Parse(OcsfJson.Serialize(evt))!;
        await Assert.That(node["custom_ext"]!["a"]!.GetValue<int>()).IsEqualTo(1);
        await Assert.That(node["another"]!.GetValue<string>()).IsEqualTo("x");
    }

    [Test]
    public async Task EventReader_ReturnsNullForUnknownOrMissingClassUid()
    {
        await Assert.That(OcsfEventReader.Deserialize("""{"class_uid":999999}""")).IsNull();
        await Assert.That(OcsfEventReader.Deserialize("""{"foo":1}""")).IsNull();
        await Assert.That(OcsfEventReader.Deserialize("[1,2]")).IsNull();
    }

    [Test]
    public async Task EventReader_MapsClassUidToType()
    {
        await Assert.That(OcsfEventReader.GetEventType(3002)).IsEqualTo(typeof(Authentication));
        await Assert.That(OcsfEventReader.GetEventType(123456)).IsNull();
    }

    [Test]
    public async Task OcsfJson_DeserializeTyped()
    {
        var evt = OcsfJson.Deserialize<Authentication>("""{"class_uid":3002,"status_id":1}""");

        await Assert.That(evt!.StatusId).IsEqualTo(AuthenticationStatusId.Success);
    }
}
