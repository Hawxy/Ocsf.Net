using System.Text.Json;

namespace Ocsf.Validation.Tests;

/// <summary>
/// Compares this validator's findings against a captured response from the schema server's
/// POST /api/v2/validate endpoint for the same event (Fixtures/authentication.server-validation.json).
/// </summary>
public class ServerParityTests
{
    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Test]
    public async Task AuthenticationSample_MatchesServerFindingCounts()
    {
        using var sample = JsonDocument.Parse(await File.ReadAllTextAsync(Fixture("authentication.sample.json")));
        using var server = JsonDocument.Parse(await File.ReadAllTextAsync(Fixture("authentication.server-validation.json")));

        var result = new OcsfValidator().Validate(sample.RootElement);

        var serverErrors = server.RootElement.GetProperty("error_count").GetInt32();
        var serverWarnings = server.RootElement.GetProperty("warning_count").GetInt32();

        await Assert.That(result.ErrorCount).IsEqualTo(serverErrors);
        await Assert.That(result.WarningCount).IsEqualTo(serverWarnings);
    }

    [Test]
    public async Task AuthenticationSample_MatchesServerRuleDistribution()
    {
        using var sample = JsonDocument.Parse(await File.ReadAllTextAsync(Fixture("authentication.sample.json")));
        using var server = JsonDocument.Parse(await File.ReadAllTextAsync(Fixture("authentication.server-validation.json")));

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
