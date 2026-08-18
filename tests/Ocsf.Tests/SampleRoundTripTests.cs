using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ocsf.Tests;

public class SampleRoundTripTests
{
    public static IEnumerable<string> SampleFiles() =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "SampleEvents"), "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Order()!;

    [Test]
    [MethodDataSource(nameof(SampleFiles))]
    public async Task OfficialSample_RoundTripsLosslessly(string sampleName)
    {
        var json = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "SampleEvents", sampleName + ".json"));

        var evt = OcsfEventReader.Deserialize(json);
        await Assert.That(evt).IsNotNull();

        var expectedUid = JsonNode.Parse(json)!["class_uid"]!.GetValue<int>();
        await Assert.That(evt!.ClassUid).IsEqualTo(expectedUid);

        var reserialized = OcsfJson.Serialize(evt);
        var expected = StripNulls(JsonNode.Parse(json))!;
        var actual = JsonNode.Parse(reserialized);

        var equal = JsonNode.DeepEquals(expected, actual);
        if (!equal)
        {
            // Surface the first difference to make failures diagnosable.
            var diff = FindFirstDifference(expected, actual, "$");
            await Assert.That(equal).IsTrue().Because($"first difference at {diff}");
        }
    }

    /// <summary>The SDK omits nulls on write, so explicit nulls in samples are normalized away.</summary>
    private static JsonNode? StripNulls(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var cleaned = new JsonObject();
                foreach (var (key, value) in obj)
                {
                    if (value is not null)
                        cleaned[key] = StripNulls(value);
                }
                return cleaned;
            case JsonArray arr:
                var list = new JsonArray();
                foreach (var item in arr)
                    list.Add(StripNulls(item));
                return list;
            default:
                return node?.DeepClone();
        }
    }

    private static string FindFirstDifference(JsonNode? expected, JsonNode? actual, string path)
    {
        if (JsonNode.DeepEquals(expected, actual))
            return "(none)";

        if (expected is JsonObject eo && actual is JsonObject ao)
        {
            foreach (var (key, value) in eo)
            {
                if (!ao.ContainsKey(key))
                    return $"{path}.{key} missing in output";
                if (!JsonNode.DeepEquals(value, ao[key]))
                    return FindFirstDifference(value, ao[key], $"{path}.{key}");
            }
            foreach (var (key, _) in ao)
            {
                if (!eo.ContainsKey(key))
                    return $"{path}.{key} unexpected in output";
            }
            return $"{path} (object)";
        }

        if (expected is JsonArray ea && actual is JsonArray aa)
        {
            if (ea.Count != aa.Count)
                return $"{path} array length {ea.Count} vs {aa.Count}";
            for (var i = 0; i < ea.Count; i++)
            {
                if (!JsonNode.DeepEquals(ea[i], aa[i]))
                    return FindFirstDifference(ea[i], aa[i], $"{path}[{i}]");
            }
        }

        return $"{path}: expected {expected?.ToJsonString()} actual {actual?.ToJsonString()}";
    }
}
