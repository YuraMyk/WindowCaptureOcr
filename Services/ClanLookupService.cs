using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WindowCaptureOcr.Services;

// ─── Data model ──────────────────────────────────────────────────────────────

public sealed class ClanEntry
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("leader")] public string Leader { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("createdAt")] public string? CreatedAt { get; set; }
    [JsonPropertyName("updatedAt")] public string? UpdatedAt { get; set; }
}

file sealed class ClanDb
{
    [JsonPropertyName("clans")] public List<ClanEntry> Clans { get; set; } = new();
}

// ─── Match method enum ───────────────────────────────────────────────────────

public enum MatchMethod { None, Exact, Contains, Fuzzy }

// ─── Lookup result ───────────────────────────────────────────────────────────

public sealed class ClanLookupResult
{
    public bool Found { get; init; }
    public string ClanName { get; init; } = "";
    public string QueryName { get; init; } = "";
    public string Leader { get; init; } = "";
    public string Status { get; init; } = "";
    public string? UpdatedAt { get; init; }
    public MatchMethod MatchMethod { get; init; }
    public double Similarity { get; init; }
    public int EditDistance { get; init; }

    public bool IsFuzzy => MatchMethod == MatchMethod.Fuzzy;
    public string MatchLabel => MatchMethod switch
    {
        MatchMethod.Exact => "EXACT",
        MatchMethod.Contains => "CONTAINS",
        MatchMethod.Fuzzy => $"FUZZY {Similarity:P0}",
        _ => ""
    };

    public string StatusColour => Status.ToLowerInvariant() switch
    {
        "friendly" => "#78B159",
        "enemy" => "#DD2E44",
        "war" => "#DD2E44",
        "neutral" => "#FDCB58",
        _ => "#CDD6F4",
    };
}

// ─── Service ─────────────────────────────────────────────────────────────────

/// <summary>
/// Looks up a clan name against the in-memory clan list using a three-tier
/// strategy: Exact → Contains → Fuzzy (Levenshtein).
///
/// Clans are loaded from <c>clans.json</c> once in the constructor and cached.
/// Call <see cref="Reload"/> after the file has been updated (e.g. after a
/// successful download) to refresh the in-memory list.
/// </summary>
public class ClanLookupService
{
    public double FuzzyThreshold { get; set; } = 0.75;

    private readonly string _jsonPath;
    private List<ClanEntry> _clans = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public ClanLookupService(string? jsonPath = null)
    {
        _jsonPath = jsonPath ?? Path.Combine(AppContext.BaseDirectory, "clans.json");
        _clans = LoadFromDisk();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Number of clans currently loaded.</summary>
    public int Count => _clans.Count;

    /// <summary>
    /// Re-reads <c>clans.json</c> from disk and refreshes the in-memory cache.
    /// Call after a successful download or manual file edit.
    /// </summary>
    public void Reload()
    {
        _clans = LoadFromDisk();
    }

    /// <summary>
    /// Looks up <paramref name="query"/> against the in-memory clan list.
    /// </summary>
    public ClanLookupResult Lookup(string query)
    {
        var needle = query.Trim();

        // Tier 1: Exact
        var entry = _clans.FirstOrDefault(c =>
            string.Equals(c.Name.Trim(), needle, StringComparison.OrdinalIgnoreCase));
        if (entry is not null)
            return Build(entry, needle, MatchMethod.Exact, 1.0, 0);

        // Tier 2: Contains
        entry = _clans.FirstOrDefault(c =>
            c.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
            needle.Contains(c.Name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (entry is not null)
        {
            double s = LevenshteinService.Similarity(needle, entry.Name.Trim());
            int d = LevenshteinService.Distance(needle, entry.Name.Trim());
            return Build(entry, needle, MatchMethod.Contains, s, d);
        }

        // Tier 3: Fuzzy
        var best = LevenshteinService.BestMatch(_clans, c => c.Name.Trim(), needle);
        if (best is not null && best.Similarity >= FuzzyThreshold)
            return Build(best.Item, needle, MatchMethod.Fuzzy, best.Similarity, best.Distance);

        return new ClanLookupResult
        {
            Found = false,
            ClanName = needle,
            QueryName = needle,
            Similarity = best?.Similarity ?? 0,
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ClanLookupResult Build(
        ClanEntry e, string query, MatchMethod m, double sim, int dist) =>
        new()
        {
            Found = true,
            ClanName = e.Name,
            QueryName = query,
            Leader = e.Leader,
            Status = e.Status,
            UpdatedAt = e.UpdatedAt,
            MatchMethod = m,
            Similarity = sim,
            EditDistance = dist,
        };

    private List<ClanEntry> LoadFromDisk()
    {
        if (!File.Exists(_jsonPath)) return new();
        try
        {
            var json = File.ReadAllText(_jsonPath);
            return JsonSerializer.Deserialize<ClanDb>(json, JsonOpts)?.Clans ?? new();
        }
        catch { return new(); }
    }
}
