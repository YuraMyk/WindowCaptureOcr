using System.IO;
using System.Text.Json;
using WindowCaptureOcr.Models;

namespace WindowCaptureOcr.Services;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> from/to <c>settings.json</c>
/// next to the executable. Thread-safe for reads; callers must serialise writes.
/// </summary>
public class SettingsService
{
    private static readonly string FilePath =
        Path.Combine(AppContext.BaseDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented             = true,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas       = true,
        ReadCommentHandling       = JsonCommentHandling.Skip,
    };

    // In-memory singleton kept for the app lifetime
    private AppSettings _current;

    public SettingsService()
    {
        _current = LoadFromDisk();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Returns the current (in-memory) settings. Never null.</summary>
    public AppSettings Current => _current;

    /// <summary>Persists <paramref name="settings"/> to disk and updates the cache.</summary>
    public void Save(AppSettings settings)
    {
        _current = settings;
        try
        {
            var json = JsonSerializer.Serialize(settings, JsonOpts);
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Ignore write errors (read-only filesystem, etc.)
        }
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private static AppSettings LoadFromDisk()
    {
        if (!File.Exists(FilePath)) return new AppSettings();
        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOpts)
                   ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }
}
