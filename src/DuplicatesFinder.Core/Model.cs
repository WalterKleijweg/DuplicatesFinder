using System.Text;

namespace DuplicatesFinder;

/// <summary>One scanned file: where it lives and what it contains.</summary>
public sealed class FileRecord
{
    /// <summary>Pad t.o.v. de scan-root, met '/' als scheidingsteken (stabiel over schijven heen).</summary>
    public string RelativePath { get; set; } = "";

    /// <summary>Volledig pad. Tijdens een scan: zoals gevonden. Na laden: gereconstrueerd uit root + relpath.</summary>
    public string AbsolutePath { get; set; } = "";

    public long Size { get; set; }

    /// <summary>SHA-256 als hex (uppercase).</summary>
    public string Hash { get; set; } = "";

    /// <summary>Niet opgeslagen in de dictionary; alleen tijdens een live scan gevuld.</summary>
    public DateTimeOffset LastModifiedUtc { get; set; }
}

/// <summary>
/// De dictionary: in het geheugen exact hash -> alle bestanden met die inhoud.
/// Op schijf een compact tekstbestand met één regel per bestand (geen redundantie):
///   #root  &lt;root&gt;
///   &lt;hash&gt;\t&lt;grootte&gt;\t&lt;relatief-pad&gt;
/// De hash staat alleen vooraan de regel; het volledige pad = root + relpath en
/// wordt bij het laden gereconstrueerd. Veel kleiner dan ingesprongen JSON.
/// </summary>
public sealed class HashDatabase
{
    public const string FormatTag = "DuplicatesFinder-db v1";

    public string Root { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }

    /// <summary>Sleutel = SHA-256 hex. Waarde = alle bestanden met die hash.</summary>
    public Dictionary<string, List<FileRecord>> ByHash { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Totaal aantal bestanden over alle hashes.</summary>
    public int FileCount => ByHash.Values.Sum(v => v.Count);

    public void Add(FileRecord f)
    {
        if (!ByHash.TryGetValue(f.Hash, out var list))
            ByHash[f.Hash] = list = new List<FileRecord>();
        list.Add(f);
    }

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var w = new StreamWriter(path, append: false, new UTF8Encoding(false));
        w.WriteLine($"#{FormatTag}");
        w.WriteLine($"#root\t{Root}");
        w.WriteLine($"#created\t{CreatedUtc:O}");
        w.WriteLine("#columns\thash\tsize\trelpath");
        foreach (var (hash, list) in ByHash)
            foreach (var f in list)
                w.WriteLine($"{hash}\t{f.Size}\t{Sanitize(f.RelativePath)}");
    }

    public static HashDatabase Load(string path)
    {
        var db = new HashDatabase();
        using var r = new StreamReader(path, Encoding.UTF8);

        string? line;
        bool sawData = false;
        while ((line = r.ReadLine()) is not null)
        {
            if (line.Length == 0) continue;

            if (line[0] == '#')
            {
                var meta = line.Split('\t', 2);
                if (meta.Length == 2 && meta[0] == "#root") db.Root = meta[1];
                else if (meta.Length == 2 && meta[0] == "#created" &&
                         DateTimeOffset.TryParse(meta[1], out var created)) db.CreatedUtc = created;
                continue;
            }

            var parts = line.Split('\t', 3);   // relpath mag verder geen tabs bevatten (gesanitized)
            if (parts.Length < 3) continue;
            if (!long.TryParse(parts[1], out long size)) size = 0;

            string rel = parts[2];
            db.Add(new FileRecord
            {
                Hash = parts[0],
                Size = size,
                RelativePath = rel,
                AbsolutePath = ReconstructAbsolute(db.Root, rel),
            });
            sawData = true;
        }

        if (!sawData && db.Root.Length == 0)
            throw new InvalidDataException($"Geen geldige dictionary gevonden in: {path}");
        return db;
    }

    private static string ReconstructAbsolute(string root, string rel)
    {
        if (string.IsNullOrEmpty(root)) return rel;
        return Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>Tabs/regeleindes in een padnaam zijn op Windows onmogelijk, maar wees defensief.</summary>
    private static string Sanitize(string s) => s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
