using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ocsf.Converters;

/// <summary>
/// Reads and writes <see cref="OcsfTimestamp"/> as a JSON number of epoch milliseconds.
/// Tolerates numeric strings and fractional values on read, since real-world producers vary.
/// </summary>
public sealed class OcsfTimestampConverter : JsonConverter<OcsfTimestamp>
{
    public override OcsfTimestamp Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.TryGetInt64(out var value)
                    ? new OcsfTimestamp(value)
                    : new OcsfTimestamp((long)reader.GetDouble());

            case JsonTokenType.String:
                var text = reader.GetString();
                if (long.TryParse(text, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                    return new OcsfTimestamp(parsed);
                throw new JsonException($"Cannot convert \"{text}\" to an OCSF timestamp.");

            default:
                throw new JsonException($"Cannot convert {reader.TokenType} to an OCSF timestamp.");
        }
    }

    public override void Write(Utf8JsonWriter writer, OcsfTimestamp value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.EpochMilliseconds);
}
