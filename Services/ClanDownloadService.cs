using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace WindowCaptureOcr.Services;

/// <summary>
/// Downloads <c>clans.json</c> from a configurable URL and saves it next to
/// the executable, overwriting the local copy on success.
///
/// Supported URL formats:
/// <list type="bullet">
///   <item>Google Drive share link:
///     <c>https://drive.google.com/file/d/{id}/view…</c> → rewritten to direct download</item>
///   <item>Google Drive open link:
///     <c>https://drive.google.com/open?id={id}</c> → rewritten to direct download</item>
///   <item>Any other HTTPS URL returning raw JSON.</item>
/// </list>
/// </summary>
public class ClanDownloadService
{
    private static readonly string LocalPath =
        Path.Combine(AppContext.BaseDirectory, "clans.json");

    // Matches /file/d/{id}/ in Google Drive share URLs
    private static readonly Regex GDriveFileId = new(
        @"drive\.google\.com/file/d/([^/?#]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Matches open?id= in Google Drive open URLs
    private static readonly Regex GDriveOpenId = new(
        @"drive\.google\.com/open\?id=([^&]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Downloads the JSON from <paramref name="url"/>, validates it is non-empty,
    /// and atomically replaces the local <c>clans.json</c>.
    /// </summary>
    /// <returns>A user-readable result message.</returns>
    public async Task<DownloadResult> DownloadAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return Fail("No download URL configured. Enter a URL in Settings.");

        string resolved;
        try
        {
            resolved = ResolveUrl(url.Trim());
        }
        catch (Exception ex)
        {
            return Fail($"Invalid URL: {ex.Message}");
        }

        string json;
        try
        {
            using var response = await Http.GetAsync(resolved);
            response.EnsureSuccessStatusCode();
            json = await response.Content.ReadAsStringAsync();
        }
        catch (HttpRequestException ex)
        {
            return Fail($"Download failed: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return Fail("Download timed out after 30 s.");
        }

        if (string.IsNullOrWhiteSpace(json))
            return Fail("Downloaded file is empty.");

        // Basic JSON sanity check
        if (!json.TrimStart().StartsWith("{") && !json.TrimStart().StartsWith("["))
            return Fail("Downloaded content does not look like JSON. Check the URL.");

        // Atomic write: write to .tmp then rename
        var tmpPath = LocalPath + ".tmp";
        try
        {
            await File.WriteAllTextAsync(tmpPath, json);
            File.Move(tmpPath, LocalPath, overwrite: true);
        }
        catch (Exception ex)
        {
            return Fail($"Could not save clans.json: {ex.Message}");
        }

        return Ok($"clans.json updated successfully ({json.Length:N0} bytes).");
    }

    // ── URL rewriting ─────────────────────────────────────────────────────────

    /// <summary>
    /// Converts any known Google Drive share/open link to a direct-download URL.
    /// Non-Google URLs are returned unchanged.
    /// </summary>
    public static string ResolveUrl(string url)
    {
        // /file/d/{id}/view  →  uc?export=download&id={id}&confirm=t
        var m = GDriveFileId.Match(url);
        if (m.Success)
            return $"https://drive.google.com/uc?export=download&id={m.Groups[1].Value}&confirm=t";

        // open?id={id}  →  same
        m = GDriveOpenId.Match(url);
        if (m.Success)
            return $"https://drive.google.com/uc?export=download&id={m.Groups[1].Value}&confirm=t";

        return url;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DownloadResult Ok(string msg)   => new(true,  msg);
    private static DownloadResult Fail(string msg) => new(false, msg);
}

/// <summary>Result of a <see cref="ClanDownloadService.DownloadAsync"/> call.</summary>
public sealed record DownloadResult(bool Success, string Message);
