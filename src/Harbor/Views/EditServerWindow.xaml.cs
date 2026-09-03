using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using Harbor.Models;

namespace Harbor.Views;

public partial class EditServerWindow : Window
{
    public ServerEntry Result { get; private set; }

    public EditServerWindow(ServerEntry entry, IEnumerable<string> categories, bool isNew)
    {
        InitializeComponent();

        GroupBox.ItemsSource = categories.ToList();

        Result = entry;
        Title = isNew ? "Add server" : "Edit server";
        HeaderText.Text = Title;

        NameBox.Text = entry.Name;
        GroupBox.Text = entry.Group;
        KindBox.SelectedIndex = entry.Kind == ServerKind.Remote ? 1 : 0;
        CommandBox.Text = entry.Command;
        DirBox.Text = entry.WorkingDirectory;
        HostBox.Text = string.IsNullOrWhiteSpace(entry.Host) ? "127.0.0.1" : entry.Host;
        PortBox.Text = entry.Port > 0 ? entry.Port.ToString() : string.Empty;
        PathBox.Text = string.IsNullOrWhiteSpace(entry.UrlPath) ? "/" : entry.UrlPath;
        NotesBox.Text = entry.Notes;
        OpenBrowserBox.IsChecked = entry.OpenBrowserOnStart;

        var env = new StringBuilder();
        foreach (var kv in entry.Environment) env.AppendLine($"{kv.Key}={kv.Value}");
        EnvBox.Text = env.ToString().TrimEnd();

        Loaded += (_, _) =>
        {
            Services.ThemeService.ApplyTitleBar(this);
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose the folder the command runs in"
        };

        if (Directory.Exists(DirBox.Text)) dialog.InitialDirectory = DirBox.Text;

        if (dialog.ShowDialog(this) == true) DirBox.Text = dialog.FolderName;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var kind = KindBox.SelectedIndex == 1 ? ServerKind.Remote : ServerKind.Local;

        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            Fail("Give the server a name.");
            return;
        }

        if (!int.TryParse(PortBox.Text.Trim(), out var port) || port is < 1 or > 65535)
        {
            Fail("Port must be a number between 1 and 65535.");
            return;
        }

        if (kind == ServerKind.Local)
        {
            if (string.IsNullOrWhiteSpace(CommandBox.Text))
            {
                Fail("A local server needs a command to run.");
                return;
            }

            var dir = DirBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(dir))
            {
                Fail("Choose the working directory the command runs in.");
                return;
            }

            if (!Directory.Exists(dir))
            {
                Fail("That working directory does not exist.");
                return;
            }
        }

        Result.Name = NameBox.Text.Trim();
        Result.Group = string.IsNullOrWhiteSpace(GroupBox.Text) ? "Uncategorised" : GroupBox.Text.Trim();
        Result.Kind = kind;
        Result.Command = CommandBox.Text.Trim();
        Result.WorkingDirectory = DirBox.Text.Trim();
        Result.Host = string.IsNullOrWhiteSpace(HostBox.Text) ? "127.0.0.1" : HostBox.Text.Trim();
        Result.Port = port;
        Result.UrlPath = string.IsNullOrWhiteSpace(PathBox.Text) ? "/" : PathBox.Text.Trim();
        Result.Notes = NotesBox.Text.Trim();
        Result.OpenBrowserOnStart = OpenBrowserBox.IsChecked == true;
        Result.Environment = ParseEnvironment(EnvBox.Text);

        DialogResult = true;
    }

    private void Fail(string message)
    {
        ErrorText.Text = message;
    }

    private static Dictionary<string, string> ParseEnvironment(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text)) return result;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var split = line.IndexOf('=');
            if (split <= 0) continue;

            var key = line[..split].Trim();
            var value = line[(split + 1)..].Trim();
            if (key.Length > 0) result[key] = value;
        }

        return result;
    }

}
