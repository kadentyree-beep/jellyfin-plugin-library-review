using Xunit;

namespace LibraryReview.Tests;

public sealed class SafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "library-review-tests-" + Guid.NewGuid());
    public SafetyTests() => Directory.CreateDirectory(_root);
    private string Video(string name = "movie.mkv", string text = "reviewed original")
    {
        string path = Path.Combine(_root, name); File.WriteAllText(path, text); return path;
    }
    [Fact]
    public async Task DeletesOnlyCapturedFileAndLeavesSiblingsAndNewImport()
    {
        string video = Video(), sibling = Video("other-edition.mkv"), subtitle = Video("movie.srt");
        var fingerprint = ExactFile.Read(video); var hash = await ExactFile.Hash(video, default);
        var staged = await ExactFile.Stage(_root, fingerprint, hash, default);
        File.WriteAllText(video, "new import arriving at same path");
        await ExactFile.DeleteStaged(staged, hash, default);
        Assert.Equal("new import arriving at same path", File.ReadAllText(video));
        Assert.True(File.Exists(sibling)); Assert.True(File.Exists(subtitle)); Assert.True(Directory.Exists(_root));
        Assert.False(File.Exists(staged));
    }
    [Fact]
    public async Task SameSizeSameTimestampDifferentContentIsRejected()
    {
        string video = Video(text: "AAAA"); var fingerprint = ExactFile.Read(video); var hash = await ExactFile.Hash(video, default);
        File.WriteAllText(video, "BBBB"); File.SetLastWriteTimeUtc(video, new DateTime(fingerprint.ModifiedTicks, DateTimeKind.Utc));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ExactFile.Stage(_root, fingerprint, hash, default));
        Assert.Equal("BBBB", File.ReadAllText(video));
    }
    [Fact]
    public async Task ChangedSizeIsRejected()
    {
        string video = Video(); var fingerprint = ExactFile.Read(video); var hash = await ExactFile.Hash(video, default);
        File.AppendAllText(video, "changed");
        await Assert.ThrowsAsync<InvalidOperationException>(() => ExactFile.Stage(_root, fingerprint, hash, default));
        Assert.True(File.Exists(video));
    }
    [Fact]
    public void RejectsDirectoryAndSidecar()
    {
        Assert.Throws<InvalidOperationException>(() => ExactFile.UnderRoot(_root, _root));
        Assert.Throws<InvalidOperationException>(() => ExactFile.UnderRoot(_root, Video("movie.nfo")));
    }
    [Fact]
    public void RejectsTraversalOutsideRoot()
    {
        Assert.Throws<InvalidOperationException>(() => ExactFile.UnderRoot(_root, Path.Combine(_root, "..", "outside.mkv")));
    }
    [Fact]
    public void RejectsPrefixLookalikeRoot()
    {
        Assert.Throws<InvalidOperationException>(() => ExactFile.UnderRoot(_root, _root + "-other/movie.mkv"));
    }
    [Fact]
    public async Task StagedTamperingIsNotDeleted()
    {
        var video = Video(); var hash = await ExactFile.Hash(video, default);
        var staged = await ExactFile.Stage(_root, ExactFile.Read(video), hash, default);
        File.WriteAllText(staged, "changed");
        await Assert.ThrowsAsync<InvalidOperationException>(() => ExactFile.DeleteStaged(staged, hash, default));
        Assert.True(File.Exists(staged));
    }
    [Fact]
    public async Task RestoreDoesNotOverwriteReplacement()
    {
        var video = Video(); var hash = await ExactFile.Hash(video, default);
        var staged = await ExactFile.Stage(_root, ExactFile.Read(video), hash, default);
        File.WriteAllText(video, "new"); ExactFile.Restore(staged, video);
        Assert.Equal("new", File.ReadAllText(video)); Assert.True(File.Exists(staged));
    }
    [Fact]
    public async Task FailedOperationCanRestoreOriginal()
    {
        var video = Video(); var hash = await ExactFile.Hash(video, default);
        var staged = await ExactFile.Stage(_root, ExactFile.Read(video), hash, default);
        ExactFile.Restore(staged, video);
        Assert.Equal("reviewed original", File.ReadAllText(video)); Assert.False(File.Exists(staged));
    }
    [Fact]
    public async Task NonStagedPathCannotBeDeleted()
    {
        var video = Video(); var hash = await ExactFile.Hash(video, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ExactFile.DeleteStaged(video, hash, default));
        Assert.True(File.Exists(video));
    }
    [Fact]
    public async Task CancelledHashDoesNotMoveOriginal()
    {
        var video = Video(); var hash = await ExactFile.Hash(video, default);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ExactFile.Stage(_root, ExactFile.Read(video), hash, cts.Token));
        Assert.True(File.Exists(video));
    }
    [Fact]
    public void SymlinkRefusedOnLinux()
    {
        if (OperatingSystem.IsWindows()) return; // Linux CI exercises this; Windows may require developer mode.
        var target = Video(); var link = Path.Combine(_root, "linked.mkv"); File.CreateSymbolicLink(link, target);
        Assert.Throws<InvalidOperationException>(() => ExactFile.UnderRoot(_root, link));
    }
    [Fact]
    public void SymlinkAncestorRefusedOnLinux()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Directory.CreateDirectory(Path.Combine(_root, "real")); File.WriteAllText(Path.Combine(dir.FullName, "movie.mkv"), "x");
        var link = Path.Combine(_root, "link"); Directory.CreateSymbolicLink(link, dir.FullName);
        Assert.Throws<InvalidOperationException>(() => ExactFile.UnderRoot(_root, Path.Combine(link, "movie.mkv")));
    }
    public void Dispose() { Directory.Delete(_root, true); }
}

public sealed class CheckTests
{
    private static AudioTrack English => new("eng", "Main soundtrack", true, false);
    [Fact] public void FlagsInceptionLikeRuntime() => Assert.Contains(Checks.Evaluate(190,148,"movie.mkv",[English]), f => f.Code == "runtime");
    [Fact] public void DoesNotFlagSmallRuntimeDifference() => Assert.DoesNotContain(Checks.Evaluate(150,148,"movie.mkv",[English]), f => f.Code == "runtime");
    [Fact] public void NoReferenceDoesNotPretendToVerifyRuntime() => Assert.DoesNotContain(Checks.Evaluate(190,null,"movie.mkv",[English]), f => f.Code == "runtime");
    [Fact] public void MissingLanguageIsUncertainty() => Assert.Contains(Checks.Evaluate(100,100,"movie.mkv",[new(null,null,true,false)]), f => f.Code == "language-unverified" && f.Severity == "info");
    [Fact] public void OptionalCommentaryIsNotAnError() => Assert.Empty(Checks.Evaluate(100,100,"movie.mkv",[English,new("eng","Director commentary",false,true)]));
    [Fact] public void DefaultCommentaryFlagged() => Assert.Contains(Checks.Evaluate(100,100,"movie.mkv",[new("eng","commentary",true,false)]), f => f.Code == "commentary-default");
    [Fact] public void BonusFilenameFlagged() => Assert.Contains(Checks.Evaluate(100,100,"Inception.Extraction.Mode.mkv",[English]), f => f.Code == "bonus-label");
    [Fact] public void AlternatePreferredLanguageSupported() => Assert.Empty(Checks.Evaluate(100,100,"movie.mkv",[new("spa",null,true,false)],"spa"));
}
