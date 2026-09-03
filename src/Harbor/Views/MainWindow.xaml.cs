using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Harbor.ViewModels;

namespace Harbor.Views;

public partial class MainWindow : Window
{
    private MainViewModel? _vm;
    private INotifyCollectionChanged? _watchedLog;

    public MainWindow()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            Services.ThemeService.ApplyTitleBar(this);
            Services.ThemeService.EffectiveThemeChanged += () => Services.ThemeService.ApplyTitleBar(this);

            _vm = DataContext as MainViewModel;
            if (_vm is not null)
            {
                _vm.PropertyChanged += OnViewModelPropertyChanged;
                WatchSelectedLog();
            }
        };
    }

    /// <summary>Keeps the console pinned to the newest line while a server is booting.</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Selected)) WatchSelectedLog();
    }

    private void WatchSelectedLog()
    {
        if (_watchedLog is not null)
        {
            _watchedLog.CollectionChanged -= OnLogChanged;
            _watchedLog = null;
        }

        if (_vm?.Selected?.Log is INotifyCollectionChanged log)
        {
            _watchedLog = log;
            log.CollectionChanged += OnLogChanged;
        }

        LogScroll?.ScrollToEnd();
    }

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
            Dispatcher.BeginInvoke(() => LogScroll?.ScrollToEnd());
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_vm is not null)
        {
            var running = _vm.AllServers.Count(s => s.CanStop);
            if (running > 0)
            {
                var answer = MessageBox.Show(
                    $"{running} server{(running == 1 ? " is" : "s are")} still running.\n\n" +
                    "Closing Harbor stops them. Continue?",
                    "Servers running",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Question);

                if (answer != MessageBoxResult.OK)
                {
                    e.Cancel = true;
                    return;
                }
            }

            _vm.Persist();
            _vm.Dispose();
        }

        base.OnClosing(e);
    }

}
