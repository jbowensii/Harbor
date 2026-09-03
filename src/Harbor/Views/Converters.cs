using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Harbor.Views;

/// <summary>Empty or whitespace string collapses the element.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>stderr lines are tinted red in the log console.</summary>
public sealed class LogBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromRgb(0xA9, 0x48, 0x3C));
    private static readonly SolidColorBrush NormalBrush = new(Color.FromRgb(0x45, 0x41, 0x3C));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? ErrorBrush : NormalBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
