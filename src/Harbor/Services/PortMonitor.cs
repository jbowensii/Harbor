using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Harbor.Services;

/// <summary>
/// Answers "is anything listening on this port?".
///
/// Local ports are read in one shot from the TCP table - far cheaper than opening a socket
/// per entry, and it sees listeners bound to 127.0.0.1 as well as 0.0.0.0. Remote hosts have
/// no table to read, so those fall back to a short connect probe.
/// </summary>
public sealed class PortMonitor
{
    private HashSet<int> _localListeners = new();
    private readonly Dictionary<string, bool> _remoteResults = new();
    private readonly object _gate = new();

    /// <summary>Re-reads the local TCP listener table. Call once per poll, not once per entry.</summary>
    public void RefreshLocal()
    {
        try
        {
            var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
            var set = new HashSet<int>();
            foreach (var ep in listeners) set.Add(ep.Port);
            lock (_gate) _localListeners = set;
        }
        catch (NetworkInformationException)
        {
            // Transient; keep the previous snapshot rather than reporting everything as down.
        }
    }

    public bool IsLocalPortListening(int port)
    {
        if (port <= 0) return false;
        lock (_gate) return _localListeners.Contains(port);
    }

    public static bool IsLocalHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return true;
        host = host.Trim();
        if (host is "localhost" or "127.0.0.1" or "::1" or "0.0.0.0") return true;
        return IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip);
    }

    /// <summary>Connect probe for hosts we cannot read a TCP table for.</summary>
    public async Task<bool> ProbeRemoteAsync(string host, int port, int timeoutMs = 1200)
    {
        if (port <= 0) return false;
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(timeoutMs);
            await client.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    public void CacheRemote(string key, bool up)
    {
        lock (_gate) _remoteResults[key] = up;
    }

    public bool GetCachedRemote(string key)
    {
        lock (_gate) return _remoteResults.TryGetValue(key, out var v) && v;
    }
}
