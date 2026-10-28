using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace VideoCapture.Services;

internal sealed record SubtitleCue(long StartMs, long EndMs, string Text);

internal sealed class SubtitleTimeline
{
    private readonly SubtitleCue[] _cues;
    private readonly long[] _endMax;
    public int Count => _cues.Length;
    public SubtitleTimeline(IEnumerable<SubtitleCue> cues)
    {
        _cues = cues.Where(c => c.EndMs > c.StartMs && !string.IsNullOrWhiteSpace(c.Text)).OrderBy(c => c.StartMs).ToArray();
        _endMax = new long[_cues.Length];
        for (int i = 0; i < _cues.Length; i++) _endMax[i] = Math.Max(i > 0 ? _endMax[i - 1] : 0, _cues[i].EndMs);
    }
    public SubtitleCue? At(long position)
    {
        int lo = 0, hi = _cues.Length;
        while (lo < hi) { int mid = lo + (hi - lo) / 2; if (_cues[mid].StartMs <= position) lo = mid + 1; else hi = mid; }
        var active = new List<SubtitleCue>();
        for (int i = lo - 1; i >= 0 && _endMax[i] > position; i--) if (_cues[i].EndMs > position) active.Add(_cues[i]);
        if (active.Count == 0) return null;
        active.Reverse();
        return new(active.Min(c => c.StartMs), active.Max(c => c.EndMs), string.Join(" ", active.Select(c => c.Text).Distinct()).Trim());
    }
}

internal static class SubtitleText
{
    private static readonly Regex Timing = new(@"(?m)^(\d{1,3}):(\d{2}):(\d{2})[,.](\d{3})\s*-->\s*(\d{1,3}):(\d{2}):(\d{2})[,.](\d{3})[^\r\n]*\r?$", RegexOptions.Compiled);
    public static string Clean(string text)
    {
        text = Regex.Replace(text, @"\{\\[^}]*\}", "");
        text = Regex.Replace(text, @"<[^>]*>", "");
        text = WebUtility.HtmlDecode(text).Replace(@"\N", " ").Replace(@"\n", " ").Replace(@"\h", " ");
        return Regex.Replace(text, @"\s+", " ").Trim();
    }
    public static IReadOnlyList<SubtitleCue> ParseSrt(string text)
    {
        text = text.Replace("\r\n", "\n").TrimStart('\uFEFF');
        var cues = new List<SubtitleCue>();
        foreach (string block in Regex.Split(text, @"\n[ \t]*\n"))
        {
            var match = Timing.Match(block);
            if (!match.Success) continue;
            long Time(int offset) => ((long)int.Parse(match.Groups[offset].Value, CultureInfo.InvariantCulture) * 3600 +
                int.Parse(match.Groups[offset + 1].Value, CultureInfo.InvariantCulture) * 60 + int.Parse(match.Groups[offset + 2].Value, CultureInfo.InvariantCulture)) * 1000 + int.Parse(match.Groups[offset + 3].Value, CultureInfo.InvariantCulture);
            long start = Time(1), end = Time(5);
            string line = Clean(block[(match.Index + match.Length)..]);
            if (end > start && line.Length > 0) cues.Add(new(start, end, line));
        }
        return cues;
    }
}

internal sealed class SubtitleRepeatFilter
{
    private string _candidate = "", _spoken = "";
    private int _stable, _blank;
    public void Reset() { _candidate = _spoken = ""; _stable = _blank = 0; }
    public string? Observe(string text, float confidence)
    {
        string clean = SubtitleText.Clean(text);
        if (!float.IsFinite(confidence) || confidence < 0.60f || clean.Length > 500 || !clean.Any(char.IsLetter)) clean = "";
        string key = Normalize(clean);
        if (key.Length == 0)
        {
            _candidate = ""; _stable = 0;
            if (++_blank >= 3) _spoken = "";
            return null;
        }
        _blank = 0;
        if (Similar(key, _candidate)) _stable++; else { _candidate = key; _stable = 1; }
        if (_stable < 2 || Similar(key, _spoken)) return null;
        _spoken = key;
        return clean;
    }
    internal static bool Similar(string first, string second)
    {
        if (first.Length == 0 || second.Length == 0) return false;
        if (first == second) return true;
        int max = Math.Max(first.Length, second.Length);
        if (Math.Abs(first.Length - second.Length) > max * 0.12) return false;
        var previous = Enumerable.Range(0, second.Length + 1).ToArray();
        var next = new int[second.Length + 1];
        for (int i = 1; i <= first.Length; i++)
        {
            next[0] = i;
            for (int j = 1; j <= second.Length; j++) next[j] = Math.Min(Math.Min(next[j - 1] + 1, previous[j] + 1), previous[j - 1] + (first[i - 1] == second[j - 1] ? 0 : 1));
            (previous, next) = (next, previous);
        }
        return previous[second.Length] <= max * 0.12;
    }
    private static string Normalize(string text) => new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
