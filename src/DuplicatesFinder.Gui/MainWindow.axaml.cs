using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DuplicatesFinder.Gui.ViewModels;

namespace DuplicatesFinder.Gui;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (Vm is not null) Vm.RequestSavePath = RequestSavePathAsync;
        };
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    /// <summary>Toont een opslaan-dialoog wanneer de standaardlocatie niet beschrijfbaar bleek.</summary>
    private async Task<string?> RequestSavePathAsync(string title, string suggestedName, string ext)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = ext,
        });
        return file?.TryGetLocalPath();
    }

    private async void OnBrowseRoot(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Kies de map om te scannen",
            AllowMultiple = false,
        });
        if (folders.Count > 0)
            Vm.RootPath = folders[0].TryGetLocalPath();
    }

    private async void OnBrowseDb(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var dictType = new FilePickerFileType("DuplicatesFinder dictionary") { Patterns = new[] { "*.dfdb" } };

        if (Vm.IsScanMode)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Dictionary opslaan als",
                SuggestedFileName = "duplicates-db.dfdb",
                DefaultExtension = "dfdb",
                FileTypeChoices = new[] { dictType },
            });
            if (file is not null) Vm.DbPath = file.TryGetLocalPath();
        }
        else
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Dictionary laden",
                AllowMultiple = false,
                FileTypeFilter = new[] { dictType },
            });
            if (files.Count > 0) Vm.DbPath = files[0].TryGetLocalPath();
        }
    }

    private async void OnBrowseLog(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Logfile opslaan als",
            SuggestedFileName = "duplicates-log.txt",
            DefaultExtension = "txt",
            FileTypeChoices = new[] { new FilePickerFileType("Tekstbestand") { Patterns = new[] { "*.txt" } } },
        });
        if (file is not null) Vm.LogPath = file.TryGetLocalPath();
    }
}
