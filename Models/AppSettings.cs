using System.Text.Json.Serialization;

namespace WindowCaptureOcr.Models;

/// <summary>
/// Persisted application settings. Serialized to <c>settings.json</c>
/// next to the executable.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// URL to download <c>clans.json</c> from.
    /// Supports:
    ///   • Google Drive share link: https://drive.google.com/file/d/{id}/view…
    ///   • Google Drive direct:     https://drive.google.com/uc?export=download&amp;id={id}
    ///   • Any raw HTTP/HTTPS URL returning JSON.
    /// Leave empty to use the local file only.
    /// </summary>
    [JsonPropertyName("clansJsonUrl")]
    public string ClansJsonUrl { get; set; } = string.Empty;

    /// <summary>Fuzzy-match threshold (0–1). Default 0.75.</summary>
    [JsonPropertyName("fuzzyThreshold")]
    public double FuzzyThreshold { get; set; } = 0.75;

    /// <summary>OCR capture interval in milliseconds. Default 800.</summary>
    [JsonPropertyName("captureIntervalMs")]
    public int CaptureIntervalMs { get; set; } = 800;
}
