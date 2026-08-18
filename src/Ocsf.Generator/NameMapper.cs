using System.Text;

namespace Ocsf.Generator;

public static class NameMapper
{
    private static readonly HashSet<string> CSharpKeywords =
    [
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
        "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw",
        "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",
        "virtual", "void", "volatile", "while",
    ];

    /// <summary>Converts a snake_case schema name to a PascalCase C# identifier.</summary>
    public static string PascalCase(string snakeName)
    {
        var sb = new StringBuilder(snakeName.Length);
        var upperNext = true;
        foreach (var c in snakeName)
        {
            if (c is '_' or '-' or ' ')
            {
                upperNext = true;
                continue;
            }
            sb.Append(upperNext ? char.ToUpperInvariant(c) : c);
            upperNext = false;
        }
        if (sb.Length == 0)
            throw new InvalidOperationException($"Name '{snakeName}' maps to an empty identifier.");
        if (char.IsAsciiDigit(sb[0]))
            sb.Insert(0, '_');
        return sb.ToString();
    }

    /// <summary>Escapes an identifier that collides with a C# keyword.</summary>
    public static string Identifier(string name) => CSharpKeywords.Contains(name) ? "@" + name : name;

    /// <summary>
    /// Builds a C# enum member identifier from an OCSF enum caption.
    /// Falls back to the integer value and disambiguates duplicates with it.
    /// </summary>
    public static string EnumMemberName(string caption, string value, ICollection<string> usedNames)
    {
        var sb = new StringBuilder(caption.Length);
        var upperNext = true;
        foreach (var c in caption)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                sb.Append(upperNext ? char.ToUpperInvariant(c) : c);
                upperNext = false;
            }
            else
            {
                upperNext = true;
            }
        }

        var name = sb.Length == 0 ? "Value" + SanitizeValue(value) : sb.ToString();
        if (char.IsAsciiDigit(name[0]))
            name = "_" + name;
        if (usedNames.Contains(name))
            name = name + "_" + SanitizeValue(value);
        usedNames.Add(name);
        return name;
    }

    private static string SanitizeValue(string value) => value.Replace("-", "Minus");
}
