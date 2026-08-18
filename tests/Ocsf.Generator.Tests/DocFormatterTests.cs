namespace Ocsf.Generator.Tests;

public class DocFormatterTests
{
    [Test]
    public async Task ConvertsCodeTagsToXmlDocC()
    {
        var result = DocFormatter.ToXmlDocText("For example <code>1618524549901</code>.");

        await Assert.That(result).IsEqualTo("For example <c>1618524549901</c>.");
    }

    [Test]
    public async Task EscapesLiteralAngleBracketsFromEntities()
    {
        var result = DocFormatter.ToXmlDocText("Header <code>'User &lt;u@example.com&gt;'</code>.");

        await Assert.That(result).IsEqualTo("Header <c>'User &lt;u@example.com&gt;'</c>.");
    }

    [Test]
    public async Task ConvertsBreakTagsToNewlinesAndStripsOtherTags()
    {
        var result = DocFormatter.ToXmlDocText("First.<br>Second <b>bold</b>.<p>Third.</p>");

        await Assert.That(result).IsEqualTo("First.\nSecond bold.\nThird.");
    }

    [Test]
    public async Task EscapesAmpersands()
    {
        var result = DocFormatter.ToXmlDocText("Files &amp; folders & things.");

        await Assert.That(result).IsEqualTo("Files &amp; folders &amp; things.");
    }

    [Test]
    public async Task ToPlainText_StripsAllMarkup()
    {
        var result = DocFormatter.ToPlainText("Use the <code>evidence_info</code> class.<br>See &quot;docs&quot;.");

        await Assert.That(result).IsEqualTo("Use the evidence_info class. See \"docs\".");
    }

    [Test]
    public async Task CSharpStringLiteral_EscapesQuotesAndBackslashes()
    {
        var result = DocFormatter.CSharpStringLiteral("a \"quoted\" \\path");

        await Assert.That(result).IsEqualTo("\"a \\\"quoted\\\" \\\\path\"");
    }
}
