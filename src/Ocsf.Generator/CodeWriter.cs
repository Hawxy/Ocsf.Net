using System.Text;

namespace Ocsf.Generator;

/// <summary>Indentation-aware writer producing LF line endings.</summary>
public sealed class CodeWriter
{
    private readonly StringBuilder _sb = new();
    private int _indent;

    public void Indent() => _indent++;

    public void Dedent() => _indent--;

    public void WriteLine(string line = "")
    {
        if (line.Length > 0)
            _sb.Append(' ', _indent * 4).Append(line);
        _sb.Append('\n');
    }

    /// <summary>Writes an XML doc summary from newline-separated, already-escaped text.</summary>
    public void WriteDocSummary(string text)
    {
        WriteLine("/// <summary>");
        foreach (var line in text.Split('\n'))
            WriteLine($"/// {line}".TrimEnd());
        WriteLine("/// </summary>");
    }

    public override string ToString() => _sb.ToString();
}
