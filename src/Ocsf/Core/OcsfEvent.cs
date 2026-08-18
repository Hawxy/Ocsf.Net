using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ocsf;

/// <summary>
/// Base class for all OCSF event classes. The shared base event attributes
/// (time, metadata, severity_id, ...) are declared in the generated partial.
/// </summary>
public abstract partial class OcsfEvent
{
    /// <summary>Attributes not modeled by this SDK (extension attributes, newer schema versions).
    /// Preserved losslessly on round-trip.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; set; }
}
