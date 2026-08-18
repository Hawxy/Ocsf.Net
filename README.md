# Ocsf.Net

Strongly-typed .NET SDK for the [Open Cybersecurity Schema Framework (OCSF)](https://schema.ocsf.io/).

Current Schema Version: **1.9.0**

| Package | Description |
|---|---|
| `Ocsf` | Generated C# classes for all 80 OCSF event classes and 190 objects, with System.Text.Json serialization and full NativeAOT/trimming support. |
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
- All properties are nullable — consumers must tolerate partial events.

## Validating events

```csharp
using Ocsf.Validation;

var validator = new OcsfValidator();
ValidationResult result = validator.Validate(jsonElement);   // or validator.Validate(typedEvent)

foreach (Finding f in result.Errors)
    Console.WriteLine($"{f.RuleId} at {f.AttributePath}: {f.Message}");
```

The rule set mirrors the schema server's validator (verified against live
`POST /api/v2/validate` responses): required attributes (recursive), unknown attributes with
profile filtering per `metadata.profiles`, JSON type and range/regex/max-length checks, enum
membership, the `Other (99)` sibling-label rule, `at_least_one`/`just_one` constraints,
`type_uid` consistency, `metadata.version` compatibility, observable path references, and
deprecation warnings. `ValidationOptions` controls recommended-attribute warnings and rule
suppression.

## Design notes

- **Timestamps** (`timestamp_t`) are epoch milliseconds on the wire; the `OcsfTimestamp`
  struct converts implicitly to/from `long` and `DateTimeOffset`.
- **Enums**: integer-coded schema enums become C# enums per class/object
  (`AuthenticationActivityId`, `UserTypeId`, ...) since OCSF classes extend enum value sets
  individually. String-coded enums stay `string`.
- **Sibling-aware setters**: every non-array enum attribute with a sibling label gets a
  generated `Set*` helper (`SetStatus(id)`, `user.SetType(id)`, ...) that assigns the enum and
  defaults the sibling label to the schema caption, per the spec's "both should be populated"
  guidance; pass an explicit label for source-specific values, which the spec requires for
  `Other (99)`. `SetActivity` additionally recomputes `type_uid` and `type_name`
  (`"Class Caption: Activity Caption"`). Every enum also gets a `Caption()` extension.
- **Producer responsibilities not automated** (kept manual so consumption stays lossless —
  defaults injected at construction would be re-emitted when round-tripping partial events):
  `metadata.version`, `Unknown (0)` defaults for unpopulatable required enums, and populating
  the `observables` array (schema observable markers are a candidate for a future helper).
- **Profiles** are pre-merged into classes by the schema export; profile-sourced properties
  are ordinary optional properties (provenance noted in the XML docs).
- **Extensions** (`win/`, `linux/`) are not included in v1; attributes referencing extension
  objects are typed `JsonElement`.
- **Deprecated** classes and attributes are generated with `[Obsolete]` so consumers can
  still read events from older producers.

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

CI runs `Test VerifyGenerated AotSmoke` on pushes and PRs to `main`; publishing is a manual
`workflow_dispatch` (`Manual Nuget Push`) that authenticates to nuget.org via
[trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing):
the `NuGet/login` action exchanges the workflow's OIDC token for a short-lived API key, so no
long-lived key is stored — only the `NUGET_USER` secret (the nuget.org profile that owns the
trusted publishing policy). The package version is set in `Package.Build.props`.

## Regenerating from the schema

Generated code lives under `src/Ocsf/Generated` and `src/Ocsf.Validation/Generated` and is
committed. To regenerate (e.g. for a new schema release):

```
dotnet run --project src/Ocsf.Generator -- fetch --version 1.9.0
dotnet run --project src/Ocsf.Generator -- generate
dotnet run --project src/Ocsf.Generator -- verify
```

`fetch` snapshots the compiled schema export into `schema/`; `generate` rewrites the
generated trees deterministically (stable ordering, LF endings) so schema bumps produce
reviewable diffs; `verify` fails if the checked-in code drifts from the snapshot (enforced in CI).

## License

Apache-2.0, matching the OCSF schema.
