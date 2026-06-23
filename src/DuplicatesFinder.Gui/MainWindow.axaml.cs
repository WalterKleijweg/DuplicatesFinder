using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DuplicatesFinder.Gui.ViewModels;

namespace DuplicatesFinder.Gui;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private MainViewModel? Vm => DataContext as MainViewModel;

    private async void OnBrowseRoot(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Kies de map om te scannen",
            AllowMultiple = false,
        });
        if (folders.Count > 0)
            Vm.RootPath = folders[0].Path.LocalPath;
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
            if (file is not null) Vm.DbPath = file.Path.LocalPath;
        }
        else
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Dictionary laden",
                AllowMultiple = false,
                FileTypeFilter = new[] { dictType },
            });
            if (files.Count > 0) Vm.DbPath = files[0].Path.LocalPath;
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
        if (file is not null) Vm.LogPath = file.Path.LocalPath;
    }
}
