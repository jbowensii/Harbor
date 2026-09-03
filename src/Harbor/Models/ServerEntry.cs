namespace Harbor.Models;

/// <summary>Local = Harbor owns the process. Remote = Harbor only probes the port.</summary>
public enum ServerKind
{
    Local,
    Remote
}

/// <summary>One stored server configuration. Serialised to servers.json.</summary>
public sealed class ServerEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    /// <summary>Free-text grouping shown as a section header, e.g. "D6" or "Homelab".</summary>
    public string Group { get; set; } = "General";

    public ServerKind Kind { get; set; } = ServerKind.Local;

    /// <summary>
    /// The full command line, exactly as typed in a shell:
    /// "npm run dev", "python scripts/dev_server.py", "dotnet run", "uvicorn app.main:app --port 8000".
    /// Run through cmd.exe so PATH, .cmd shims (npm/npx) and shell builtins all resolve.
    /// </summary>
    public string Command { get; set; } = string.Empty;

    public string WorkingDirectory { get; set; } = string.Empty;

    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; }

    /// <summary>Path appended when opening the browser, e.g. "/" or "/docs".</summary>
    public string UrlPath { get; set; } = "/";

    public bool OpenBrowserOnStart { get; set; }

    /// <summary>Extra environment variables layered over the inherited environment.</summary>
    public Dictionary<string, string> Environment { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string Notes { get; set; } = string.Empty;

    public string Url
    {
        get
        {
            var host = string.IsNullOrWhiteSpace(Host) ? "127.0.0.1" : Host.Trim();
            var path = string.IsNullOrWhiteSpace(UrlPath) ? "/" : UrlPath.Trim();
            if (!path.StartsWith('/')) path = "/" + path;
            return $"http://{host}:{Port}{path}";
        }
    }

    public ServerEntry Clone() => new()
    {
        Id = Id,
        Name = Name,
        Group = Group,
        Kind = Kind,
        Command = Command,
        WorkingDirectory = WorkingDirectory,
        Host = Host,
        Port = Port,
        UrlPath = UrlPath,
        OpenBrowserOnStart = OpenBrowserOnStart,
        Environment = new Dictionary<string, string>(Environment, StringComparer.OrdinalIgnoreCase),
        Notes = Notes
    };
}
