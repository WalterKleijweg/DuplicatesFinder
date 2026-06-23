using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DuplicatesFinder;   // Core

namespace DuplicatesFinder.Gui.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private CancellationTokenSource? _cts;
    private string? _lastLogPath;

    public MainViewModel()
    {
        RunCommand = new AsyncRelayCommand(RunAsync, () => !IsBusy);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsBusy);
        OpenLogCommand = new RelayCommand(OpenLog,
            () => !IsBusy && !string.IsNullOrEmpty(_lastLogPath) && File.Exists(_lastLogPath));
    }

    public AsyncRelayCommand RunCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand OpenLogCommand { get; }

    // ---- modus (twee elkaar uitsluitende radioknoppen) ----

    private bool _isScanMode = true;
    public bool IsScanMode
    {
        get => _isScanMode;
        set
        {
            if (!SetField(ref _isScanMode, value)) return;
            OnPropertyChanged(nameof(IsCompareMode));
            OnPropertyChanged(nameof(DbLabel));
            OnPropertyChanged(nameof(RunLabel));
        }
    }

    public bool IsCompareMode
    {
        get => !_isScanMode;
        set => IsScanMode = !value;
    }

    public string DbLabel => IsScanMode ? "Dictionary (opslaan)" : "Dictionary (laden)";
    public string RunLabel => IsScanMode ? "Scannen" : "Vergelijken";

    // ---- invoervelden ----

    private string _rootPath = "";
    public string RootPath { get => _rootPath; set => SetField(ref _rootPath, value); }

    private string _dbPath = "";
    public string DbPath { get => _dbPath; set => SetField(ref _dbPath, value); }

    private string _logPath = "";
    public string LogPath { get => _logPath; set => SetField(ref _logPath, value); }

    private int _threads = Math.Max(2, Environment.ProcessorCount);
    public int Threads { get => _threads; set => SetField(ref _threads, value); }

    private bool _matchAbsolute;
    public bool MatchAbsolute { get => _matchAbsolute; set => SetField(ref _matchAbsolute, value); }

    // ---- status ----

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set { if (SetField(ref _isBusy, value)) RaiseCommands(); }
    }

    private string _statusText = "Kies een map en start.";
    public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }

    private string _summary = "";
    public string Summary { get => _summary; set => SetField(ref _summary, value); }

    private double _progressValue;
    public double ProgressValue { get => _progressValue; set => SetField(ref _progressValue, value); }

    private bool _progressIndeterminate;
    public bool ProgressIndeterminate { get => _progressIndeterminate; set => SetField(ref _progressIndeterminate, value); }

    public ObservableCollection<ResultGroup> Groups { get; } = new();

    // ---- uitvoering ----

    private async Task RunAsync()
    {
        if (string.IsNullOrWhiteSpace(RootPath) || !Directory.Exists(RootPath))
        {
            StatusText = "Kies eerst een bestaande map.";
            return;
        }

        bool compare = IsCompareMode;
        string root = Path.GetFullPath(RootPath);

        if (compare && (string.IsNullOrWhiteSpace(DbPath) || !File.Exists(DbPath)))
        {
            StatusText = "Kies een bestaande dictionary (.dfdb) om mee te vergelijken.";
            return;
        }

        if (string.IsNullOrWhiteSpace(DbPath))
            DbPath = Path.Combine(root, "duplicates-db.dfdb");
        if (string.IsNullOrWhiteSpace(LogPath))
            LogPath = Path.Combine(root, compare ? "duplicates-compare-log.txt" : "duplicates-log.txt");

        string dbPath = DbPath;
        string logPath = LogPath;
        bool matchAbsolute = MatchAbsolute;
        int threads = Threads <= 0 ? 1 : Threads;

        Groups.Clear();
        Summary = "";
        IsBusy = true;
        ProgressIndeterminate = true;
        ProgressValue = 0;
        StatusText = "Bezig met inventariseren…";

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        var progress = new Progress<ScanProgress>(p =>
        {
            switch (p.Phase)
            {
                case ScanPhase.Enumerating:
                    ProgressIndeterminate = true;
                    StatusText = p.Total > 0 ? $"{p.Total:N0} bestanden gevonden — hashen…" : "Inventariseren…";
                    break;
                case ScanPhase.Hashing:
                    ProgressIndeterminate = false;
                    ProgressValue = p.Total == 0 ? 0 : p.Done * 100.0 / p.Total;
                    StatusText = $"Hashen… {p.Done:N0} / {p.Total:N0}";
                    break;
            }
        });

        try
        {
            if (compare)
            {
                var (result, db, errorCount, log) = await Task.Run(() =>
                {
                    var loaded = HashDatabase.Load(dbPath);
                    var scanner = new Scanner(threads);
                    var files = scanner.Scan(root, progress, ct);
                    var res = Analysis.Compare(loaded, files, matchAbsolute);
                    var text = Report.BuildCompareLog(root, loaded, dbPath, matchAbsolute, res, scanner.Errors);
                    return (res, loaded, scanner.Errors.Count, text);
                }, ct);

                Report.WriteToFile(logPath, log);
                _lastLogPath = logPath;
                PopulateCompare(result);
                Summary = $"Verwachte kopieën: {result.Expected.Count:N0}   •   RAAR (ander pad): {result.Weird.Count:N0}   •   " +
                          $"Niet in dictionary: {result.NotInDb.Count:N0}   •   Overgeslagen: {errorCount:N0}";
                StatusText = $"Klaar — logfile: {logPath}";
            }
            else
            {
                var (dups, db, errorCount, log) = await Task.Run(() =>
                {
                    var scanner = new Scanner(threads);
                    var files = scanner.Scan(root, progress, ct);
                    var database = Analysis.BuildDatabase(root, files);
                    database.Save(dbPath);
                    var d = Analysis.FindDuplicates(database);
                    var text = Report.BuildScanLog(root, database, d, scanner.Errors);
                    return (d, database, scanner.Errors.Count, text);
                }, ct);

                Report.WriteToFile(logPath, log);
                _lastLogPath = logPath;
                PopulateScan(dups);
                long wasted = Analysis.WastedBytes(dups);
                Summary = $"Unieke: {db.ByHash.Count:N0}   •   Totaal: {db.FileCount:N0}   •   Dubbele groepen: {dups.Count:N0}   •   " +
                          $"Verspild: {Report.FormatSize(wasted)}   •   Overgeslagen: {errorCount:N0}";
                StatusText = $"Klaar — dictionary: {dbPath}";
            }

            ProgressIndeterminate = false;
            ProgressValue = 100;
        }
        catch (OperationCanceledException)
        {
            StatusText = "Geannuleerd.";
            ProgressIndeterminate = false;
            ProgressValue = 0;
        }
        catch (Exception ex)
        {
            StatusText = "Fout: " + ex.Message;
            ProgressIndeterminate = false;
            ProgressValue = 0;
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
            RaiseCommands();
        }
    }

    private void PopulateScan(List<List<FileRecord>> dups)
    {
        if (dups.Count == 0)
        {
            Groups.Add(new ResultGroup { Title = "Geen dubbele bestanden gevonden." });
            return;
        }

        int i = 0;
        foreach (var g in dups)
        {
            i++;
            var grp = new ResultGroup
            {
                Title = $"[{i}] komt {g.Count}× voor — {Report.FormatSize(g[0].Size)} per stuk",
                Subtitle = $"hash {g[0].Hash}",
            };
            foreach (var f in g.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase))
                grp.Paths.Add(f.AbsolutePath);
            Groups.Add(grp);
        }
    }

    private void PopulateCompare(Analysis.CompareResult r)
    {
        if (r.Weird.Count == 0)
        {
            Groups.Add(new ResultGroup
            {
                Title = "Geen rare dubbelen — alle gevonden kopieën staan op hetzelfde pad als in de dictionary.",
            });
            return;
        }

        int i = 0;
        foreach (var m in r.Weird.OrderByDescending(x => x.New.Size))
        {
            i++;
            var grp = new ResultGroup
            {
                IsWarning = true,
                Title = $"[{i}] RAAR — {Report.FormatSize(m.New.Size)} — zelfde inhoud, ander pad",
                Subtitle = $"hash {m.New.Hash}",
            };
            grp.Paths.Add("hier:    " + m.New.AbsolutePath);
            foreach (var k in m.KnownAt.OrderBy(k => k.RelativePath, StringComparer.OrdinalIgnoreCase))
                grp.Paths.Add($"in dict: {k.AbsolutePath}   (relatief: {k.RelativePath})");
            Groups.Add(grp);
        }
    }

    private void OpenLog()
    {
        if (string.IsNullOrEmpty(_lastLogPath) || !File.Exists(_lastLogPath)) return;
        Process.Start(new ProcessStartInfo(_lastLogPath) { UseShellExecute = true });
    }

    private void RaiseCommands()
    {
        RunCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        OpenLogCommand.RaiseCanExecuteChanged();
    }
}
