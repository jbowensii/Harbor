using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Harbor.Models;

namespace Harbor.Services;

/// <summary>
/// Reads and writes servers.json in %APPDATA%\Harbor.
/// Writes land in a temp file first, so a crash mid-save cannot leave a truncated config.
/// </summary>
public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public string Directory { get; }
    public string FilePath { get; }

    public ConfigStore()
    {
        Directory = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "Harbor");
        FilePath = Path.Combine(Directory, "servers.json");
    }

    public HarborConfig Load()
    {
        try
        {
            var exists = File.Exists(FilePath);
            Log.Info($"config=\"{FilePath}\"  exists={exists}  base=\"{AppContext.BaseDirectory}\"");

            // What the process can actually see in the config folder, which is the thing that
            // differs between a sandboxed and a normal launch.
            try
            {
                var listing = System.IO.Directory.Exists(Directory)
                    ? string.Join(", ", System.IO.Directory.GetFiles(Directory).Select(f => $"{Path.GetFileName(f)}({new FileInfo(f).Length})"))
                    : "<config dir does not exist>";
                Log.Info($"  dir contents: {listing}");
            }
            catch (Exception ex) { Log.Warn($"  dir listing failed: {ex.Message}"); }

            if (!exists)
            {
                var seeded = SeedIfPresent();
                Log.Info($"  -> seeded {seeded.Servers.Count} servers, {seeded.Categories.Count} categories");
                return seeded;
            }

            var json = File.ReadAllText(FilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                Log.Warn("  -> file was empty");
                return new HarborConfig();
            }

            Log.Info($"  read {json.Length} chars");

            var config = Parse(json);
            config.Normalise();

            // Self-heal a blank config.
            //
            // If an instance ever starts with nothing loaded and is then closed, it writes an
            // empty list back over servers.json. From then on the file exists, so the seed
            // path never runs and the app is permanently empty with no way to tell why.
            // A config with neither servers nor categories is not a state the app produces in
            // normal use - deleting the last server still leaves the categories behind - so
            // treat it as damage and restore from the seed sitting next to the exe.
            if (config.Servers.Count == 0 && config.Categories.Count == 0)
            {
                var restored = SeedIfPresent();
                if (restored.Servers.Count > 0)
                {
                    Log.Warn($"  -> config was blank; restored {restored.Servers.Count} servers from seed");
                    return restored;
                }
            }

            Log.Info($"  -> loaded {config.Servers.Count} servers, {config.Categories.Count} categories");
            return config;
        }
        catch (Exception ex)
        {
            // Any failure here used to be indistinguishable from "no servers configured".
            // Keep the unreadable file rather than silently overwriting stored configuration.
            Log.Error("  -> load failed; keeping a .broken copy", ex);

            try
            {
                var backup = FilePath + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                if (File.Exists(FilePath)) File.Copy(FilePath, backup, overwrite: true);
            }
            catch { /* nothing useful to do here */ }

            return new HarborConfig();
        }
    }

    /// <summary>
    /// Accepts either the current object form or the original bare array of servers,
    /// so an existing servers.json from an earlier build still opens.
    /// </summary>
    private static HarborConfig Parse(string json)
    {
        var trimmed = json.TrimStart();

        if (trimmed.StartsWith('['))
        {
            var servers = JsonSerializer.Deserialize<List<ServerEntry>>(json, Options) ?? new List<ServerEntry>();
            return new HarborConfig { Servers = servers };
        }

        return JsonSerializer.Deserialize<HarborConfig>(json, Options) ?? new HarborConfig();
    }

    /// <summary>First run: pick up servers.seed.json shipped next to the exe, if there is one.</summary>
    private HarborConfig SeedIfPresent()
    {
        try
        {
            var seed = Path.Combine(AppContext.BaseDirectory, "servers.seed.json");
            if (!File.Exists(seed)) return new HarborConfig();

            var config = Parse(File.ReadAllText(seed));
            config.Normalise();
            Save(config);
            return config;
        }
        catch
        {
            return new HarborConfig();
        }
    }

    public void Save(HarborConfig config)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var json = JsonSerializer.Serialize(config, Options);

        // Write to a temp file and swap it in, so an interrupted save cannot truncate the
        // real one. The swap is a Move where possible because that is atomic.
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, json);

        try
        {
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // A replacing Move can be refused where the target is projected through a
            // redirection layer - MSIX file virtualisation, or a sync client holding the
            // file. Copy-then-delete goes through a plain write instead, which those layers
            // do allow. Marginally less atomic, but the alternative is losing the save.
            Log.Warn($"atomic replace refused ({ex.GetType().Name}); falling back to copy");
            File.Copy(temp, FilePath, overwrite: true);
            try { File.Delete(temp); } catch { /* leftover temp is harmless */ }
        }

        Log.Info($"saved {config.Servers.Count} servers, {config.Categories.Count} categories to \"{FilePath}\"");
    }
}
