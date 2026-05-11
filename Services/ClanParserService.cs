using System.Text.RegularExpressions;

namespace WindowCaptureOcr.Services;

/// <summary>
/// Extracts a clan name from raw OCR text.
///
/// Per spec, a valid clan name is a single token: [A-Za-z0-9_]{2,}
/// (letters, digits, underscores — no spaces).
///
/// Strategy (first match wins):
///   1. "Clan {TOKEN} Alliance"  — fully anchored, highest confidence
///   2. "Clan {TOKEN}"           — left-anchor only (Alliance cut off / not yet rendered)
///   3. "{TOKEN} Alliance"       — right-anchor only (Clan keyword missing / OCR noise)
///   4. Bare line                — entire line is a valid token (last resort fallback)
/// </summary>
public static class ClanParserService
{
    // Keywords that must never be returned as a clan name
    private static readonly HashSet<string> StopWords =
        new(StringComparer.OrdinalIgnoreCase)
        { "Clan", "Alliance", "No", "The", "A", "An" };

    // A valid token: at least 2 word-chars, surrounded by word boundaries
    private const string Token = @"[A-Za-z0-9_]{2,}";

    // ── Compiled patterns ────────────────────────────────────────────────────

    // 1. Clan TOKEN Alliance
    private static readonly Regex P1 = new(
        $@"(?i)\bClan\b\s+\b({Token})\b\s+\bAlliance\b",
        RegexOptions.Compiled);

    // 2. Clan TOKEN  (Alliance absent / cut off)
    private static readonly Regex P2 = new(
        $@"(?i)\bClan\b\s+\b({Token})\b",
        RegexOptions.Compiled);

    // 3. TOKEN Alliance  (Clan absent / OCR noise)
    private static readonly Regex P3 = new(
        $@"(?i)\b({Token})\b\s+\bAlliance\b",
        RegexOptions.Compiled);

    // 4. Bare line: the whole trimmed line is a single valid token
    private static readonly Regex P4 = new(
        $@"^{Token}$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the clan name found in <paramref name="ocrText"/>,
    /// or <c>null</c> if nothing plausible is found.
    /// </summary>
    public static string? ExtractClanName(string ocrText)
    {
        if (string.IsNullOrWhiteSpace(ocrText)) return null;

        var text = ocrText.Replace("\r\n", "\n").Replace("\r", "\n");

        return TryRegex(P1, text)
            ?? TryRegex(P2, text)
            ?? TryRegex(P3, text)
            ?? TryBareLine(text);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string? TryRegex(Regex regex, string text)
    {
        var m = regex.Match(text);
        if (!m.Success) return null;
        var candidate = m.Groups[1].Value.Trim();
        return IsValid(candidate) ? candidate : null;
    }

    private static string? TryBareLine(string text)
    {
        foreach (Match m in P4.Matches(text))
        {
            var candidate = m.Value.Trim();
            if (IsValid(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>Returns true if the candidate is non-empty and not a stop-word.</summary>
    private static bool IsValid(string candidate) =>
        !string.IsNullOrEmpty(candidate) && !StopWords.Contains(candidate);
}
