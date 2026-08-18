namespace Ocsf.Generator.Tests;

public class NameMapperTests
{
    [Test]
    [Arguments("user", "User")]
    [Arguments("dst_endpoint", "DstEndpoint")]
    [Arguments("type_uid", "TypeUid")]
    [Arguments("ip", "Ip")]
    [Arguments("uid_alt", "UidAlt")]
    [Arguments("cvss", "Cvss")]
    public async Task PascalCase_MapsSnakeCase(string input, string expected)
    {
        await Assert.That(NameMapper.PascalCase(input)).IsEqualTo(expected);
    }

    [Test]
    public async Task EnumMemberName_SanitizesCaptions()
    {
        var used = new HashSet<string>();

        await Assert.That(NameMapper.EnumMemberName("Unknown", "0", used)).IsEqualTo("Unknown");
        await Assert.That(NameMapper.EnumMemberName("5Views", "6", used)).IsEqualTo("_5Views");
        await Assert.That(NameMapper.EnumMemberName("TLS 1.2", "3", used)).IsEqualTo("TLS12");
        await Assert.That(NameMapper.EnumMemberName("Client-side", "4", used)).IsEqualTo("ClientSide");
    }

    [Test]
    public async Task EnumMemberName_DisambiguatesDuplicatesWithValue()
    {
        var used = new HashSet<string>();

        await Assert.That(NameMapper.EnumMemberName("Same", "1", used)).IsEqualTo("Same");
        await Assert.That(NameMapper.EnumMemberName("Same", "2", used)).IsEqualTo("Same_2");
    }

    [Test]
    [Arguments(null, "user", "User")]
    [Arguments("win", "reg_key", "WinRegKey")]
    [Arguments("win", "reg_value", "WinRegValue")]
    [Arguments("win", "win_service", "WinService")]
    [Arguments("win", "win_resource", "WinResource")]
    [Arguments("win", "registry_key_activity", "WinRegistryKeyActivity")]
    [Arguments("win", "registry_value_activity", "WinRegistryValueActivity")]
    [Arguments("win", "registry_key_query", "WinRegistryKeyQuery")]
    [Arguments("win", "registry_value_query", "WinRegistryValueQuery")]
    [Arguments("win", "prefetch_query", "WinPrefetchQuery")]
    [Arguments("win", "windows_service_activity", "WindowsServiceActivity")]
    [Arguments("win", "windows_resource_activity", "WindowsResourceActivity")]
    [Arguments("linux", "kernel_module", "LinuxKernelModule")]
    public async Task ExtensionTypeName_PrefixesWithoutStutter(string? extension, string name, string expected)
    {
        await Assert.That(NameMapper.ExtensionTypeName(extension, name)).IsEqualTo(expected);
    }

    [Test]
    public async Task Identifier_EscapesKeywords()
    {
        await Assert.That(NameMapper.Identifier("class")).IsEqualTo("@class");
        await Assert.That(NameMapper.Identifier("Class")).IsEqualTo("Class");
    }
}
