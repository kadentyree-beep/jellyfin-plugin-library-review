using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace LibraryReview;

public sealed class ReviewService(ILibraryManager library)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConfirmationStore _previews = new();
    private static Configuration Config => Plugin.Instance.Configuration;
    private static string StateFile => Path.Combine(Plugin.Instance.StateDirectory, "review-state.json");
    private static ReviewState Load() => File.Exists(StateFile)
        ? JsonSerializer.Deserialize<ReviewState>(File.ReadAllText(StateFile)) ?? new() : new();
    private static void Save(ReviewState state)
    {
        var tmp = StateFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(state));
        File.Move(tmp, StateFile, true);
    }
    private static string ConfigStamp() => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Config))));
    public async Task<ReviewState> Read(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { return Load(); } finally { _gate.Release(); }
    }
    public async Task Scan(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var state = Load();
            var old = state.Items.ToDictionary(i => i.Id);
            Dictionary<int, double> reference = [];
            string? referenceError = null;
            if (!string.IsNullOrWhiteSpace(Config.RadarrUrl))
            {
                try
                {
                    using var radarr = new RadarrClient(Config);
                    foreach (var m in (await radarr.Get("movie", ct)).AsArray())
                        if ((int?)m?["tmdbId"] is int id && (double?)m?["runtime"] is double runtime && runtime > 0) reference[id] = runtime;
                }
                catch (Exception e) when (e is HttpRequestException or InvalidOperationException or TaskCanceledException)
                { ct.ThrowIfCancellationRequested(); referenceError = "Radarr reference unavailable; runtime comparisons skipped."; }
            }
            var movies = library.GetItemList(new InternalItemsQuery { IncludeItemTypes = [BaseItemKind.Movie], Recursive = true, IsVirtualItem = false });
            List<ReviewItem> items = [];
            foreach (var movie in movies.OfType<Movie>())
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(movie.Path) || !File.Exists(movie.Path)) continue;
                try
                {
                    var file = ExactFile.Read(movie.Path);
                    int? tmdb = movie.ProviderIds.TryGetValue("Tmdb", out var value) && int.TryParse(value, out var number) ? number : null;
                    double actual = (movie.RunTimeTicks ?? 0) / (double)TimeSpan.TicksPerMinute;
                    double? expected = tmdb is int id && reference.TryGetValue(id, out var duration) ? duration : null;
                    var audio = movie.GetMediaStreams().Where(s => s.Type == MediaStreamType.Audio)
                        .Select(s => new AudioTrack(s.Language, s.Title, s.IsDefault, false)).ToList();
                    var findings = Checks.Evaluate(actual, expected, Path.GetFileName(movie.Path), audio, Config.DesiredLanguage);
                    var status = old.TryGetValue(movie.Id, out var previous) && previous.File == file
                        && JsonSerializer.Serialize(previous.Findings) == JsonSerializer.Serialize(findings) ? previous.Status : "open";
                    items.Add(new(movie.Id, movie.Name, tmdb, file, actual, expected,
                        expected is null ? "Unavailable; no independent runtime comparison" : "Radarr reference metadata (may describe theatrical cut)", findings, status));
                }
                catch (IOException) { /* Moving/importing files will be revisited next scan. */ }
                catch (UnauthorizedAccessException) { referenceError = "Some media paths could not be read; check Jellyfin filesystem permissions."; }
            }
            state.Items = items;
            state.LastScan = DateTimeOffset.UtcNow;
            state.LastError = referenceError;
            Save(state);
        }
        finally { _gate.Release(); }
    }
    public async Task Decide(Guid id, string status, CancellationToken ct)
    {
        if (status is not ("accepted" or "dismissed" or "problem" or "open")) throw new InvalidOperationException("Invalid review status.");
        await _gate.WaitAsync(ct);
        try
        {
            var state = Load();
            int index = state.Items.FindIndex(i => i.Id == id);
            if (index < 0 || ExactFile.Read(state.Items[index].File.Path) != state.Items[index].File)
                throw new InvalidOperationException("File changed or no longer exists. Run a new scan.");
            state.Items[index] = state.Items[index] with { Status = status };
            Save(state);
        }
        finally { _gate.Release(); }
    }
    public async Task<ReplacementPreview> Prepare(Guid id, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!Config.EnableReplacement) throw new InvalidOperationException("Replacement is disabled. An administrator must configure and enable it first.");
            var item = Load().Items.Single(i => i.Id == id);
            if (item.Status != "problem") throw new InvalidOperationException("Mark this file as a problem before preparing replacement.");
            using var radarr = new RadarrClient(Config);
            var match = await radarr.Match(item, Config, ct);
            if (ExactFile.Read(item.File.Path) != item.File) throw new InvalidOperationException("File changed; rescan first.");
            string hash = await ExactFile.Hash(item.File.Path, ct);
            if (ExactFile.Read(item.File.Path) != item.File) throw new InvalidOperationException("File changed during verification.");
            var preview = new ReplacementPreview(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), id,
                item.Title, item.File.Path, item.File.Size, hash, match.Release, match.MovieId, match.FileId, DateTimeOffset.UtcNow.AddMinutes(10));
            _previews.Add(new(preview, item.File, match, ConfigStamp()));
            return preview;
        }
        finally { _gate.Release(); }
    }
    public async Task<string> Replace(Guid id, ConfirmRequest request, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        string? staged = null;
        try
        {
            var pending = _previews.Consume(id, request, ConfigStamp(), Config.EnableReplacement);
            var state = Load();
            var item = state.Items.Single(i => i.Id == id);
            if (item.File != pending.File || item.Status != "problem") throw new InvalidOperationException("Review changed; prepare again.");
            using var radarr = new RadarrClient(Config);
            if (await radarr.Match(item, Config, ct) != pending.Match) throw new InvalidOperationException("Radarr file or release changed. Nothing deleted.");
            state.Actions.Add(new(DateTimeOffset.UtcNow, id, "confirmed", "Administrator confirmed exact file and release.")); Save(state);
            staged = await ExactFile.Stage(Config.JellyfinMovieRoot, pending.File, pending.Preview.Sha256, ct);
            state.Actions.Add(new(DateTimeOffset.UtcNow, id, "staged", staged)); Save(state);
            try
            {
                // Do not remove media if blocklisting fails. Never use Radarr's delete
                // endpoint: it may remove associated sidecar files as well.
                await radarr.Post($"history/failed/{pending.Match.HistoryId}", new { }, ct);
                await radarr.VerifyBlocklisted(pending.Match, ct);
                state.Actions.Add(new(DateTimeOffset.UtcNow, id, "blocked", pending.Match.Release)); Save(state);
                await ExactFile.DeleteStaged(staged, pending.Preview.Sha256, ct);
            }
            catch
            {
                ExactFile.Restore(staged, pending.File.Path);
                state.Actions.Add(new(DateTimeOffset.UtcNow, id, "stopped", "Operation stopped. Original restored if its path was free; otherwise inspect the staged path. Blocklist may have succeeded.")); Save(state);
                throw;
            }
            staged = null;
            state.Actions.Add(new(DateTimeOffset.UtcNow, id, "deleted", pending.File.Path)); Save(state);
            await radarr.RefreshAndSearch(pending.Match.MovieId, ct);
            state.Actions.Add(new(DateTimeOffset.UtcNow, id, "search-requested", "Radarr replacement search requested; availability is not guaranteed.")); Save(state);
            return "Exact file removed and release blocked. Radarr search requested. Replacement is not yet verified.";
        }
        finally { _gate.Release(); }
    }
}
