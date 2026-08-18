using System.Text;
using System.Text.RegularExpressions;

namespace Ocsf.Generator;

/// <summary>Converts schema descriptions (which embed HTML) into XML doc comment text.</summary>
public static partial class DocFormatter
{
    [GeneratedRegex(@"<br\s*/?>|</p>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakTags();

    [GeneratedRegex(@"<p[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ParagraphOpenTags();

    [GeneratedRegex(@"<code[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex CodeOpenTags();

    [GeneratedRegex(@"</code>", RegexOptions.IgnoreCase)]
    private static partial Regex CodeCloseTags();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex OtherTags();

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex WhitespaceRuns();

    private const char CodeOpen = '\uE000';
    private const char CodeClose = '\uE001';
    private const char Lt = '\uE002';
    private const char Gt = '\uE003';

    /// <summary>Converts embedded HTML to XML doc text: code becomes &lt;c&gt;, line break
    /// tags become newlines, other tags are stripped, and the result is XML-escaped.</summary>
    public static string ToXmlDocText(string html)
    {
        var s = LineBreakTags().Replace(html, "\n");
        s = ParagraphOpenTags().Replace(s, "\n");
        s = CodeOpenTags().Replace(s, CodeOpen.ToString());
        s = CodeCloseTags().Replace(s, CodeClose.ToString());
        s = OtherTags().Replace(s, "");

        s = DecodeEntities(s);

        s = s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        s = s.Replace(Lt.ToString(), "&lt;").Replace(Gt.ToString(), "&gt;")
             .Replace(CodeOpen.ToString(), "<c>").Replace(CodeClose.ToString(), "</c>");

        s = WhitespaceRuns().Replace(s, " ");
        var lines = s.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return string.Join("\n", lines);
    }

    /// <summary>Converts embedded HTML to plain text, for [Obsolete] messages and similar.</summary>
    public static string ToPlainText(string html)
    {
        var s = LineBreakTags().Replace(html, " ");
        s = ParagraphOpenTags().Replace(s, " ");
        s = OtherTags().Replace(s, "");
        s = DecodeEntities(s);
        s = s.Replace(Lt, '<').Replace(Gt, '>');
        return WhitespaceRuns().Replace(s, " ").Trim();
    }

    /// <summary>Escapes text for use inside a C# string literal.</summary>
    public static string CSharpStringLiteral(string text)
    {
        var sb = new StringBuilder(text.Length + 2);
        sb.Append('"');
        foreach (var c in text)
        {
            switch (c)
            {
                case '\\': sb.Append(@"\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append(@"\n"); break;
                case '\r': break;
                case '\t': sb.Append(@"\t"); break;
                default:
                    if (char.IsControl(c))
                        sb.Append($"\\u{(int)c:x4}");
                    else
                        sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    private static string DecodeEntities(string s) => s
        .Replace("&nbsp;", " ")
        .Replace("&quot;", "\"")
        .Replace("&#39;", "'")
        .Replace("&apos;", "'")
        .Replace("&lt;", Lt.ToString())
        .Replace("&gt;", Gt.ToString())
        .Replace("&amp;", "&");
}
