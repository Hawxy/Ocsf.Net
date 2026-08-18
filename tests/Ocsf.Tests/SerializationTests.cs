using System.Text.Json;
using System.Text.Json.Nodes;
using Ocsf.Events.Iam;

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
