using System.Text.Json;

namespace Ocsf.Validation.Tests;

/// <summary>
/// Compares this validator's findings against captured responses from the schema server's
/// POST /api/v2/validate endpoint for the same events (Fixtures/*.server-validation.json).
/// </summary>
public class ServerParityTests
{
    public static IEnumerable<string> FixtureNames() => ["authentication", "windows_service_activity"];

    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Test]
    [MethodDataSource(nameof(FixtureNames))]
    public async Task Sample_MatchesServerFindingCounts(string fixture)
    {
        using var sample = JsonDocument.Parse(await File.ReadAllTextAsync(Fixture($"{fixture}.sample.json")));
        using var server = JsonDocument.Parse(await File.ReadAllTextAsync(Fixture($"{fixture}.server-validation.json")));

        var result = new OcsfValidator().Validate(sample.RootElement);

        var serverErrors = server.RootElement.GetProperty("error_count").GetInt32();
        var serverWarnings = server.RootElement.GetProperty("warning_count").GetInt32();

        await Assert.That(result.ErrorCount).IsEqualTo(serverErrors);
        await Assert.That(result.WarningCount).IsEqualTo(serverWarnings);
    }

    [Test]
    [MethodDataSource(nameof(FixtureNames))]
    public async Task Sample_MatchesServerRuleDistribution(string fixture)
    {
        using var sample = JsonDocument.Parse(await File.ReadAllTextAsync(Fixture($"{fixture}.sample.json")));
        using var server = JsonDocument.Parse(await File.ReadAllTextAsync(Fixture($"{fixture}.server-validation.json")));

        var result = new OcsfValidator().Validate(sample.RootElement);

        var serverErrorRules = server.RootElement.GetProperty("errors").EnumerateArray()
            .GroupBy(e => e.GetProperty("error").GetString()!)
            .ToDictionary(g => g.Key, g => g.Count());
        var ourErrorRules = result.Errors
            .GroupBy(f => f.RuleId)
            .ToDictionary(g => g.Key, g => g.Count());

        await Assert.That(ourErrorRules).IsEquivalentTo(serverErrorRules);

        var serverWarningRules = server.RootElement.GetProperty("warnings").EnumerateArray()
            .Select(e => e.GetProperty("warning").GetString()!)
            .ToHashSet();
        var ourWarningRules = result.Warnings.Select(f => f.RuleId).ToHashSet();

        await Assert.That(ourWarningRules).IsEquivalentTo(serverWarningRules);
    }
}
