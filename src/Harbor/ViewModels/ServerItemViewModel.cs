using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Harbor.Models;
using Harbor.Services;

namespace Harbor.ViewModels;

/// <summary>One row in the list: the stored config, its live status, and its output log.</summary>
public sealed class ServerItemViewModel : ObservableObject, IDisposable
{
    private const int MaxLogLines = 800;

    private readonly ProcessRunner _runner = new();
    private ServerStatus _status = ServerStatus.Stopped;
    private bool _hasPortConflict;
    private bool _isSelected;
    private string _lastError = string.Empty;

    public ServerItemViewModel(ServerEntry entry)
    {
        Entry = entry;

        _runner.OutputReceived += line => Application.Current?.Dispatcher.Invoke(() => AppendLog(line));
        _runner.Exited += code => Application.Current?.Dispatcher.Invoke(() =>
        {
            if (code != 0)
            {
                Status = ServerStatus.Crashed;
                AppendLog(new OutputLine(DateTime.Now, $"-- process exited with code {code} --", true));
            }
            else
            {
                Status = ServerStatus.Stopped;
            }
            RaiseAll();
        });
    }

    public ServerEntry Entry { get; private set; }

    public ObservableCollection<OutputLine> Log { get; } = new();

    public string Name => Entry.Name;
    public string Group => string.IsNullOrWhiteSpace(Entry.Group) ? "General" : Entry.Group;
    public string Command => Entry.Command;
    public string WorkingDirectory => Entry.WorkingDirectory;
    public int Port => Entry.Port;
    public string Url => Entry.Url;
    public string Notes => Entry.Notes;
    public bool IsLocal => Entry.Kind == ServerKind.Local;
    public bool IsRemote => Entry.Kind == ServerKind.Remote;

    public string HostPort => $"{(string.IsNullOrWhiteSpace(Entry.Host) ? "127.0.0.1" : Entry.Host)}:{Entry.Port}";

    public ServerStatus Status
    {
        get => _status;
        private set
        {
            if (Set(ref _status, value)) RaiseAll();
        }
    }

    public bool HasPortConflict
    {
        get => _hasPortConflict;
        set { if (Set(ref _hasPortConflict, value)) Raise(nameof(SubtitleText)); }
    }

    /// <summary>Selection is tracked on the item because the list is a nested ItemsControl,
    /// not a single ListBox, so there is no built-in selection to lean on.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    public string LastError
    {
        get => _lastError;
        private set { if (Set(ref _lastError, value)) Raise(nameof(HasError)); }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(LastError);

    public bool CanStart => IsLocal && Status is ServerStatus.Stopped or ServerStatus.Crashed;

    public bool CanStop => IsLocal && _runner.IsRunning;

    public string StatusText => Status switch
    {
        ServerStatus.Running => "Running",
        ServerStatus.Starting => "Starting",
        ServerStatus.External => IsRemote ? "Online" : "External",
        ServerStatus.Crashed => "Crashed",
        _ => IsRemote ? "Offline" : "Stopped"
    };

    /// <summary>Drives the status dot colour through a style trigger in the XAML.</summary>
    public string StatusKey => Status switch
    {
        ServerStatus.Running => "Running",
        ServerStatus.Starting => "Starting",
        ServerStatus.External => "External",
        ServerStatus.Crashed => "Crashed",
        _ => "Stopped"
    };

    public string SubtitleText
    {
        get
        {
            var pid = _runner.ProcessId;
            var bits = new List<string>();

            if (IsRemote) bits.Add("monitor only");
            else if (pid is int id) bits.Add($"pid {id}");

            if (HasPortConflict) bits.Add("port shared with another entry");

            if (bits.Count == 0)
                return IsLocal && !string.IsNullOrWhiteSpace(Entry.Command) ? Entry.Command : HostPort;

            return string.Join("  -  ", bits);
        }
    }

    public void Start()
    {
        if (!CanStart) return;

        try
        {
            LastError = string.Empty;
            Log.Clear();
            _runner.Start(Entry);
            Status = ServerStatus.Starting;

            if (Entry.OpenBrowserOnStart)
            {
                // Give the server a moment to bind before the browser races it.
                Task.Delay(2500).ContinueWith(_ =>
                    Application.Current?.Dispatcher.Invoke(OpenInBrowser));
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Status = ServerStatus.Stopped;
            AppendLog(new OutputLine(DateTime.Now, ex.Message, true));
        }

        RaiseAll();
    }

    public void Stop()
    {
        _runner.Stop();
        Status = ServerStatus.Stopped;
        RaiseAll();
    }

    public void OpenInBrowser()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Entry.Url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    public void OpenFolder()
    {
        try
        {
            if (Directory.Exists(Entry.WorkingDirectory))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Entry.WorkingDirectory}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    /// <summary>Applies an edited copy of the entry without losing a running process.</summary>
    public void UpdateFrom(ServerEntry edited)
    {
        Entry = edited;
        RaiseAll();
    }

    /// <summary>Follows a category rename through to the stored entry.</summary>
    public void SetGroup(string group)
    {
        Entry.Group = group;
        Raise(nameof(Group));
    }

    /// <summary>Called once per poll tick with a fresh listener snapshot.</summary>
    public void RefreshStatus(bool portIsListening)
    {
        if (IsRemote)
        {
            Status = portIsListening ? ServerStatus.External : ServerStatus.Stopped;
            return;
        }

        if (_runner.IsRunning)
        {
            Status = portIsListening ? ServerStatus.Running : ServerStatus.Starting;
            return;
        }

        // Not ours. Something else is on the port, or nothing is.
        if (Status == ServerStatus.Crashed && !portIsListening) return;
        Status = portIsListening ? ServerStatus.External : ServerStatus.Stopped;
    }

    private void AppendLog(OutputLine line)
    {
        Log.Add(line);
        while (Log.Count > MaxLogLines) Log.RemoveAt(0);
    }

    private void RaiseAll()
    {
        Raise(nameof(Name));
        Raise(nameof(Group));
        Raise(nameof(Command));
        Raise(nameof(WorkingDirectory));
        Raise(nameof(Port));
        Raise(nameof(Url));
        Raise(nameof(Notes));
        Raise(nameof(IsLocal));
        Raise(nameof(IsRemote));
        Raise(nameof(HostPort));
        Raise(nameof(StatusText));
        Raise(nameof(StatusKey));
        Raise(nameof(SubtitleText));
        Raise(nameof(CanStart));
        Raise(nameof(CanStop));
    }

    public void Dispose() => _runner.Dispose();
}
