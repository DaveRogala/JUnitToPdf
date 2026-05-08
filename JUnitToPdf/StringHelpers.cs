using System.Text;

namespace JUnitToPdf;

static class StringHelpers
{
    public static string Clip(string? s, int max)
    {
        s ??= "";
        if (s.Length <= max) return s;
        return s[..max] + "…";
    }

    public static string NormalizeWhitespace(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var sb = new StringBuilder(s.Length);
        bool lastWasWs = false;
        foreach (var ch in s)
        {
            var isWs = char.IsWhiteSpace(ch);
            if (isWs)
            {
                if (!lastWasWs) sb.Append(' ');
            }
            else
            {
                sb.Append(ch);
            }
            lastWasWs = isWs;
        }
        return sb.ToString().Trim();
    }

    // Converts underscores to spaces, inserts spaces at camelCase/digit boundaries, and
    // optionally inserts a space after dots to allow wrapping on namespaces.
    public static string PrettyIdentifier(string s, bool spaceAfterDots)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";

        s = s.Replace("_", " ");
        if (spaceAfterDots)
            s = s.Replace(".", ". ");

        var sb = new StringBuilder(s.Length + 16);
        char prev = '\0';
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];

            if (i > 0)
            {
                if ((char.IsLower(prev) && char.IsUpper(c)) || (char.IsDigit(prev) && char.IsLetter(c)))
                {
                    if (sb.Length > 0 && sb[^1] != ' ')
                        sb.Append(' ');
                }
            }

            sb.Append(c);
            prev = c;
        }

        return NormalizeWhitespace(sb.ToString());
    }

    public static string ShortClassName(string? full)
    {
        if (string.IsNullOrWhiteSpace(full)) return "(no classname)";

        var s = full;

        var plus = s.LastIndexOf('+');
        if (plus >= 0 && plus < s.Length - 1)
            s = s[(plus + 1)..];

        var dot = s.LastIndexOf('.');
        if (dot >= 0 && dot < s.Length - 1)
            s = s[(dot + 1)..];

        return s;
    }
}
