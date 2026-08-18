using System.Text.Json;
using Ocsf.Converters;

namespace Ocsf.Tests;

public class OcsfTimestampTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new OcsfTimestampConverter() },
    };

    [Test]
    public async Task ConvertsToAndFromDateTimeOffset()
    {
        var moment = new DateTimeOffset(2021, 4, 15, 21, 29, 9, 901, TimeSpan.Zero);
        OcsfTimestamp ts = moment;

        await Assert.That(ts.EpochMilliseconds).IsEqualTo(1618522149901);
        await Assert.That(ts.ToDateTimeOffset()).IsEqualTo(moment);
    }

    [Test]
    public async Task ConvertsImplicitlyFromLong()
    {
        OcsfTimestamp ts = 1618524549901;

        long back = ts;
        await Assert.That(back).IsEqualTo(1618524549901);
    }

    [Test]
    [Arguments(0L)]
    [Arguments(-1000L)]
    [Arguments(1618524549901L)]
    public async Task RoundTripsThroughJsonAsNumber(long epochMs)
    {
        var json = JsonSerializer.Serialize(new OcsfTimestamp(epochMs), Options);

        await Assert.That(json).IsEqualTo(epochMs.ToString());
        var back = JsonSerializer.Deserialize<OcsfTimestamp>(json, Options);
        await Assert.That(back.EpochMilliseconds).IsEqualTo(epochMs);
    }

    [Test]
    public async Task ReadToleratesNumericStrings()
    {
        var value = JsonSerializer.Deserialize<OcsfTimestamp>("\"1618524549901\"", Options);

        await Assert.That(value.EpochMilliseconds).IsEqualTo(1618524549901);
    }

    [Test]
    public async Task ReadToleratesFractionalNumbers()
    {
        var value = JsonSerializer.Deserialize<OcsfTimestamp>("1618524549901.7", Options);

        await Assert.That(value.EpochMilliseconds).IsEqualTo(1618524549901);
    }

    [Test]
    public async Task ReadRejectsNonNumericValues()
    {
        await Assert.That(() => JsonSerializer.Deserialize<OcsfTimestamp>("\"not a time\"", Options))
            .Throws<JsonException>();
        await Assert.That(() => JsonSerializer.Deserialize<OcsfTimestamp>("true", Options))
            .Throws<JsonException>();
    }

    [Test]
    public async Task ComparesByEpochMilliseconds()
    {
        var earlier = new OcsfTimestamp(1000);
        var later = new OcsfTimestamp(2000);

        await Assert.That(earlier < later).IsTrue();
        await Assert.That(earlier == new OcsfTimestamp(1000)).IsTrue();
    }
}
