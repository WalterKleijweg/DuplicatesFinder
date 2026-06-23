using System.Collections.ObjectModel;
using Avalonia.Media;

namespace DuplicatesFinder.Gui.ViewModels;

/// <summary>Eén regel in de resultatenlijst: een titel + de paden eronder.</summary>
public sealed class ResultGroup
{
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public bool IsWarning { get; init; }
    public ObservableCollection<string> Paths { get; init; } = new();

    public bool HasSubtitle => !string.IsNullOrEmpty(Subtitle);

    public IBrush Background => IsWarning
        ? new SolidColorBrush(Color.Parse("#FFF3E0"))   // licht oranje = let op
        : new SolidColorBrush(Color.Parse("#F4F6F8"));  // neutraal grijs
}
