using System.Windows;
using System.Windows.Threading;
using Harbor.Services;

namespace Harbor;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Log.StartSession();

        // Resolve the theme before StartupUri builds MainWindow, so the first paint is
        // already correct rather than flashing light and then switching.
        var stored = new ConfigStore().Load().Theme;
        ThemeService.Apply(stored);
        ThemeService.StartWatchingSystem();
        Log.Info($"theme = {stored} (effective: {(ThemeService.IsDark ? "dark" : "light")})");

        DispatcherUnhandledException += OnUnhandled;
    }

    private void OnUnhandled(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("unhandled exception on the UI thread", e.Exception);

        MessageBox.Show(
            e.Exception.Message,
            "Harbor hit an error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Info($"exiting with code {e.ApplicationExitCode}");
        base.OnExit(e);
    }
}
