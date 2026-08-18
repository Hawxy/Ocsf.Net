namespace Ocsf.Generator.Tests;

public class EmitterTests
{
    private static readonly Lazy<ExportSchema> Schema = new(() => SchemaLoader.Load(TestPaths.SchemaFile));

    [Test]
    public async Task EmitAll_IsDeterministic()
    {
        var first = Emitter.EmitAll(Schema.Value);
        var second = Emitter.EmitAll(Schema.Value);

        await Assert.That(second.Count).IsEqualTo(first.Count);
        foreach (var ((pathA, contentA), (pathB, contentB)) in first.Zip(second))
        {
            await Assert.That(pathB).IsEqualTo(pathA);
            await Assert.That(contentB).IsEqualTo(contentA);
        }
    }

    [Test]
    public async Task EmitAll_ProducesOnlyLfLineEndings()
    {
        foreach (var (path, content) in Emitter.EmitAll(Schema.Value))
        {
            await Assert.That(content.Contains('\r')).IsFalse().Because($"{path} contains CR");
        }
    }

    [Test]
    public async Task UserObject_HasExpectedShape()
    {
        var outputs = Emitter.EmitAll(Schema.Value);
        var user = outputs.Single(o => o.RelativePath.EndsWith("Objects/User.g.cs")).Content;

        await Assert.That(user).Contains("[OcsfObject(\"user\")]");
        await Assert.That(user).Contains("public class User : OcsfObject");
        await Assert.That(user).Contains("[JsonPropertyName(\"email_addr\")]");
        await Assert.That(user).Contains("public UserTypeId? TypeId { get; set; }");
        await Assert.That(user).Contains("public enum UserTypeId");
        await Assert.That(user).Contains("Admin = 2,");
        await Assert.That(user).Contains("Other = 99,");
        await Assert.That(user).Contains("public List<Group>? Groups { get; set; }");
    }

    [Test]
    public async Task ExtensionObjectReferences_DegradeToJsonElement()
    {
        var outputs = Emitter.EmitAll(Schema.Value);
        var startupItem = outputs.Single(o => o.RelativePath.EndsWith("Objects/StartupItem.g.cs")).Content;

        await Assert.That(startupItem).Contains("public JsonElement? WinService { get; set; }");
    }

    [Test]
    public async Task EmitAll_ProducesObjectsBaseEventAndClasses()
    {
        var outputs = Emitter.EmitAll(Schema.Value);

        // 190 objects + OcsfEvent partial + 80 event classes + serializer context + event reader
        // + 3 validation registry files
        await Assert.That(outputs.Count).IsEqualTo(276);
    }

    [Test]
    public async Task AuthenticationClass_HasExpectedShape()
    {
        var outputs = Emitter.EmitAll(Schema.Value);
        var auth = outputs.Single(o => o.RelativePath.EndsWith("Events/Iam/Authentication.g.cs")).Content;

        await Assert.That(auth).Contains("namespace Ocsf.Events.Iam;");
        await Assert.That(auth).Contains("[OcsfEventClass(3002, 3, \"authentication\")]");
        await Assert.That(auth).Contains("public const int EventClassUid = 3002;");
        await Assert.That(auth).Contains("TypeUid = EventClassUid * 100L;");
        await Assert.That(auth).Contains("public void SetActivity(AuthenticationActivityId activity)");
        await Assert.That(auth).Contains("public Objects.User? User { get; set; }");
        await Assert.That(auth).Contains("public enum AuthenticationActivityId");
        // Inherited base attributes are not redeclared.
        await Assert.That(auth).DoesNotContain("[JsonPropertyName(\"metadata\")]");
        await Assert.That(auth).DoesNotContain("[JsonPropertyName(\"class_uid\")]");
    }

    [Test]
    public async Task BaseEventPartial_DeclaresSharedNonEnumAttributes()
    {
        var outputs = Emitter.EmitAll(Schema.Value);
        var baseEvent = outputs.Single(o => o.RelativePath.EndsWith("Generated/OcsfEvent.g.cs")).Content;

        await Assert.That(baseEvent).Contains("public abstract partial class OcsfEvent");
        await Assert.That(baseEvent).Contains("public Objects.Metadata? Metadata { get; set; }");
        await Assert.That(baseEvent).Contains("public int? ClassUid { get; set; }");
        await Assert.That(baseEvent).Contains("public long? TypeUid { get; set; }");
        await Assert.That(baseEvent).Contains("public OcsfTimestamp? Time { get; set; }");
        await Assert.That(baseEvent).Contains("public List<Objects.Observable>? Observables { get; set; }");
        // Enum-coded attributes live on concrete classes, not the base.
        await Assert.That(baseEvent).DoesNotContain("[JsonPropertyName(\"severity_id\")]");
        await Assert.That(baseEvent).DoesNotContain("[JsonPropertyName(\"activity_id\")]");
    }

    [Test]
    public async Task SystemCategory_MapsToSystemActivityNamespace()
    {
        var outputs = Emitter.EmitAll(Schema.Value);
        var fileActivity = outputs.Single(o => o.RelativePath.EndsWith("Events/SystemActivity/FileActivity.g.cs")).Content;

        await Assert.That(fileActivity).Contains("namespace Ocsf.Events.SystemActivity;");
    }

    [Test]
    public async Task DeprecatedAttributes_GetObsoleteAttribute()
    {
        var outputs = Emitter.EmitAll(Schema.Value);
        var user = outputs.Single(o => o.RelativePath.EndsWith("Objects/User.g.cs")).Content;

        await Assert.That(user).Contains("[Obsolete(\"Use programmatic_credentials instead. Deprecated since 1.6.0.\")]");
    }
}
