using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace LibraryReview;

public sealed class RadarrClient : IDisposable
{
    private readonly HttpClient _http;
    public RadarrClient(Configuration config, HttpMessageHandler? handler = null)
    {
        if (!Uri.TryCreate(config.RadarrUrl.TrimEnd('/') + "/", UriKind.Absolute, out var url)
            || url.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(url.UserInfo)
            || !string.IsNullOrEmpty(url.Query) || !string.IsNullOrEmpty(url.Fragment)
            || string.IsNullOrWhiteSpace(config.RadarrApiKey))
            throw new InvalidOperationException("Configure a Radarr HTTP(S) URL and API key first.");
        _http = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = url, Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.Add("X-Api-Key", config.RadarrApiKey);
    }
    public async Task<JsonNode> Get(string path, CancellationToken ct)
    {
        using var response = await _http.GetAsync("api/v3/" + path, ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Radarr GET failed ({(int)response.StatusCode}).");
        return await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: ct) ?? throw new InvalidOperationException("Empty Radarr response.");
    }
    public async Task Post(string path, object body, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync("api/v3/" + path, body, ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Radarr operation failed ({(int)response.StatusCode}). Review activity before retrying.");
    }
    public async Task<RadarrMatch> Match(ReviewItem item, Configuration config, CancellationToken ct)
    {
        if (item.TmdbId is null) throw new InvalidOperationException("TMDB identity is missing.");
        ExactFile.UnderRoot(config.JellyfinMovieRoot, item.File.Path);
        var relative = Path.GetRelativePath(Path.GetFullPath(config.JellyfinMovieRoot), item.File.Path).Replace('\\', '/');
        var mapped = config.RadarrMovieRoot.TrimEnd('/', '\\') + "/" + relative;
        var all = (await Get("movie", ct)).AsArray();
        var matches = all.Where(m => (int?)m?["tmdbId"] == item.TmdbId).ToList();
        if (matches.Count != 1) throw new InvalidOperationException("Radarr identity is missing or ambiguous.");
        var movie = matches[0]!;
        var file = movie["movieFile"] ?? throw new InvalidOperationException("Radarr has no current file.");
        if (!string.Equals(((string?)file["path"])?.Replace('\\', '/'), mapped, StringComparison.Ordinal)
            || (long?)file["size"] != item.File.Size)
            throw new InvalidOperationException("Radarr file path/size does not exactly match this Jellyfin file.");
        int movieId = (int)movie["id"]!, fileId = (int)file["id"]!;
        string release = (string?)file["sceneName"] ?? "";
        if (string.IsNullOrWhiteSpace(release)) throw new InvalidOperationException("Original release identity missing; cannot safely blocklist.");
        var history = (await Get($"history/movie?movieId={movieId}", ct)).AsArray();
        // Link current file -> import record -> exact originating grab, not a title search.
        var imports = history.Where(h => (string?)h?["eventType"] == "downloadFolderImported"
            && (string?)h?["data"]?["fileId"] == fileId.ToString()
            && (string?)h?["sourceTitle"] == release).ToList();
        if (imports.Count != 1) throw new InvalidOperationException("Unique import history for this file was not found.");
        string? downloadId = (string?)imports[0]?["downloadId"];
        if (string.IsNullOrWhiteSpace(downloadId)) throw new InvalidOperationException("Import download identity is missing.");
        var grabs = history.Where(h => (string?)h?["eventType"] == "grabbed"
            && (string?)h?["downloadId"] == downloadId && (string?)h?["sourceTitle"] == release).ToList();
        if (grabs.Count != 1) throw new InvalidOperationException("Unique original grab was not found; replacement disabled.");
        var queue = (await Get("queue?pageSize=1000", ct))["records"]!.AsArray();
        if (queue.Any(q => (int?)q?["movieId"] == movieId)) throw new InvalidOperationException("This movie already has an active download. Resolve it first.");
        return new(movieId, fileId, (int)grabs[0]!["id"]!, mapped, item.File.Size, release);
    }
    public async Task RefreshAndSearch(int movieId, CancellationToken ct)
    {
        await Post("command", new { name = "RefreshMovie", movieIds = new[] { movieId } }, ct);
        for (int i = 0; i < 30; i++)
        {
            await Task.Delay(1000, ct);
            var movie = await Get($"movie/{movieId}", ct);
            if ((bool?)movie["hasFile"] == false)
            {
                var queue = (await Get("queue?pageSize=1000", ct))["records"]!.AsArray();
                if (!queue.Any(q => (int?)q?["movieId"] == movieId))
                    await Post("command", new { name = "MoviesSearch", movieIds = new[] { movieId } }, ct);
                return;
            }
        }
        throw new InvalidOperationException("File removed, but Radarr refresh is pending. Check Radarr before retrying any action.");
    }
    public void Dispose() => _http.Dispose();
}
