using Newtonsoft.Json;
using Serilog;

namespace VRCVideoCacher;

/// <summary>
/// Manages Plus-specific settings in a separate file so they don't get
/// overwritten when the upstream VRCVideoCacher opens the shared Config.json.
/// </summary>
public class PlusConfigManager
{
    public static PlusConfigModel Config { get; private set; }
    private static readonly ILogger Log = Program.Logger.ForContext<PlusConfigManager>();
    private static readonly string ConfigFilePath;

    public static event Action? OnConfigChanged;

    static PlusConfigManager()
    {
        ConfigFilePath = Path.Join(Program.DataPath, "PlusConfig.json");
        Log.Information("Loading Plus config from {Path}...", ConfigFilePath);

        PlusConfigModel? loaded = null;
        try
        {
            if (File.Exists(ConfigFilePath))
                loaded = JsonConvert.DeserializeObject<PlusConfigModel>(File.ReadAllText(ConfigFilePath));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load Plus config, creating new one...");
        }

        if (loaded != null)
        {
            Config = loaded;
            Log.Information("Plus config loaded successfully.");
        }
        else
        {
            Log.Information("No Plus config found, creating new one...");
            Config = new PlusConfigModel();
            MigrateFromMainConfig();
        }

        TrySaveConfig();
    }

    /// <summary>
    /// On first run, pull any existing Plus-specific values from the main Config.json
    /// so the user doesn't lose their settings.
    /// </summary>
    private static void MigrateFromMainConfig()
    {
        var configPath = Path.Join(Program.DataPath, "Config.json");
        if (!File.Exists(configPath))
            return;

        try
        {
            var json = JsonConvert.DeserializeObject<Dictionary<string, object>>(File.ReadAllText(configPath));
            if (json == null)
                return;

            if (json.TryGetValue("CacheDownloadRateLimitMBs", out var rate))
                Config.CacheDownloadRateLimitMBs = Convert.ToInt32(rate);
            if (json.TryGetValue("CacheDownloadIdleSeconds", out var idle))
                Config.CacheDownloadIdleSeconds = Convert.ToInt32(idle);
            Log.Information("Migrated Plus settings from main Config.json.");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to migrate Plus settings from main Config.json, using defaults.");
        }
    }

    public static void TrySaveConfig()
    {
        var newConfig = JsonConvert.SerializeObject(Config, Formatting.Indented);
        var oldConfig = File.Exists(ConfigFilePath) ? File.ReadAllText(ConfigFilePath) : string.Empty;
        if (newConfig == oldConfig)
            return;

        Log.Information("Plus config changed, saving...");
        File.WriteAllText(ConfigFilePath, newConfig);
        Log.Information("Plus config saved.");
        OnConfigChanged?.Invoke();
    }
}

public class PlusConfigModel
{
    public int CacheDownloadRateLimitMBs { get; set; } // 0 = unlimited
    public int CacheDownloadIdleSeconds { get; set; } = 30; // 0 = disabled
}
