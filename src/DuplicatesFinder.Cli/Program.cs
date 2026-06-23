using System.Text;

namespace DuplicatesFinder;

public static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return args.Length == 0 ? 1 : 0;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "scan" => RunScan(args),
                "compare" => RunCompare(args),
                _ => Unknown(args[0]),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"FOUT: {ex.Message}");
            return 2;
        }
    }

    private static int Unknown(string cmd)
    {
        Console.Error.WriteLine($"Onbekend commando: '{cmd}'. Gebruik 'scan' of 'compare'. Zie --help.");
        return 1;
    }

    // ----------------------------------------------------------------- scan

    private static int RunScan(string[] args)
    {
        var opts = ParseOptions(args);
        if (opts.Path is null)
        {
            Console.Error.WriteLine("Geef een hoofdpad op: DuplicatesFinder scan <pad> [opties]");
            return 1;
        }

        string root = Path.GetFullPath(opts.Path);
        string dbPath = opts.Db ?? Path.Combine(Environment.CurrentDirectory, "duplicates-db.dfdb");
        string logPath = opts.Log ?? Path.Combine(Environment.CurrentDirectory, "duplicates-log.txt");

        Console.WriteLine($"DuplicatesFinder {Report.Version} — scan");
        Console.WriteLine($"Hoofdpad : {root}");
        Console.WriteLine($"Threads  : {opts.Threads}");
        Console.WriteLine();

        var scanner = new Scanner(opts.Threads);
        var files = scanner.Scan(root, ConsoleProgress());
        Console.WriteLine();

        var db = Analysis.BuildDatabase(root, files);
        var duplicates = Analysis.FindDuplicates(db);

        db.Save(dbPath);
        string log = Report.BuildScanLog(root, db, duplicates, scanner.Errors);
        Report.WriteToFile(logPath, log);

        long wasted = Analysis.WastedBytes(duplicates);
        Console.WriteLine();
        Console.WriteLine($"Unieke bestanden (op inhoud): {db.ByHash.Count:N0}");
        Console.WriteLine($"Totaal bestanden            : {db.FileCount:N0}");
        Console.WriteLine($"Dubbele groepen             : {duplicates.Count:N0}");
        Console.WriteLine($"Verspilde ruimte            : {Report.FormatSize(wasted)}");
        if (scanner.Errors.Count > 0)
            Console.WriteLine($"Overgeslagen (fouten)       : {scanner.Errors.Count:N0}");
        Console.WriteLine();
        Console.WriteLine($"Dictionary opgeslagen : {dbPath}");
        Console.WriteLine($"Logfile               : {logPath}");
        return 0;
    }

    // -------------------------------------------------------------- compare

    private static int RunCompare(string[] args)
    {
        var opts = ParseOptions(args);
        if (opts.Path is null)
        {
            Console.Error.WriteLine("Geef het te vergelijken pad op: DuplicatesFinder compare <pad> --db <bestand> [opties]");
            return 1;
        }
        if (opts.Db is null || !File.Exists(opts.Db))
        {
            Console.Error.WriteLine("Geef een bestaande dictionary op met --db <bestand> (gemaakt via 'scan').");
            return 1;
        }

        string root = Path.GetFullPath(opts.Path);
        string logPath = opts.Log ?? Path.Combine(Environment.CurrentDirectory, "duplicates-compare-log.txt");
        bool matchAbsolute = opts.MatchAbsolute;

        var db = HashDatabase.Load(opts.Db);

        Console.WriteLine($"DuplicatesFinder {Report.Version} — compare");
        Console.WriteLine($"Dictionary : {opts.Db}");
        Console.WriteLine($"  van root : {db.Root}  ({db.FileCount:N0} bestanden, gemaakt {db.CreatedUtc.LocalDateTime:yyyy-MM-dd HH:mm})");
        Console.WriteLine($"Te vergelijken pad : {root}");
        Console.WriteLine($"Pad-vergelijking   : {(matchAbsolute ? "absoluut volledig pad" : "relatief t.o.v. root")}");
        Console.WriteLine();

        var scanner = new Scanner(opts.Threads);
        var files = scanner.Scan(root, ConsoleProgress());
        Console.WriteLine();

        var result = Analysis.Compare(db, files, matchAbsolute);
        string log = Report.BuildCompareLog(root, db, opts.Db!, matchAbsolute, result, scanner.Errors);
        Report.WriteToFile(logPath, log);

        Console.WriteLine();
        Console.WriteLine($"Verwachte kopieën (zelfde pad) : {result.Expected.Count:N0}");
        Console.WriteLine($"RAAR (zelfde inhoud, ander pad): {result.Weird.Count:N0}");
        Console.WriteLine($"Niet in dictionary             : {result.NotInDb.Count:N0}");
        if (scanner.Errors.Count > 0)
            Console.WriteLine($"Overgeslagen (fouten)          : {scanner.Errors.Count:N0}");
        Console.WriteLine();
        Console.WriteLine($"Logfile : {logPath}");
        return result.Weird.Count > 0 ? 3 : 0;   // exitcode 3 = er zijn rare dubbelen gevonden
    }

    // ------------------------------------------------------------- helpers

    private static IProgress<ScanProgress> ConsoleProgress() => new Progress<ScanProgress>(p =>
    {
        switch (p.Phase)
        {
            case ScanPhase.Enumerating when p.Total == 0:
                Console.Write("Bestanden inventariseren... ");
                break;
            case ScanPhase.Enumerating:
                Console.Write($"{p.Total:N0} gevonden.");
                break;
            case ScanPhase.Hashing:
                double pct = p.Total == 0 ? 100 : p.Done * 100.0 / p.Total;
                Console.Write($"\rHashen... {p.Done:N0}/{p.Total:N0} ({pct:0.0}%)   ");
                break;
        }
    });

    private sealed class Options
    {
        public string? Path;
        public string? Db;
        public string? Log;
        public int Threads = Math.Max(2, Environment.ProcessorCount);
        public bool MatchAbsolute;
    }

    private static Options ParseOptions(string[] args)
    {
        var o = new Options();
        for (int i = 1; i < args.Length; i++)
        {
            string a = args[i];
            switch (a.ToLowerInvariant())
            {
                case "--db": o.Db = Next(args, ref i, a); break;
                case "--log": o.Log = Next(args, ref i, a); break;
                case "--threads":
                    if (int.TryParse(Next(args, ref i, a), out int t) && t > 0) o.Threads = t;
                    break;
                case "--match":
                    o.MatchAbsolute = string.Equals(Next(args, ref i, a), "absolute", StringComparison.OrdinalIgnoreCase);
                    break;
                default:
                    if (a.StartsWith('-'))
                        throw new ArgumentException($"Onbekende optie: {a}");
                    o.Path ??= a;
                    break;
            }
        }
        return o;
    }

    private static string Next(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length) throw new ArgumentException($"Optie {flag} verwacht een waarde.");
        return args[++i];
    }

    private static void PrintHelp()
    {
        Console.WriteLine($"""
            DuplicatesFinder {Report.Version} — dubbele bestanden zoeken via SHA-256

            GEBRUIK
              DuplicatesFinder scan    <hoofdpad> [--db <bestand>] [--log <bestand>] [--threads N]
              DuplicatesFinder compare <pad> --db <bestand> [--log <bestand>] [--match relative|absolute] [--threads N]

            SCAN
              Loopt <hoofdpad> recursief af, berekent per bestand een SHA-256-hash,
              en bewaart alles in een compacte dictionary (1 regel per bestand:
              hash, grootte, relatief pad). Bestanden met dezelfde hash die meer dan
              1x voorkomen worden als 'dubbel' in de logfile gezet, met het aantal en
              alle volledige paden onder elkaar.
                Standaard --db  : ./duplicates-db.dfdb
                Standaard --log : ./duplicates-log.txt

            COMPARE
              Scant <pad> (bv. een externe schijf of netwerkshare) en vergelijkt elke
              hash met een eerder opgeslagen dictionary:
                - zelfde inhoud op HETZELFDE pad   -> verwachte kopie (ok)
                - zelfde inhoud op een ANDER pad   -> 'RAAR', komt in de logfile
                - niet in de dictionary            -> apart gerapporteerd
              --match relative (standaard) vergelijkt het pad t.o.v. de root, zodat
              D:\Foto's\a.jpg en E:\Backup\a.jpg als 'zelfde pad' tellen. Met
              --match absolute moet het volledige pad gelijk zijn.
                Standaard --log : ./duplicates-compare-log.txt

            VOORBEELDEN
              DuplicatesFinder scan "D:\Data" --db "C:\Tools\DuplicatesFinder\data.dfdb"
              DuplicatesFinder compare "E:\Backup" --db "C:\Tools\DuplicatesFinder\data.dfdb"

            EXITCODES
              0 = ok   1 = verkeerd gebruik   2 = fout   3 = rare dubbelen gevonden (compare)
            """);
    }
}
