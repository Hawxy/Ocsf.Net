# Ocsf.Net

Strongly-typed .NET SDK for the [Open Cybersecurity Schema Framework (OCSF)](https://schema.ocsf.io/).

Current Schema Version: **1.9.0**

| Package | Description |
|---|---|
| `Ocsf` | Generated C# classes for all 87 OCSF event classes and 194 objects (schema extensions included), with System.Text.Json serialization and full NativeAOT/trimming support. |
| `Ocsf.Validation` | Validates JSON events against the OCSF schema, mirroring the rules and severities of the schema server's `POST /api/v2/validate` endpoint. |

Targets `net8.0` and `net10.0`. Fully AOT Compatible.

## Producing events

```csharp
using Ocsf;
using Ocsf.Events.Iam;
using Ocsf.Objects;

var evt = new Authentication
{
    Time = OcsfTimestamp.Now,
    SeverityId = AuthenticationSeverityId.Informational,
    User = new User { Name = "janedoe1", TypeId = UserTypeId.User },
    Metadata = new Metadata
    {
        Version = "1.9.0",
        Product = new Product { Name = "MyProduct", VendorName = "MyVendor" },
    },
    DstEndpoint = new NetworkEndpoint { Ip = "10.0.0.1" },
};
evt.SetActivity(AuthenticationActivityId.Logon);   // sets activity_id and recomputes type_uid
// For source-specific values, pass the label required by the Other (99) rule:
// evt.SetActivity(AuthenticationActivityId.Other, "custom-logon");

string json = OcsfJson.Serialize(evt);
```

Every event class constructor pre-populates `class_uid`, `category_uid`, `type_uid`,
`class_name`, and `category_name`. `type_uid` follows the spec formula
`class_uid * 100 + activity_id` via `SetActivity`.

## Consuming events

```csharp
using Ocsf;

// Dispatches on class_uid; returns null for unknown classes.
OcsfEvent? evt = OcsfEventReader.Deserialize(json);
if (evt is Ocsf.Events.Iam.Authentication auth)
{
    Console.WriteLine(auth.User?.Name);
}
```

Consumption is lossless and lenient by design:

- Unknown attributes (vendor extensions, newer schema versions) round-trip through
  `AdditionalProperties` (`[JsonExtensionData]`).
- Unknown enum codes deserialize without throwing (`(AuthenticationActivityId)47`).
- All properties are nullable, as consumers must tolerate partial events.

## Validating events

```csharp
using Ocsf.Validation;

var validator = new OcsfValidator();
ValidationResult result = validator.Validate(jsonElement);   // or validator.Validate(typedEvent)

foreach (Finding f in result.Errors)
    Console.WriteLine($"{f.RuleId} at {f.AttributePath}: {f.Message}");
```

The rule set mirrors the OCSF schema server's validator:

- Required attributes (recursive)
- Unknown attributes, with profile filtering per `metadata.profiles`
- JSON type and range/regex/max-length checks
- Enum membership
- Sibling-label caption checks
- `at_least_one`/`just_one` constraints
- `type_uid` consistency
- `metadata.version` compatibility
- Observable path references
- Deprecation warnings

`ValidationOptions` controls recommended-attribute warnings and rule suppression.

## Analyzers

The `Ocsf` package ships a Roslyn analyzer that flags common producer mistakes while typing:

| ID | Severity | Checks |
|---|---|---|
| OCSF001 | Warning | Required attribute not populated on a locally constructed event/object |
| OCSF002 | Warning | Enum set to `Other (99)` without an explicit sibling label |
| OCSF003 | Info | `ActivityId` assigned directly, leaving `type_uid` stale, use `SetActivity` |
| OCSF004 | Warning | `at_least_one` / `just_one` constraint visibly violated |

Analysis is intra-method and conservative: instances passed to other methods are assumed to
be populated elsewhere and are not flagged. Suppress any rule via `.editorconfig`, e.g.
`dotnet_diagnostic.OCSF001.severity = none`.

## Design notes

- **Timestamps** (`timestamp_t`) are epoch milliseconds. The `OcsfTimestamp`
  struct converts implicitly to/from `long` and `DateTimeOffset`.
- **Enums** integer-coded schema enums become C# enums per class/object
  (`AuthenticationActivityId`, `UserTypeId`, ...) since OCSF classes extend enum value sets
  individually. String-coded enums stay `string`.
- **Sibling-aware setters** every non-array enum attribute with a sibling label gets a
  generated `Set*` helper (`SetStatus(id)`, `user.SetType(id)`, ...) that assigns the enum and
  defaults the sibling label to the schema caption, as "both should be populated" per the spec.
  Pass an explicit label for source-specific values, which the spec requires for
  `Other (99)`. `SetActivity` additionally recomputes `type_uid` and `type_name`
  (`"Class Caption: Activity Caption"`). Every enum also gets a `Caption()` extension.
- **Producer responsibilities are not automated** `metadata.version` and populating the `observables` array are left to the producer.
- **Profiles** are pre-merged into classes by the schema export. Profile-sourced properties
  are ordinary optional properties. This includes the
  `linux/linux_users` and `macos/macos_users` extension profiles, which add `Auid`, `Egid`,
  `Euid`, and `Group` to `Process` when `metadata.profiles` declares one of those profiles.
- **Deprecated** classes and attributes are generated with `[Obsolete]` so consumers can
  still read events from older producers.

## Extensions

OCSF platform extensions are generated as first-class types into the same namespaces as core.
The `win` extension is the only one that defines entities, with type names being prefixed with 
the extension name unless the schema name already starts with it:

| Schema key | CLR type | class_uid |
|---|---|---|
| `win/reg_key` | `Ocsf.Objects.WinRegKey` | — |
| `win/reg_value` | `Ocsf.Objects.WinRegValue` | — |
| `win/win_service` | `Ocsf.Objects.WinService` | — |
| `win/win_resource` | `Ocsf.Objects.WinResource` | — |
| `win/registry_key_activity` | `Ocsf.Events.SystemActivity.WinRegistryKeyActivity` | 201001 |
| `win/registry_value_activity` | `Ocsf.Events.SystemActivity.WinRegistryValueActivity` | 201002 |
| `win/windows_resource_activity` | `Ocsf.Events.SystemActivity.WindowsResourceActivity` | 201003 |
| `win/windows_service_activity` | `Ocsf.Events.SystemActivity.WindowsServiceActivity` | 201004 |
| `win/registry_key_query` | `Ocsf.Events.Discovery.WinRegistryKeyQuery` | 205004 |
| `win/registry_value_query` | `Ocsf.Events.Discovery.WinRegistryValueQuery` | 205005 |
| `win/prefetch_query` | `Ocsf.Events.Discovery.WinPrefetchQuery` | 205019 |

Generated extension types are marked with
`[OcsfEventClass(..., Extension = "win", ExtensionUid = 2)]` (and `[OcsfObject]` likewise)
for runtime introspection.

## Building

The repo builds with [Nuke](https://nuke.build/); the GitHub workflows are generated from
`build/Build.cs` and invoke the same targets you can run locally:

```
./build.cmd Test               # Release build + full test suite
./build.cmd VerifyGenerated    # fails if generated code drifts from the schema snapshot
./build.cmd AotSmoke           # NativeAOT publish + run of samples/Ocsf.AotSmoke
./build.cmd NugetPack          # packs Ocsf + Ocsf.Validation into artifacts/
./build.cmd NugetPush          # pack + push to nuget.org (requires NugetApiKey)
```

## Regenerating from the schema

Generated code lives under `src/Ocsf/Generated` and `src/Ocsf.Validation/Generated` and is
committed. To regenerate (e.g. for a new schema release):

```
dotnet run --project src/Ocsf.Generator -- fetch --version 1.9.0
dotnet run --project src/Ocsf.Generator -- generate
dotnet run --project src/Ocsf.Generator -- verify
```

`fetch` snapshots the compiled schema export (`/export/v2/schema`, which always includes all
profiles and extensions) into `schema/`; `generate` rewrites the
generated trees deterministically (stable ordering, LF endings) so schema bumps produce
reviewable diffs; `verify` fails if the checked-in code drifts from the snapshot (enforced in CI).

## License

This project is Apache-2.0, in line with upstream. This is a community project and is not endorsed or related to the core OSCF project.
