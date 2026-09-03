using System.IO;
using System.Text;

namespace Harbor.Services;

/// <summary>
/// Append-only log at %APPDATA%\Harbor\harbor.log, next to the configuration.
///
/// Deliberately plain: no dependency, no async, and every failure swallowed. Logging must
/// never be the reason the app does not start. Writes are serialised through a lock because
/// server output arrives on background threads.
///
/// It exists because "the app opened empty" was, for a long stretch, indistinguishable from
/// "there is nothing configured". The load path therefore records the resolved config path,
/// whether the file was there, and what the process could actually see in the folder.
/// </summary>
public static class Log
{
    private const long MaxBytes = 1_000_000;

    private static readonly object Gate = new();
    private static string? _path;

    public static string FilePath => _path ??= Resolve();

    private static string Resolve()
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Harbor");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "harbor.log");
        }
        catch
        {
            return Path.Combine(Path.GetTempPath(), "harbor.log");
        }
    }

    public static void Info(string message) => Write("INFO ", message);

    public static void Warn(string message) => Write("WARN ", message);

    /// <summary>
    /// Includes the full exception, stack trace and all. A bare type and message named the
    /// symptom but not the call site, which is the one thing worth knowing after the fact.
    /// </summary>
    public static void Error(string message, Exception? ex = null)
    {
        if (ex is null)
        {
            Write("ERROR", message);
            return;
        }

        var indented = ex.ToString().Replace(Environment.NewLine, Environment.NewLine + "         ");
        Write("ERROR", $"{message} :: {indented}");
    }

    /// <summary>A blank line plus a header, so each run is easy to find in a long file.</summary>
    public static void StartSession()
    {
        try
        {
            var version = typeof(Log).Assembly.GetName().Version?.ToString() ?? "unknown";
            var sb = new StringBuilder()
                .AppendLine()
                .AppendLine("======================================================================")
                .AppendLine($"Harbor {version}   started {DateTime.Now:yyyy-MM-dd HH:mm:ss}   pid {Environment.ProcessId}")
                .AppendLine($"  exe    : {Environment.ProcessPath}")
                .AppendLine($"  base   : {AppContext.BaseDirectory}")
                .AppendLine($"  appdata: {Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}")
                .AppendLine($"  log    : {FilePath}")
                .AppendLine("======================================================================");

            Append(sb.ToString());
        }
        catch { /* never fatal */ }
    }

    private static void Write(string level, string message)
    {
        try
        {
            Append($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {level}  {message}{Environment.NewLine}");
        }
        catch { /* never fatal */ }
    }

    private static void Append(string text)
    {
        lock (Gate)
        {
            var path = FilePath;

            // Roll once at a megabyte. One generation is plenty for a desktop utility, and it
            // keeps the file small enough to open in Notepad when something goes wrong.
            try
            {
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxBytes)
                {
                    var previous = path + ".1";
                    File.Delete(previous);
                    File.Move(path, previous);
                }
            }
            catch { /* rolling is best-effort */ }

            File.AppendAllText(path, text);
        }
    }
}
