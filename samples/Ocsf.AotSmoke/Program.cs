using Ocsf;
using Ocsf.Events.Iam;
using Ocsf.Objects;

// Produce: build a typed event and serialize it.
var produced = new Authentication
{
    Time = OcsfTimestamp.Now,
    SeverityId = AuthenticationSeverityId.Informational,
    User = new User { Name = "janedoe1", TypeId = UserTypeId.User },
    Metadata = new Metadata
    {
        Version = "1.9.0",
        Product = new Product { Name = "AotSmoke", VendorName = "ocsf.net" },
    },
    DstEndpoint = new NetworkEndpoint { Ip = "10.0.0.1" },
};
produced.SetActivity(AuthenticationActivityId.Logon);

var json = OcsfJson.Serialize(produced);
Console.WriteLine(json);

// Consume: read it back through class_uid dispatch.
var consumed = OcsfEventReader.Deserialize(json);
if (consumed is not Authentication auth)
{
    Console.Error.WriteLine("FAIL: expected Authentication");
    return 1;
}
if (auth.TypeUid != 300201 || auth.User?.Name != "janedoe1")
{
    Console.Error.WriteLine("FAIL: round-trip mismatch");
    return 1;
}

// Unknown attributes and enum codes must survive without reflection.
var lenient = OcsfEventReader.Deserialize(
    """{"class_uid":3002,"activity_id":47,"vendor_ext":{"a":1}}""");
if (lenient is not Authentication { ActivityId: (AuthenticationActivityId)47 } withExt
    || withExt.AdditionalProperties?.ContainsKey("vendor_ext") != true)
{
    Console.Error.WriteLine("FAIL: leniency round-trip");
    return 1;
}

// Validation must also run reflection-free.
var validation = new Ocsf.Validation.OcsfValidator().Validate(produced);
if (!validation.IsValid)
{
    Console.Error.WriteLine("FAIL: produced event should validate clean:");
    foreach (var finding in validation.Errors)
        Console.Error.WriteLine($"  {finding.RuleId} {finding.AttributePath}: {finding.Message}");
    return 1;
}

var invalid = new Ocsf.Validation.OcsfValidator()
    .Validate(System.Text.Json.JsonDocument.Parse("""{"class_uid":3002}""").RootElement);
if (invalid.IsValid)
{
    Console.Error.WriteLine("FAIL: bare event should have validation errors");
    return 1;
}

// Extension classes: offset uid dispatch, typed win objects, and validation.
var winProduced = new Ocsf.Events.SystemActivity.WindowsServiceActivity
{
    Time = OcsfTimestamp.Now,
    SeverityId = Ocsf.Events.SystemActivity.WindowsServiceActivitySeverityId.Informational,
    Metadata = new Metadata
    {
        Version = "1.9.0",
        Product = new Product { Name = "AotSmoke", VendorName = "ocsf.net" },
    },
    Device = new Device { Hostname = "host-1", TypeId = DeviceTypeId.Virtual },
    Actor = new Actor { Process = new Process { Pid = 4 } },
    WinService = new WinService { Name = "wuauserv", ServiceTypeId = WinServiceServiceTypeId.OwnProcess },
};
winProduced.SetActivity(Ocsf.Events.SystemActivity.WindowsServiceActivityActivityId.Start);

var winJson = OcsfJson.Serialize(winProduced);
if (OcsfEventReader.Deserialize(winJson) is not Ocsf.Events.SystemActivity.WindowsServiceActivity winConsumed
    || winConsumed.ClassUid != 201004
    || winConsumed.WinService?.Name != "wuauserv")
{
    Console.Error.WriteLine("FAIL: win extension round-trip");
    return 1;
}

var winValidation = new Ocsf.Validation.OcsfValidator().Validate(winProduced);
if (!winValidation.IsValid)
{
    Console.Error.WriteLine("FAIL: win extension event should validate clean:");
    foreach (var finding in winValidation.Errors)
        Console.Error.WriteLine($"  {finding.RuleId} {finding.AttributePath}: {finding.Message}");
    return 1;
}

Console.WriteLine("AOT smoke passed.");
return 0;
