namespace Harbor.Models;

public enum ServerStatus
{
    /// <summary>Nothing listening, no process owned.</summary>
    Stopped,

    /// <summary>Harbor launched it and the process is alive.</summary>
    Running,

    /// <summary>Harbor launched it, the process is alive, but the port is not listening yet.</summary>
    Starting,

    /// <summary>The port is listening but Harbor did not start it (started by hand, or another app).</summary>
    External,

    /// <summary>The owned process exited on its own with a non-zero code.</summary>
    Crashed
}
