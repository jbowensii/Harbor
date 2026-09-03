using System.Diagnostics;
using System.IO;
using Harbor.Models;

namespace Harbor.Services;

public sealed record OutputLine(DateTime Timestamp, string Text, bool IsError);

/// <summary>
/// Owns one launched server process and its child tree.
///
/// Everything is run through "cmd.exe /d /s /c" so that whatever was typed behaves the way it
/// does in a terminal: npm/npx/yarn resolve their .cmd shims, PATH lookups work, and quoting
/// stays intact. The spawned cmd.exe goes into a job object so Stop() takes the whole tree
/// down instead of orphaning node.exe or python.exe on the port.
/// </summary>
public sealed class ProcessRunner : IDisposable
{
    private readonly object _gate = new();
    private Process? _process;
    private JobObject? _job;
    private bool _stopRequested;

    public event Action<OutputLine>? OutputReceived;
    public event Action<int>? Exited;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                try { return _process is { HasExited: false }; }
                catch { return false; }
            }
        }
    }

    public int? ProcessId
    {
        get
        {
            lock (_gate)
            {
                try { return _process is { HasExited: false } p ? p.Id : null; }
                catch { return null; }
            }
        }
    }

    public void Start(ServerEntry entry)
    {
        if (IsRunning) return;

        if (string.IsNullOrWhiteSpace(entry.Command))
            throw new InvalidOperationException("This entry has no command to run.");

        var workDir = entry.WorkingDirectory;
        if (string.IsNullOrWhiteSpace(workDir))
            workDir = Directory.GetCurrentDirectory();

        if (!Directory.Exists(workDir))
            throw new DirectoryNotFoundException($"Working directory not found:\n{workDir}");

        var psi = new ProcessStartInfo
        {
            FileName = System.Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            // /d skips AutoRun scripts, /s keeps the quoting of the rest of the line intact,
            // /c runs the command and exits.
            Arguments = $"/d /s /c \"{entry.Command}\"",
            WorkingDirectory = workDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        foreach (var kv in entry.Environment)
        {
            if (!string.IsNullOrWhiteSpace(kv.Key))
                psi.Environment[kv.Key] = kv.Value ?? string.Empty;
        }

        // Nudge tools away from ANSI colour codes, which would otherwise show up as
        // escape-sequence noise in the log pane.
        psi.Environment["NO_COLOR"] = "1";
        psi.Environment["FORCE_COLOR"] = "0";

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) OutputReceived?.Invoke(new OutputLine(DateTime.Now, e.Data, false));
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) OutputReceived?.Invoke(new OutputLine(DateTime.Now, e.Data, true));
        };
        process.Exited += (_, _) =>
        {
            int code;
            try { code = process.ExitCode; } catch { code = -1; }
            if (!_stopRequested) Log.Warn($"\"{entry.Name}\" exited on its own with code {code}");
            Exited?.Invoke(_stopRequested ? 0 : code);
        };

        lock (_gate)
        {
            _stopRequested = false;
            process.Start();

            try
            {
                _job = new JobObject();
                _job.Assign(process);
            }
            catch (Exception ex)
            {
                Log.Warn($"job object unavailable, falling back to tree-kill: {ex.Message}");
                _job = null;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _process = process;
        }

        Log.Info($"start \"{entry.Name}\" pid={ProcessId} port={entry.Port} :: {entry.Command}  [in {workDir}]");
        OutputReceived?.Invoke(new OutputLine(DateTime.Now, $"> {entry.Command}   [in {workDir}]", false));
    }

    public void Stop()
    {
        Process? process;
        JobObject? job;

        lock (_gate)
        {
            _stopRequested = true;
            process = _process;
            job = _job;
            _process = null;
            _job = null;
        }

        if (process is null) return;

        // Preferred path: the job takes down cmd.exe and every descendant at once.
        try { job?.Terminate(); } catch (Exception ex) { Debug.WriteLine(ex.Message); }

        // Belt and braces for the case where the job could not be assigned.
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { /* already gone */ }
        catch (Exception ex) { Debug.WriteLine(ex.Message); }

        try { process.WaitForExit(4000); } catch { /* ignore */ }

        job?.Dispose();
        process.Dispose();

        Log.Info("stopped process tree");
        OutputReceived?.Invoke(new OutputLine(DateTime.Now, "-- stopped --", false));
    }

    public void Dispose() => Stop();
}
