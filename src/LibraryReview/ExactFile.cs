using System.Security.Cryptography;

namespace LibraryReview;

/// <summary>No directory deletion, globbing, recursion, or Radarr delete calls.</summary>
public static class ExactFile
{
    public static string UnderRoot(string root, string path)
    {
        if (!System.IO.Path.IsPathFullyQualified(root) || !System.IO.Path.IsPathFullyQualified(path))
            throw new InvalidOperationException("An absolute library root and file path are required.");
        root = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar);
        path = System.IO.Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.StartsWith(root + System.IO.Path.DirectorySeparatorChar, comparison))
            throw new InvalidOperationException("File is outside the configured movie root.");
        // Refuse links/junctions in every ancestor, including above the root.
        for (string? current = path; current is not null; current = System.IO.Path.GetDirectoryName(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Symlinks and junctions are not supported for replacement.");
        }
        if ((File.GetAttributes(path) & FileAttributes.Directory) != 0)
            throw new InvalidOperationException("Only a single regular media file can be removed.");
        if (!new[] { ".mkv", ".mp4", ".m4v", ".avi", ".ts", ".mov", ".wmv" }.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant()))
            throw new InvalidOperationException("Unsupported media file extension.");
        return path;
    }

    public static Fingerprint Read(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new InvalidOperationException("Reviewed file no longer exists.");
        return new(info.FullName, info.Length, info.LastWriteTimeUtc.Ticks);
    }

    public static async Task<string> Hash(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
    }

    public static async Task<string> Stage(string root, Fingerprint reviewed, string hash, CancellationToken ct)
    {
        var path = UnderRoot(root, reviewed.Path);
        if (Read(path) != reviewed || await Hash(path, ct) != hash || Read(path) != reviewed)
            throw new InvalidOperationException("File changed since review. Nothing was removed.");
        // An atomic same-directory rename captures one directory entry. A replacement
        // imported at the original path after this point is never touched.
        UnderRoot(root, path);
        var staged = path + ".library-review-" + Guid.NewGuid().ToString("N") + ".quarantine";
        File.Move(path, staged, false);
        try
        {
            var actual = Read(staged);
            if (actual.Size != reviewed.Size || actual.ModifiedTicks != reviewed.ModifiedTicks || await Hash(staged, ct) != hash)
                throw new InvalidOperationException("Captured file changed. Refusing deletion.");
            return staged;
        }
        catch
        {
            Restore(staged, path);
            throw;
        }
    }

    public static void Restore(string staged, string original)
    {
        // Never overwrite a new file that has appeared at the original name.
        if (File.Exists(staged) && !File.Exists(original)) File.Move(staged, original, false);
    }

    public static async Task DeleteStaged(string staged, string expectedHash, CancellationToken ct)
    {
        if (!staged.EndsWith(".quarantine", StringComparison.Ordinal) || !staged.Contains(".library-review-", StringComparison.Ordinal))
            throw new InvalidOperationException("Not an exact staged file.");
        if ((File.GetAttributes(staged) & FileAttributes.ReparsePoint) != 0 || await Hash(staged, ct) != expectedHash)
            throw new InvalidOperationException("Staged file changed. Refusing deletion.");
        File.Delete(staged);
    }
}
