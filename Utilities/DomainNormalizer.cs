using System.Globalization;
using System.Text;

namespace AntiSpamBot.Utilities;

public static class DomainNormalizer
{
    private static readonly IdnMapping Idn = new();
    private static readonly Dictionary<Rune, string> Confusables = new()
    {
        [new Rune('а')] = "a", [new Rune('Α')] = "a", [new Rune('α')] = "a",
        [new Rune('е')] = "e", [new Rune('Ε')] = "e", [new Rune('ε')] = "e",
        [new Rune('о')] = "o", [new Rune('Ο')] = "o", [new Rune('ο')] = "o",
        [new Rune('р')] = "p", [new Rune('Ρ')] = "p", [new Rune('ρ')] = "p",
        [new Rune('с')] = "c", [new Rune('С')] = "c", [new Rune('ϲ')] = "c",
        [new Rune('х')] = "x", [new Rune('Χ')] = "x", [new Rune('χ')] = "x",
        [new Rune('у')] = "y", [new Rune('Υ')] = "y", [new Rune('γ')] = "y",
        [new Rune('і')] = "i", [new Rune('ї')] = "i", [new Rune('Ι')] = "i",
        [new Rune('ӏ')] = "l", [new Rune('ⅼ')] = "l",
        [new Rune('ԁ')] = "d", [new Rune('ԛ')] = "q",
        [new Rune('Ь')] = "b", [new Rune('ъ')] = "b",
        [new Rune('ѕ')] = "s", [new Rune('Տ')] = "s",
        [new Rune('ԝ')] = "w", [new Rune('ᴡ')] = "w",
        [new Rune('ո')] = "n", [new Rune('ս')] = "u"
    };

    public static string ExtractHost(string value)
    {
        var candidate = value.Trim();
        if (candidate.Length == 0)
        {
            return "";
        }

        candidate = StripInvisible(candidate);
        if (!candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            candidate = "https://" + candidate;
        }

        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            ? NormalizeHost(uri.Host)
            : "";
    }

    public static string NormalizeHost(string host)
    {
        host = StripInvisible(host)
            .Trim()
            .Trim('.')
            .Normalize(NormalizationForm.FormKC)
            .ToLowerInvariant();

        try
        {
            host = Idn.GetUnicode(host);
        }
        catch (ArgumentException)
        {
        }

        return host.Trim('.').ToLowerInvariant();
    }

    public static string ToAsciiHost(string host)
    {
        host = NormalizeHost(host);
        try
        {
            return Idn.GetAscii(host).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return host;
        }
    }

    public static string ToSkeleton(string host, bool mapConfusables, bool mapLeetspeak)
    {
        host = NormalizeHost(host);
        var builder = new StringBuilder(host.Length);

        foreach (var rune in host.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (mapConfusables && Confusables.TryGetValue(rune, out var replacement))
            {
                builder.Append(replacement);
                continue;
            }

            var text = rune.ToString();
            if (mapLeetspeak)
            {
                text = text switch
                {
                    "0" => "o",
                    "1" or "!" or "|" => "i",
                    "3" => "e",
                    "4" or "@" => "a",
                    "5" or "$" => "s",
                    "7" => "t",
                    _ => text
                };
            }

            builder.Append(text);
        }

        return builder.ToString().ToLowerInvariant();
    }

    private static string StripInvisible(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var rune in value.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(rune.ToString());
        }

        return builder.ToString();
    }
}
