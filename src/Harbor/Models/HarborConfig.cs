namespace Harbor.Models;

/// <summary>Follow Windows, or pin one way. System is the first-run default.</summary>
public enum AppTheme
{
    System,
    Light,
    Dark
}

/// <summary>
/// The whole of servers.json.
///
/// Categories are stored explicitly rather than derived from the servers, so an empty
/// category survives a restart and the display order is the order in this list.
/// </summary>
public sealed class HarborConfig
{
    public AppTheme Theme { get; set; } = AppTheme.System;

    public List<string> Categories { get; set; } = new();

    public List<ServerEntry> Servers { get; set; } = new();

    /// <summary>Categories that exist plus any referenced by a server, in stored order.</summary>
    public void Normalise()
    {
        Categories = Categories
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var server in Servers)
        {
            if (string.IsNullOrWhiteSpace(server.Group)) server.Group = "Uncategorised";

            if (!Categories.Contains(server.Group, StringComparer.OrdinalIgnoreCase))
                Categories.Add(server.Group);
        }
    }
}
