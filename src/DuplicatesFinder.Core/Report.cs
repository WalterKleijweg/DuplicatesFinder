using System.Text;

namespace DuplicatesFinder;

/// <summary>Bouwt de logfile-teksten en formatteert groottes. Gedeeld door CLI en GUI.</summary>
public static class Report
{
    public const string Version = "1.0";

    public static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int u = 0;
        while (size >= 1024 && u < units.Length - 1) { size /= 1024; u++; }
        return u == 0 ? $"{bytes} B" : $"{size:0.##} {units[u]}";
    }

    public static string BuildScanLog(string root, HashDatabase db,
        List<List<FileRecord>> duplicates, IReadOnlyList<(string Path, string Error)> errors)
    {
        var w = new StringBuilder();
        long wasted = Analysis.WastedBytes(duplicates);

        w.AppendLine("============================================================");
        w.AppendLine($" DuplicatesFinder {Version} — scan-rapport");
        w.AppendLine($" Hoofdpad : {root}");
        w.AppendLine($" Datum    : {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}");
        w.AppendLine($" Unieke bestanden (inhoud) : {db.ByHash.Count:N0}");
        w.AppendLine($" Totaal bestanden          : {db.FileCount:N0}");
        w.AppendLine($" Dubbele groepen           : {duplicates.Count:N0}");
        w.AppendLine($" Verspilde ruimte          : {FormatSize(wasted)}");
        w.AppendLine("============================================================");
        w.AppendLine();

        w.AppendLine("DUBBELE BESTANDEN (zelfde inhoud, komt meer dan 1x voor)");
        w.AppendLine("------------------------------------------------------------");
        if (duplicates.Count == 0)
        {
            w.AppendLine("Geen dubbelen gevonden.");
        }
        else
        {
            int i = 0;
            foreach (var group in duplicates)
            {
                i++;
                var first = group[0];
                w.AppendLine($"[{i}] komt {group.Count}x voor — {FormatSize(first.Size)} per stuk — hash {first.Hash}");
                foreach (var f in group.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase))
                    w.AppendLine($"      {f.AbsolutePath}");
                w.AppendLine();
            }
        }

        AppendErrors(w, errors);
        return w.ToString();
    }

    public static string BuildCompareLog(string root, HashDatabase db, string dbPath, bool matchAbsolute,
        Analysis.CompareResult result, IReadOnlyList<(string Path, string Error)> errors)
    {
        var w = new StringBuilder();

        w.AppendLine("============================================================");
        w.AppendLine($" DuplicatesFinder {Version} — vergelijkings-rapport");
        w.AppendLine($" Dictionary        : {dbPath}");
        w.AppendLine($"   oorspr. root    : {db.Root}");
        w.AppendLine($"   gemaakt         : {db.CreatedUtc.LocalDateTime:yyyy-MM-dd HH:mm}");
        w.AppendLine($" Vergeleken pad    : {root}");
        w.AppendLine($" Pad-vergelijking  : {(matchAbsolute ? "absoluut volledig pad" : "relatief t.o.v. root")}");
        w.AppendLine($" Datum             : {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}");
        w.AppendLine($" Verwachte kopieën : {result.Expected.Count:N0}");
        w.AppendLine($" RARE dubbelen     : {result.Weird.Count:N0}");
        w.AppendLine($" Niet in dictionary: {result.NotInDb.Count:N0}");
        w.AppendLine("============================================================");
        w.AppendLine();

        w.AppendLine("RARE DUBBELEN — zelfde inhoud, maar op een ANDER pad dan in de dictionary");
        w.AppendLine("------------------------------------------------------------");
        if (result.Weird.Count == 0)
        {
            w.AppendLine("Geen rare dubbelen gevonden.");
        }
        else
        {
            int i = 0;
            foreach (var m in result.Weird.OrderByDescending(x => x.New.Size))
            {
                i++;
                w.AppendLine($"[{i}] {FormatSize(m.New.Size)} — hash {m.New.Hash}");
                w.AppendLine($"      hier   : {m.New.AbsolutePath}");
                w.AppendLine($"      in dict: {m.KnownAt.Count}x bekend op:");
                foreach (var k in m.KnownAt.OrderBy(k => k.RelativePath, StringComparer.OrdinalIgnoreCase))
                    w.AppendLine($"               {k.AbsolutePath}   (relatief: {k.RelativePath})");
                w.AppendLine();
            }
        }

        w.AppendLine();
        w.AppendLine("NIET IN DICTIONARY — bestanden hier die niet in de opgeslagen scan zaten");
        w.AppendLine("------------------------------------------------------------");
        if (result.NotInDb.Count == 0)
            w.AppendLine("Geen.");
        else
            foreach (var f in result.NotInDb.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase))
                w.AppendLine($"      {f.AbsolutePath}");

        w.AppendLine();
        AppendErrors(w, errors);
        return w.ToString();
    }

    private static void AppendErrors(StringBuilder w, IReadOnlyList<(string Path, string Error)> errors)
    {
        if (errors.Count == 0) return;
        w.AppendLine();
        w.AppendLine($"OVERGESLAGEN ({errors.Count}) — niet leesbaar / toegang geweigerd");
        w.AppendLine("------------------------------------------------------------");
        foreach (var (p, e) in errors)
            w.AppendLine($"      {p}  —  {e}");
    }

    public static void WriteToFile(string logPath, string content)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(logPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(logPath, content, new UTF8Encoding(false));
    }
}
