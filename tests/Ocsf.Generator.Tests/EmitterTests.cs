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
    public async Task ExtensionObjectReferences_AreTyped()
    {
        var outputs = Emitter.EmitAll(Schema.Value);
        var startupItem = outputs.Single(o => o.RelativePath.EndsWith("Objects/StartupItem.g.cs")).Content;
        var process = outputs.Single(o => o.RelativePath.EndsWith("Objects/Process.g.cs")).Content;

        await Assert.That(startupItem).Contains("public WinService? WinService { get; set; }");
        await Assert.That(process).Contains("public List<WinService>? HostedServices { get; set; }");
    }

    [Test]
    public async Task EmitAll_ProducesObjectsBaseEventAndClasses()
    {
        var outputs = Emitter.EmitAll(Schema.Value);

        // 194 objects + OcsfEvent partial + 87 event classes + serializer context + event reader
        // + 3 validation registry files
        await Assert.That(outputs.Count).IsEqualTo(287);
    }

    [Test]
    public async Task WindowsServiceActivity_HasExpectedShape()
    {
        var outputs = Emitter.EmitAll(Schema.Value);
        var cls = outputs.Single(o => o.RelativePath.EndsWith("Events/SystemActivity/WindowsServiceActivity.g.cs")).Content;

        await Assert.That(cls).Contains("namespace Ocsf.Events.SystemActivity;");
        await Assert.That(cls).Contains("[OcsfEventClass(201004, 1, \"windows_service_activity\", Extension = \"win\", ExtensionUid = 2)]");
        await Assert.That(cls).Contains("public const int EventClassUid = 201004;");
        await Assert.That(cls).Contains("public Objects.WinService? WinService { get; set; }");
        await Assert.That(cls).Contains("public void SetActivity(WindowsServiceActivityActivityId activityId, string? activityName = null)");
        await Assert.That(cls).Contains("Part of the <c>win</c> (Windows) extension.");
    }

    [Test]
    public async Task ExtensionTypeNames_ArePrefixedWithoutStutter()
    {
        var outputs = Emitter.EmitAll(Schema.Value);
        var paths = outputs.Select(o => o.RelativePath.Replace('\\', '/')).ToList();

        await Assert.That(paths).Contains("src/Ocsf/Generated/Objects/WinRegKey.g.cs");
        await Assert.That(paths).Contains("src/Ocsf/Generated/Objects/WinRegValue.g.cs");
        await Assert.That(paths).Contains("src/Ocsf/Generated/Objects/WinService.g.cs");
        await Assert.That(paths).Contains("src/Ocsf/Generated/Objects/WinResource.g.cs");
        await Assert.That(paths).Contains("src/Ocsf/Generated/Events/SystemActivity/WinRegistryKeyActivity.g.cs");
        await Assert.That(paths).Contains("src/Ocsf/Generated/Events/Discovery/WinPrefetchQuery.g.cs");
        foreach (var (path, content) in outputs)
        {
            await Assert.That(content.Contains("WinWinService")).IsFalse().Because($"{path} contains a stuttered name");
        }

        // reg_key gets the extension prefix; win_service already carries it.
        var regKey = outputs.Single(o => o.RelativePath.EndsWith("Objects/WinRegKey.g.cs")).Content;
        await Assert.That(regKey).Contains("[OcsfObject(\"reg_key\", Extension = \"win\", ExtensionUid = 2)]");
        await Assert.That(regKey).Contains("public class WinRegKey : OcsfObject");

        // The deprecated discovery-query classes keep [Obsolete].
        var prefetch = outputs.Single(o => o.RelativePath.EndsWith("Events/Discovery/WinPrefetchQuery.g.cs")).Content;
        await Assert.That(prefetch).Contains("[Obsolete(");
    }

    [Test]
    public async Task RegistrySpecs_KeyExtensionObjectsByPrefixedName()
    {
        var outputs = Emitter.EmitAll(Schema.Value);
        var objects = outputs.Single(o => o.RelativePath.EndsWith("OcsfSchemaRegistry.Objects.g.cs")).Content;
        var classes = outputs.Single(o => o.RelativePath.EndsWith("OcsfSchemaRegistry.Classes.g.cs")).Content;

        await Assert.That(objects).Contains("objects[\"win/win_service\"] = Object_win_win_service();");
        await Assert.That(objects).Contains("\"win/win_service\"");
        await Assert.That(classes).Contains("classes[201004] = Class_win_windows_service_activity();");
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
        await Assert.That(auth).Contains("public void SetActivity(AuthenticationActivityId activityId, string? activityName = null)");
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
    public async Task SiblingSetters_AreEmittedForEnumAttributesWithSiblings()
    {
        var outputs = Emitter.EmitAll(Schema.Value);
        var auth = outputs.Single(o => o.RelativePath.EndsWith("Events/Iam/Authentication.g.cs")).Content;
        var user = outputs.Single(o => o.RelativePath.EndsWith("Objects/User.g.cs")).Content;

        await Assert.That(auth).Contains(
            "public void SetActivity(AuthenticationActivityId activityId, string? activityName = null)");
        await Assert.That(auth).Contains("TypeUid = EventClassUid * 100L + (long)activityId;");
        await Assert.That(auth).Contains(
            "public void SetStatus(AuthenticationStatusId statusId, string? status = null)");
        await Assert.That(user).Contains(
            "public void SetType(UserTypeId typeId, string? type = null)");
        await Assert.That(auth).Contains("public static class AuthenticationActivityIdExtensions");
        await Assert.That(auth).Contains("TypeName = $\"{ClassName}: {typeCaption}\";");
        await Assert.That(user).Contains("public static string? Caption(this UserTypeId value)");
    }

    [Test]
    public async Task SiblingSetters_SkipArrayEnumsAndDeprecatedAttributes()
    {
        var outputs = Emitter.EmitAll(Schema.Value);
        var dns = outputs.Single(o => o.RelativePath.EndsWith("Events/Network/DnsActivity.g.cs")).Content;
        var hwInfo = outputs.Single(o => o.RelativePath.EndsWith("Objects/DeviceHwInfo.g.cs")).Content;

        // flag_ids is an array enum; its sibling holds an array of labels.
        await Assert.That(dns).DoesNotContain("public void SetFlag");
        // cpu_architecture_id is deprecated; no helper is generated for it.
        await Assert.That(hwInfo).DoesNotContain("public void SetCpuArchitecture(");
    }

    [Test]
    public async Task DeprecatedAttributes_GetObsoleteAttribute()
    {
        var outputs = Emitter.EmitAll(Schema.Value);
        var user = outputs.Single(o => o.RelativePath.EndsWith("Objects/User.g.cs")).Content;

        await Assert.That(user).Contains("[Obsolete(\"Use programmatic_credentials instead. Deprecated since 1.6.0.\")]");
    }
}
