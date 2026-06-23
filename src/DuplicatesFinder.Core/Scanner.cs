using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace DuplicatesFinder;

/// <summary>Fase + tellers voor voortgangsrapportage tijdens een scan.</summary>
public readonly record struct ScanProgress(ScanPhase Phase, int Done, int Total);

public enum ScanPhase { Enumerating, Hashing, Done }

/// <summary>
/// Loopt een map recursief af en berekent SHA-256 per bestand (parallel).
/// Geen Console-afhankelijkheid: voortgang via <see cref="IProgress{T}"/>, stoppen via token.
/// </summary>
public sealed class Scanner
{
    private readonly int _threads;

    public Scanner(int threads) => _threads = Math.Max(1, threads);

    /// <summary>Fouten per bestand/map (toegang geweigerd, in gebruik, ...). Pad -> melding.</summary>
    public List<(string Path, string Error)> Errors { get; } = new();

    public List<FileRecord> Scan(string root, IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        root = Path.GetFullPath(root);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Map bestaat niet: {root}");

        progress?.Report(new ScanProgress(ScanPhase.Enumerating, 0, 0));
        var files = EnumerateFiles(root).ToList();
        progress?.Report(new ScanProgress(ScanPhase.Enumerating, files.Count, files.Count));

        var results = new ConcurrentBag<FileRecord>();
        var errors = new ConcurrentBag<(string, string)>();
        int done = 0;
        int total = files.Count;

        var options = new ParallelOptions { MaxDegreeOfParallelism = _threads, CancellationToken = ct };
        try
        {
            Parallel.ForEach(files, options, path =>
            {
                try
                {
                    var info = new FileInfo(path);
                    string hash = HashFile(path);
                    results.Add(new FileRecord
                    {
                        RelativePath = ToRelative(root, path),
                        AbsolutePath = path,
                        Size = info.Length,
                        Hash = hash,
                        LastModifiedUtc = info.LastWriteTimeUtc,
                    });
                }
                catch (Exception ex)
                {
                    errors.Add((path, ex.Message));
                }
                finally
                {
                    int n = Interlocked.Increment(ref done);
                    if (progress is not null && (n % 50 == 0 || n == total))
                        progress.Report(new ScanProgress(ScanPhase.Hashing, n, total));
                }
            });
        }
        catch (OperationCanceledException)
        {
            Errors.AddRange(errors);
            throw;
        }

        progress?.Report(new ScanProgress(ScanPhase.Done, total, total));
        Errors.AddRange(errors);
        return results.ToList();
    }

    private IEnumerable<string> EnumerateFiles(string root)
    {
        // Handmatige recursie zodat één onbereikbare submap de hele scan niet stopt.
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            string[] subDirs = Array.Empty<string>();
            try { subDirs = Directory.GetDirectories(dir); }
            catch (Exception ex) { Errors.Add((dir, ex.Message)); }
            foreach (var sub in subDirs) stack.Push(sub);

            string[] entries = Array.Empty<string>();
            try { entries = Directory.GetFiles(dir); }
            catch (Exception ex) { Errors.Add((dir, ex.Message)); continue; }
            foreach (var f in entries) yield return f;
        }
    }

    private static string HashFile(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }

    /// <summary>Relatief pad met '/' als scheidingsteken — stabiel over schijven/shares heen.</summary>
    public static string ToRelative(string root, string fullPath)
    {
        var rel = Path.GetRelativePath(root, fullPath);
        return rel.Replace('\\', '/');
    }
}
