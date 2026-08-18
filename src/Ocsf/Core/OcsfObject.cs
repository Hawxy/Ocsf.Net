using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ocsf;

/// <summary>Base class for all OCSF objects.</summary>
public abstract class OcsfObject
{
    /// <summary>Attributes not modeled by this SDK (extension attributes, newer schema versions).
    /// Preserved losslessly on round-trip.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; set; }
}
