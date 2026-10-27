using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace LibraryReview;
// Test fixture settings: production Configuration derives from Jellyfin's base.
public sealed class Configuration
{
    public string RadarrUrl { get; set; } = "http://radarr.test/";
    public string RadarrApiKey { get; set; } = "test-key-not-a-secret";
    public string JellyfinMovieRoot { get; set; } = "";
    public string RadarrMovieRoot { get; set; } = "/movies";
}

public sealed class RadarrTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lr-match-" + Guid.NewGuid());
    private readonly Configuration _config;
    private readonly ReviewItem _item;
    private readonly Dictionary<string, JsonNode> _responses;
    private readonly List<string> _calls = [];
    public RadarrTests()
    {
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root,"movie.mkv"); File.WriteAllText(path,"test");
        _config = new() { JellyfinMovieRoot = _root };
        _item = new(Guid.NewGuid(), "Example", 123, ExactFile.Read(path), 100, 100, "fixture", [], "problem");
        _responses = new()
        {
            ["movie"] = JsonNode.Parse("""[{"id":1,"tmdbId":123,"movieFile":{"id":2,"path":"/movies/movie.mkv","size":4,"sceneName":"Example.Release"}}]""")!,
            ["history/movie?movieId=1"] = JsonNode.Parse("""[{"id":3,"eventType":"downloadFolderImported","sourceTitle":"Example.Release","downloadId":"abc","data":{"fileId":"2"}},{"id":4,"eventType":"grabbed","sourceTitle":"Example.Release","downloadId":"abc"}]""")!,
            ["queue?pageSize=1000"] = JsonNode.Parse("""{"records":[]}""")!
        };
    }
    private RadarrClient Client() => new(_config,new FakeHandler(_responses,_calls));
    [Fact] public async Task ExactIdentityMatches()
    {
        using var c=Client();var match=await c.Match(_item,_config,default);
        Assert.Equal(2,match.FileId);Assert.Equal(4,match.HistoryId);Assert.All(_calls, x=>Assert.StartsWith("GET ",x));
    }
    [Fact] public async Task WrongMovieIdRefused()
    {
        _responses["movie"][0]!["tmdbId"]=999;using var c=Client();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>c.Match(_item,_config,default));
    }
    [Fact] public async Task WrongFilePathRefused()
    {
        _responses["movie"][0]!["movieFile"]!["path"]="/movies/other.mkv";using var c=Client();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>c.Match(_item,_config,default));
    }
    [Fact] public async Task WrongSizeRefused()
    {
        _responses["movie"][0]!["movieFile"]!["size"]=99;using var c=Client();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>c.Match(_item,_config,default));
    }
    [Fact] public async Task MissingFileHistoryRefused()
    {
        _responses["history/movie?movieId=1"][0]!["data"]!["fileId"]="20";using var c=Client();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>c.Match(_item,_config,default));
    }
    [Fact] public async Task MismatchedDownloadHistoryRefused()
    {
        _responses["history/movie?movieId=1"][1]!["downloadId"]="other";using var c=Client();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>c.Match(_item,_config,default));
    }
    [Fact] public async Task DuplicateIdentityRefused()
    {
        _responses["movie"].AsArray().Add(_responses["movie"][0]!.DeepClone());using var c=Client();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>c.Match(_item,_config,default));
    }
    [Fact] public async Task ActiveDownloadRefused()
    {
        _responses["queue?pageSize=1000"]["records"]!.AsArray().Add(JsonNode.Parse("""{"movieId":1}"""));using var c=Client();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>c.Match(_item,_config,default));
    }
    [Fact] public async Task OtherEditionRefusesReplacement()
    {
        File.WriteAllText(Path.Combine(_root,"other.mp4"),"keep");using var c=Client();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>c.Match(_item,_config,default));
        Assert.Empty(_calls);
    }
    [Fact] public async Task MissingBlocklistConfirmationRefused()
    {
        _responses["blocklist/movie?movieId=1"]=JsonNode.Parse("[]")!;using var c=Client();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>c.VerifyBlocklisted(new(1,2,4,"/movies/movie.mkv",4,"Example.Release"),default));
    }
    [Fact] public async Task ExactReleaseBlocklistVerified()
    {
        _responses["blocklist/movie?movieId=1"]=JsonNode.Parse("""[{"movieId":1,"sourceTitle":"Example.Release"}]""")!;using var c=Client();
        await c.VerifyBlocklisted(new(1,2,4,"/movies/movie.mkv",4,"Example.Release"),default);
    }
    [Fact] public void UrlCredentialsRefused()
    {
        _config.RadarrUrl="https://user:password@radarr.test";
        Assert.Throws<InvalidOperationException>(()=>Client());
    }
    [Fact] public async Task ApiFailuresDoNotExposeResponseSecrets()
    {
        using var c=new RadarrClient(_config,new FailureHandler());
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>c.Post("history/failed/4",new {},default));
        Assert.DoesNotContain("secret-from-upstream",error.Message);
    }
    public void Dispose()=>Directory.Delete(_root,true);
    private sealed class FakeHandler(Dictionary<string,JsonNode> responses,List<string> calls):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var key=request.RequestUri!.PathAndQuery.Replace("/api/v3/","");calls.Add(request.Method+" "+key);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(responses[key].ToJsonString())});
        }
    }
    private sealed class FailureHandler:HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError){Content=new StringContent("secret-from-upstream")});
    }
}
