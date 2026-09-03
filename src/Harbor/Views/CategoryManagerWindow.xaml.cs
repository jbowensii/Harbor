using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Harbor.Views;

/// <summary>One row in the category manager: the name plus how many servers reference it.</summary>
public sealed class CategoryRow : INotifyPropertyChanged
{
    private string _name;

    public CategoryRow(string name, string originalName, int count)
    {
        _name = name;
        OriginalName = originalName;
        Count = count;
    }

    /// <summary>The name this row started life with, so a rename can be applied to servers.</summary>
    public string OriginalName { get; }

    public int Count { get; }

    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            Raise();
            Raise(nameof(CountLabel));
        }
    }

    public string CountLabel => Count switch
    {
        0 => "empty",
        1 => "1 server",
        _ => $"{Count} servers"
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class CategoryManagerWindow : Window
{
    private readonly ObservableCollection<CategoryRow> _rows = new();

    /// <summary>Final category names, in display order.</summary>
    public List<string> Result { get; private set; } = new();

    /// <summary>Old name to new name, for every row that was renamed.</summary>
    public List<(string Old, string New)> Renames { get; } = new();

    public CategoryManagerWindow(IEnumerable<string> categories, Func<string, int> countFor)
    {
        InitializeComponent();

        foreach (var name in categories)
            _rows.Add(new CategoryRow(name, name, countFor(name)));

        List.ItemsSource = _rows;
        if (_rows.Count > 0) List.SelectedIndex = 0;

        Loaded += (_, _) =>
        {
            Services.ThemeService.ApplyTitleBar(this);
            NewBox.Focus();
        };
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        var name = NewBox.Text.Trim();
        ErrorText.Text = string.Empty;

        if (name.Length == 0)
        {
            ErrorText.Text = "Type a name first.";
            return;
        }

        if (_rows.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            ErrorText.Text = $"\"{name}\" already exists.";
            return;
        }

        var row = new CategoryRow(name, name, 0);
        _rows.Add(row);
        List.SelectedItem = row;
        NewBox.Clear();
        NewBox.Focus();
    }

    private void OnNewBoxKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        // Enter in this box means "add", not "close the dialog".
        e.Handled = true;
        OnAdd(sender, e);
    }

    private void OnRename(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is not CategoryRow row) return;

        var dialog = new TextPromptWindow("Rename category", "New name", row.Name) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var name = dialog.Value.Trim();
        ErrorText.Text = string.Empty;

        if (name.Length == 0) return;

        if (_rows.Any(r => r != row && string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            ErrorText.Text = $"\"{name}\" already exists.";
            return;
        }

        row.Name = name;
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is not CategoryRow row) return;

        if (row.Count > 0)
        {
            var answer = MessageBox.Show(
                $"\"{row.Name}\" still holds {row.Count} server{(row.Count == 1 ? "" : "s")}.\n\n" +
                "Deleting the category moves them to Uncategorised. The servers themselves are kept.",
                "Delete category",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.OK) return;
        }

        var index = _rows.IndexOf(row);
        _rows.Remove(row);
        if (_rows.Count > 0) List.SelectedIndex = Math.Min(index, _rows.Count - 1);
    }

    private void OnMoveUp(object sender, RoutedEventArgs e) => Move(-1);

    private void OnMoveDown(object sender, RoutedEventArgs e) => Move(+1);

    private void Move(int delta)
    {
        if (List.SelectedItem is not CategoryRow row) return;

        var index = _rows.IndexOf(row);
        var target = index + delta;
        if (target < 0 || target >= _rows.Count) return;

        _rows.Move(index, target);
        List.SelectedItem = row;
        List.ScrollIntoView(row);
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnDone(object sender, RoutedEventArgs e)
    {
        Result = _rows.Select(r => r.Name).ToList();

        foreach (var row in _rows)
        {
            if (!string.Equals(row.OriginalName, row.Name, StringComparison.Ordinal))
                Renames.Add((row.OriginalName, row.Name));
        }

        DialogResult = true;
    }

}
