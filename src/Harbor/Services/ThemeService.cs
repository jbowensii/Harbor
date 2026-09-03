using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Harbor.Models;
using Microsoft.Win32;

namespace Harbor.Services;

/// <summary>
/// Swaps the merged theme dictionary at runtime.
///
/// Light.xaml and Dark.xaml define an identical set of keys, so switching is a single
/// dictionary replacement. That only reaches the UI because the views bind these keys with
/// DynamicResource; a StaticResource would have been resolved once at load and would keep
/// pointing at the old brush.
/// </summary>
public static class ThemeService
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static AppTheme _mode = AppTheme.System;

    /// <summary>Raised when the effective (resolved) theme changes, so windows can restyle chrome.</summary>
    public static event Action? EffectiveThemeChanged;

    public static AppTheme Mode => _mode;

    public static bool IsDark => _mode switch
    {
        AppTheme.Dark => true,
        AppTheme.Light => false,
        _ => SystemPrefersDark()
    };

    public static void Apply(AppTheme mode)
    {
        _mode = mode;

        var source = new Uri(IsDark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative);
        var dictionary = (ResourceDictionary)Application.LoadComponent(source);

        var merged = Application.Current.Resources.MergedDictionaries;
        if (merged.Count == 0) merged.Add(dictionary);
        else merged[0] = dictionary;

        EffectiveThemeChanged?.Invoke();
    }

    /// <summary>Re-resolves the theme when Windows switches between light and dark.</summary>
    public static void StartWatchingSystem()
    {
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category != UserPreferenceCategory.General) return;
            if (_mode != AppTheme.System) return;

            Application.Current?.Dispatcher.BeginInvoke(() => Apply(AppTheme.System));
        };
    }

    private static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            // AppsUseLightTheme: 1 = light, 0 = dark. Absent on very old builds, where light wins.
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Matches the native title bar to the current theme.</summary>
    public static void ApplyTitleBar(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;

            int dark = IsDark ? 1 : 0;
            if (DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
        }
        catch (DllNotFoundException)
        {
            // Not fatal - the window keeps the system default title bar.
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
