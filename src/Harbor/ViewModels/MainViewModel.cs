using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using Harbor.Models;
using Harbor.Services;
using System.Windows.Threading;

namespace Harbor.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly ConfigStore _store = new();
    private readonly PortMonitor _monitor = new();
    private readonly DispatcherTimer _timer;

    private ServerItemViewModel? _selected;
    private string _search = string.Empty;
    private bool _isPolling;

    public MainViewModel()
    {
        var config = _store.Load();

        CategoryNames = new ObservableCollection<string>(config.Categories);

        foreach (var entry in config.Servers)
            AllServers.Add(new ServerItemViewModel(entry));

        AddCommand = new RelayCommand(AddServer);
        EditCommand = new RelayCommand(p => EditServer(p as ServerItemViewModel ?? Selected), p => (p ?? Selected) is not null);
        DeleteCommand = new RelayCommand(p => DeleteServer(p as ServerItemViewModel ?? Selected), p => (p ?? Selected) is not null);
        StartCommand = new RelayCommand(p => (p as ServerItemViewModel)?.Start());
        StopCommand = new RelayCommand(p => (p as ServerItemViewModel)?.Stop());
        OpenCommand = new RelayCommand(p => (p as ServerItemViewModel)?.OpenInBrowser());
        OpenFolderCommand = new RelayCommand(p => (p as ServerItemViewModel)?.OpenFolder());
        SelectCommand = new RelayCommand(p => { if (p is ServerItemViewModel vm) Selected = vm; });
        StopAllCommand = new RelayCommand(StopAll);
        OpenConfigFolderCommand = new RelayCommand(OpenConfigFolder);
        ManageCategoriesCommand = new RelayCommand(ManageCategories);
        SetThemeCommand = new RelayCommand(SetTheme);

        ThemeService.EffectiveThemeChanged += RaiseThemeProperties;

        Rebuild();
        Selected = AllServers.FirstOrDefault();

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += async (_, _) => await PollAsync();
        _timer.Start();

        _ = PollAsync();
    }

    /// <summary>Flat list, used for persistence, polling and conflict detection.</summary>
    public List<ServerItemViewModel> AllServers { get; } = new();

    /// <summary>What the list actually renders: ordered categories, each with its servers.</summary>
    public ObservableCollection<CategoryViewModel> Categories { get; } = new();

    /// <summary>Category order and membership, independent of any server.</summary>
    public ObservableCollection<string> CategoryNames { get; }

    public ServerItemViewModel? Selected
    {
        get => _selected;
        set
        {
            if (_selected is not null) _selected.IsSelected = false;

            if (Set(ref _selected, value))
            {
                if (_selected is not null) _selected.IsSelected = true;
                Raise(nameof(HasSelection));
            }
        }
    }

    public bool HasSelection => Selected is not null;

    public string Search
    {
        get => _search;
        set { if (Set(ref _search, value)) Rebuild(); }
    }

    public string SummaryText
    {
        get
        {
            var running = AllServers.Count(s => s.Status is ServerStatus.Running or ServerStatus.Starting);
            var external = AllServers.Count(s => s.Status == ServerStatus.External);

            var text = $"{running} running of {AllServers.Count}";
            if (external > 0) text += $"  ·  {external} started outside Harbor";
            return text;
        }
    }

    public string ConflictWarning
    {
        get
        {
            var groups = AllServers
                .Where(s => s.Port > 0)
                .GroupBy(s => s.Port)
                .Where(g => g.Count() > 1)
                .ToList();

            if (groups.Count == 0) return string.Empty;

            var parts = groups.Select(g => $"port {g.Key}: {string.Join(" + ", g.Select(s => s.Name))}");
            return "Port conflict  ·  " + string.Join("     ", parts);
        }
    }

    public bool HasConflict => !string.IsNullOrEmpty(ConflictWarning);

    public bool IsEmpty => AllServers.Count == 0;

    public RelayCommand AddCommand { get; }
    public RelayCommand EditCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand OpenCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand SelectCommand { get; }
    public RelayCommand StopAllCommand { get; }
    public RelayCommand OpenConfigFolderCommand { get; }
    public RelayCommand ManageCategoriesCommand { get; }
    public RelayCommand SetThemeCommand { get; }

    // Drives the three-way theme control in the header.
    //
    // These are strings rather than bools because they are bound to Button.Tag, which is
    // typed object: a Trigger comparing it against Value="True" would compare the literal
    // string "True" to a boxed boolean and never match. Comparing string to string does.
    public string ThemeSystemState => ThemeService.Mode == AppTheme.System ? "on" : "off";
    public string ThemeLightState => ThemeService.Mode == AppTheme.Light ? "on" : "off";
    public string ThemeDarkState => ThemeService.Mode == AppTheme.Dark ? "on" : "off";

    private void SetTheme(object? parameter)
    {
        if (parameter is not string name || !Enum.TryParse<AppTheme>(name, out var mode)) return;
        if (mode == ThemeService.Mode) return;

        ThemeService.Apply(mode);
        Persist();
    }

    private void RaiseThemeProperties()
    {
        Raise(nameof(ThemeSystemState));
        Raise(nameof(ThemeLightState));
        Raise(nameof(ThemeDarkState));
    }

    /// <summary>
    /// Rebuilds the category sections from the flat list. Empty categories are kept when
    /// browsing and dropped while searching, where they would only be noise.
    /// </summary>
    private void Rebuild()
    {
        var searching = !string.IsNullOrWhiteSpace(Search);

        Categories.Clear();

        foreach (var name in CategoryNames)
        {
            var members = AllServers
                .Where(s => string.Equals(s.Group, name, StringComparison.OrdinalIgnoreCase))
                .Where(Matches)
                .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            if (searching && members.Count == 0) continue;

            Categories.Add(new CategoryViewModel(name, members));
        }

        Raise(nameof(IsEmpty));
    }

    private bool Matches(ServerItemViewModel vm)
    {
        if (string.IsNullOrWhiteSpace(Search)) return true;

        var q = Search.Trim();
        return vm.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
               || vm.Group.Contains(q, StringComparison.OrdinalIgnoreCase)
               || vm.Command.Contains(q, StringComparison.OrdinalIgnoreCase)
               || vm.WorkingDirectory.Contains(q, StringComparison.OrdinalIgnoreCase)
               || vm.Port.ToString().Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private async Task PollAsync()
    {
        if (_isPolling) return;
        _isPolling = true;

        try
        {
            _monitor.RefreshLocal();

            var remote = AllServers.Where(s => !PortMonitor.IsLocalHost(s.Entry.Host)).ToList();
            var probes = remote.ToDictionary(s => s, s => _monitor.ProbeRemoteAsync(s.Entry.Host, s.Entry.Port));

            if (probes.Count > 0)
                await Task.WhenAll(probes.Values).ConfigureAwait(true);

            foreach (var vm in AllServers)
            {
                bool listening = probes.TryGetValue(vm, out var task)
                    ? task.Result
                    : _monitor.IsLocalPortListening(vm.Entry.Port);

                vm.RefreshStatus(listening);
            }

            foreach (var category in Categories) category.RefreshCounts();
            Raise(nameof(SummaryText));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Poll failed: {ex.Message}");
        }
        finally
        {
            _isPolling = false;
        }
    }

    private void AddServer()
    {
        var entry = new ServerEntry
        {
            Name = "New server",
            Group = CategoryNames.FirstOrDefault() ?? "Uncategorised",
            Host = "127.0.0.1",
            Port = 3000
        };

        var dialog = new Views.EditServerWindow(entry, CategoryNames, isNew: true)
        {
            Owner = Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true) return;

        EnsureCategory(dialog.Result.Group);

        var vm = new ServerItemViewModel(dialog.Result);
        AllServers.Add(vm);
        Persist();
        Selected = vm;
    }

    private void EditServer(ServerItemViewModel? vm)
    {
        if (vm is null) return;

        var dialog = new Views.EditServerWindow(vm.Entry.Clone(), CategoryNames, isNew: false)
        {
            Owner = Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true) return;

        EnsureCategory(dialog.Result.Group);
        vm.UpdateFrom(dialog.Result);
        Persist();
        Selected = vm;
    }

    private void DeleteServer(ServerItemViewModel? vm)
    {
        if (vm is null) return;

        var verb = vm.CanStop ? "It is running and will be stopped first.\n\n" : string.Empty;
        var answer = MessageBox.Show(
            $"Remove \"{vm.Name}\" from Harbor?\n\n{verb}This only deletes the Harbor entry. Nothing on disk is touched.",
            "Remove server",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.OK) return;

        vm.Stop();
        vm.Dispose();
        AllServers.Remove(vm);
        Persist();
        Selected = AllServers.FirstOrDefault();
    }

    private void ManageCategories()
    {
        var dialog = new Views.CategoryManagerWindow(CategoryNames, CountFor)
        {
            Owner = Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true) return;

        // Renames have to follow through to every server that referenced the old name.
        foreach (var (oldName, newName) in dialog.Renames)
        {
            foreach (var server in AllServers.Where(s => string.Equals(s.Group, oldName, StringComparison.OrdinalIgnoreCase)))
                server.SetGroup(newName);
        }

        // Anything whose category was deleted lands in Uncategorised rather than vanishing.
        var kept = dialog.Result;
        foreach (var server in AllServers)
        {
            if (!kept.Contains(server.Group, StringComparer.OrdinalIgnoreCase))
            {
                if (!kept.Contains("Uncategorised", StringComparer.OrdinalIgnoreCase))
                    kept.Add("Uncategorised");
                server.SetGroup("Uncategorised");
            }
        }

        CategoryNames.Clear();
        foreach (var name in kept) CategoryNames.Add(name);

        Persist();
    }

    private int CountFor(string category)
        => AllServers.Count(s => string.Equals(s.Group, category, StringComparison.OrdinalIgnoreCase));

    private void EnsureCategory(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!CategoryNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            CategoryNames.Add(name.Trim());
    }

    private void StopAll()
    {
        foreach (var vm in AllServers.Where(s => s.CanStop).ToList()) vm.Stop();
    }

    private void OpenConfigFolder()
    {
        try
        {
            System.IO.Directory.CreateDirectory(_store.Directory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_store.Directory}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
        }
    }

    private void RecomputeConflicts()
    {
        var counts = AllServers.Where(s => s.Port > 0)
                               .GroupBy(s => s.Port)
                               .ToDictionary(g => g.Key, g => g.Count());

        foreach (var vm in AllServers)
            vm.HasPortConflict = vm.Port > 0 && counts.TryGetValue(vm.Port, out var n) && n > 1;

        Raise(nameof(ConflictWarning));
        Raise(nameof(HasConflict));
    }

    public void Persist()
    {
        // A save that cannot write - file locked by another instance, antivirus holding it,
        // a sync client mid-upload - must not take the window down with it. Log it and carry
        // on with what is in memory; the next save usually succeeds.
        try
        {
            _store.Save(new HarborConfig
            {
                Theme = ThemeService.Mode,
                Categories = CategoryNames.ToList(),
                Servers = AllServers.Select(s => s.Entry).ToList()
            });
        }
        catch (Exception ex)
        {
            Log.Error("could not save the configuration", ex);
        }

        RecomputeConflicts();
        Rebuild();
        Raise(nameof(SummaryText));
    }

    public void Dispose()
    {
        ThemeService.EffectiveThemeChanged -= RaiseThemeProperties;
        _timer.Stop();
        foreach (var vm in AllServers) vm.Dispose();
    }
}
