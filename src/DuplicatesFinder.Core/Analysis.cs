namespace DuplicatesFinder;

/// <summary>Pure analyse-logica: dubbelen vinden en een scan vergelijken met een dictionary.</summary>
public static class Analysis
{
    public static HashDatabase BuildDatabase(string root, IEnumerable<FileRecord> files)
    {
        var db = new HashDatabase { Root = root, CreatedUtc = DateTimeOffset.UtcNow };
        foreach (var f in files) db.Add(f);
        return db;
    }

    /// <summary>Groepen van bestanden met dezelfde inhoud die meer dan 1x voorkomen.</summary>
    public static List<List<FileRecord>> FindDuplicates(HashDatabase db) =>
        db.ByHash.Values
            .Where(v => v.Count > 1)
            .OrderByDescending(v => v.Count)
            .ThenByDescending(v => v[0].Size)
            .ToList();

    /// <summary>Verspilde ruimte = som van (grootte × extra kopieën).</summary>
    public static long WastedBytes(IEnumerable<List<FileRecord>> duplicateGroups) =>
        duplicateGroups.Sum(g => g[0].Size * (g.Count - 1));

    public sealed record CompareResult(
        List<WeirdMatch> Weird,
        List<FileRecord> Expected,
        List<FileRecord> NotInDb);

    /// <summary>Een bestand dat qua inhoud wél in de dictionary zit, maar op een ander pad.</summary>
    public sealed record WeirdMatch(FileRecord New, List<FileRecord> KnownAt);

    /// <summary>
    /// Vergelijk een verse scan met een opgeslagen dictionary.
    /// Zelfde inhoud + zelfde pad = verwacht; zelfde inhoud + ander pad = raar.
    /// </summary>
    public static CompareResult Compare(HashDatabase db, IEnumerable<FileRecord> files, bool matchAbsolute)
    {
        var weird = new List<WeirdMatch>();
        var expected = new List<FileRecord>();
        var notInDb = new List<FileRecord>();

        foreach (var f in files)
        {
            if (!db.ByHash.TryGetValue(f.Hash, out var known) || known.Count == 0)
            {
                notInDb.Add(f);
                continue;
            }

            bool samePath = matchAbsolute
                ? known.Any(k => string.Equals(k.AbsolutePath, f.AbsolutePath, StringComparison.OrdinalIgnoreCase))
                : known.Any(k => string.Equals(k.RelativePath, f.RelativePath, StringComparison.OrdinalIgnoreCase));

            if (samePath) expected.Add(f);
            else weird.Add(new WeirdMatch(f, known));
        }

        return new CompareResult(weird, expected, notInDb);
    }
}
