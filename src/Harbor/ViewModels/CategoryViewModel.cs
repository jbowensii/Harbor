using System.Collections.ObjectModel;

namespace Harbor.ViewModels;

/// <summary>
/// A named section in the list. Categories exist independently of their contents, so an
/// empty one still renders - that is what makes them feel like something you manage
/// rather than a label that happens to be on a server.
/// </summary>
public sealed class CategoryViewModel : ObservableObject
{
    private string _name;

    public CategoryViewModel(string name, IEnumerable<ServerItemViewModel> servers)
    {
        _name = name;
        Servers = new ObservableCollection<ServerItemViewModel>(servers);
    }

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    public ObservableCollection<ServerItemViewModel> Servers { get; }

    public bool IsEmpty => Servers.Count == 0;

    public string CountText
    {
        get
        {
            var running = Servers.Count(s => s.StatusKey is "Running" or "Starting");
            if (Servers.Count == 0) return string.Empty;
            return running > 0 ? $"{running}/{Servers.Count}" : Servers.Count.ToString();
        }
    }

    public void RefreshCounts() => Raise(nameof(CountText));
}
