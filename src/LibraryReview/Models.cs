namespace LibraryReview;

public record AudioTrack(string? Language, string? Title, bool IsDefault, bool IsCommentary);
public record Finding(string Code, string Severity, string Message);
public record Fingerprint(string Path, long Size, long ModifiedTicks);
public record ReviewItem(Guid Id, string Title, int? TmdbId, Fingerprint File,
    double ActualMinutes, double? ExpectedMinutes, string ReferenceSource,
    List<Finding> Findings, string Status = "open");
public sealed class ReviewState
{
    public DateTimeOffset? LastScan { get; set; }
    public string? LastError { get; set; }
    public List<ReviewItem> Items { get; set; } = [];
    public List<ActionRecord> Actions { get; set; } = [];
}
public record ActionRecord(DateTimeOffset At, Guid ItemId, string Stage, string Message);
public record ReplacementPreview(string Token, Guid ItemId, string Title, string Path,
    long Size, string Sha256, string Release, int MovieId, int FileId, DateTimeOffset Expires);
public record RadarrMatch(int MovieId, int FileId, int HistoryId, string Path, long Size, string Release);
public record ConfirmRequest(string Token, string ConfirmTitle);
public record ReviewDecision(string Status);

public static class Checks
{
    public static List<Finding> Evaluate(double actual, double? expected, string label, IReadOnlyList<AudioTrack> audio, string desiredLanguage = "eng")
    {
        List<Finding> findings = [];
        if (actual <= 0) findings.Add(new("duration-unknown", "info", "Duration unavailable; file identity has not been verified."));
        else if (expected > 0 && Math.Abs(actual - expected.Value) > Math.Max(10, expected.Value * .10))
            findings.Add(new("runtime", "review", $"Runtime {actual:F1} minutes; reference {expected:F1}. A different edition may be legitimate."));
        if (audio.Count == 0) findings.Add(new("audio-missing", "review", "No audio streams recorded by Jellyfin. Refresh media information before deciding."));
        else
        {
            var wanted = audio.Where(a => string.Equals(a.Language, desiredLanguage, StringComparison.OrdinalIgnoreCase)
                || (desiredLanguage == "eng" && a.Language == "en")).ToList();
            if (wanted.Count == 0)
                findings.Add(new("language-unverified", "info", $"No {desiredLanguage}-tagged audio. Missing or incorrect labels do not establish the spoken language."));
            if (audio.Any(a => a.IsDefault && Commentary(a)))
                findings.Add(new("commentary-default", "review", "Default audio is labeled commentary; another track may be the normal soundtrack."));
            if (wanted.Count > 0 && wanted.All(Commentary))
                findings.Add(new("commentary-only", "review", "All audio tagged with the preferred language is labeled commentary."));
        }
        if (System.Text.RegularExpressions.Regex.IsMatch(label,
            @"extraction[ ._-]*mode|behind[ ._-]*the[ ._-]*scenes|featurette|rifftrax", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            findings.Add(new("bonus-label", "review", "Filename suggests bonus or commentary content. Inspect before replacing."));
        return findings;
    }
    private static bool Commentary(AudioTrack a) => a.IsCommentary || (a.Title?.Contains("commentary", StringComparison.OrdinalIgnoreCase) ?? false);
}
