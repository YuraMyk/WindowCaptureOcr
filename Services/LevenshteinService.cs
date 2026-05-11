namespace WindowCaptureOcr.Services;

/// <summary>
/// Levenshtein edit-distance utilities.
///
/// <para>
/// <see cref="Distance"/> returns the raw edit count (insertions, deletions,
/// substitutions).  <see cref="Similarity"/> normalises this to a 0–1 score
/// where 1.0 = identical and 0.0 = completely different.
/// </para>
///
/// <para>
/// <see cref="BestMatch{T}"/> scans a collection and returns the closest item
/// together with its score, so callers can apply their own acceptance threshold.
/// </para>
/// </summary>
public static class LevenshteinService
{
    // ── Core algorithm ────────────────────────────────────────────────────────

    /// <summary>
    /// Computes the Levenshtein (edit) distance between <paramref name="a"/>
    /// and <paramref name="b"/> using the memory-efficient two-row DP approach.
    /// Comparison is case-insensitive.
    /// </summary>
    public static int Distance(string a, string b)
    {
        a = a.ToLowerInvariant();
        b = b.ToLowerInvariant();

        int la = a.Length, lb = b.Length;
        if (la == 0) return lb;
        if (lb == 0) return la;

        // prev[j] = cost of aligning a[0..i-1] with b[0..j-1]
        int[] prev = new int[lb + 1];
        int[] curr = new int[lb + 1];

        for (int j = 0; j <= lb; j++) prev[j] = j;

        for (int i = 1; i <= la; i++)
        {
            curr[0] = i;
            for (int j = 1; j <= lb; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(prev[j] + 1,        // deletion
                             curr[j - 1] + 1),   // insertion
                    prev[j - 1] + cost);          // substitution
            }
            (prev, curr) = (curr, prev);
        }

        return prev[lb];
    }

    /// <summary>
    /// Returns a normalised similarity score in [0, 1].
    /// <c>1.0</c> = identical; <c>0.0</c> = maximally different.
    /// </summary>
    public static double Similarity(string a, string b)
    {
        int maxLen = Math.Max(a.Length, b.Length);
        if (maxLen == 0) return 1.0;                  // both empty → identical
        return 1.0 - (double)Distance(a, b) / maxLen;
    }

    // ── Collection helpers ────────────────────────────────────────────────────

    /// <summary>Result of a <see cref="BestMatch{T}"/> search.</summary>
    public sealed record MatchResult<T>(T Item, string MatchedText, double Similarity, int Distance);

    /// <summary>
    /// Finds the element in <paramref name="source"/> whose key (returned by
    /// <paramref name="keySelector"/>) has the highest similarity to
    /// <paramref name="query"/>.
    /// </summary>
    /// <returns>
    /// The best match, or <c>null</c> when <paramref name="source"/> is empty.
    /// </returns>
    public static MatchResult<T>? BestMatch<T>(
        IEnumerable<T> source,
        Func<T, string> keySelector,
        string query)
    {
        MatchResult<T>? best = null;

        foreach (var item in source)
        {
            var key  = keySelector(item);
            var sim  = Similarity(query, key);
            var dist = Distance(query, key);

            if (best is null || sim > best.Similarity)
                best = new MatchResult<T>(item, key, sim, dist);
        }

        return best;
    }

    /// <summary>
    /// Returns all matches from <paramref name="source"/> that meet or exceed
    /// <paramref name="minSimilarity"/>, sorted descending by similarity.
    /// </summary>
    public static IReadOnlyList<MatchResult<T>> AllMatches<T>(
        IEnumerable<T> source,
        Func<T, string> keySelector,
        string query,
        double minSimilarity = 0.0)
    {
        return source
            .Select(item =>
            {
                var key = keySelector(item);
                return new MatchResult<T>(item, key, Similarity(query, key), Distance(query, key));
            })
            .Where(r => r.Similarity >= minSimilarity)
            .OrderByDescending(r => r.Similarity)
            .ToList();
    }
}
