using System.Collections.Concurrent;

namespace Gloam.Localization;

/// <summary>
/// Thread-safe record of every key that was requested but not found, with a hit count.
/// </summary>
/// <remarks>
/// This is the cheapest possible defence against the classic i18n failure mode where a key
/// typo ships and the user sees a raw identifier. The diagnostics bundle exports it, and the
/// audit test asserts it stays empty for every key the catalogs claim to cover.
/// </remarks>
public sealed class MissingKeyReport
{
    private readonly ConcurrentDictionary<string, int> _counts = new(StringComparer.Ordinal);
    private readonly int _limit;

    /// <summary>Creates a report. Public so hosts and test doubles can supply their own.</summary>
    public MissingKeyReport(int maxTrackedKeys = 500) => _limit = Math.Max(1, maxTrackedKeys);

    /// <summary>Distinct missing keys, ordered for stable output.</summary>
    public IReadOnlyList<string> Keys => _counts.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();

    public bool IsEmpty => _counts.IsEmpty;

    /// <summary>How many times <paramref name="key"/> was missed.</summary>
    public int CountOf(string key) => _counts.TryGetValue(key, out int n) ? n : 0;

    internal void Record(string key)
    {
        if (string.IsNullOrEmpty(key))
            return;

        // Existing keys keep counting even at the cap; only new keys are dropped.
        if (_counts.TryGetValue(key, out int current))
        {
            _counts[key] = current + 1;
            return;
        }

        if (_counts.Count >= _limit)
            return;

        _counts[key] = 1;
    }

    /// <summary>Clears the record. Called when the culture changes: misses are per-culture signal.</summary>
    public void Clear() => _counts.Clear();
}
