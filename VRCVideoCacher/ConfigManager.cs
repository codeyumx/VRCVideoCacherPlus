using System.Globalization;
using Jeek.Avalonia.Localization;
using Serilog;
using VRCVideoCacher.Models;
using VRCVideoCacher.Utils;

// ReSharper disable FieldCanBeMadeReadOnly.Global

namespace VRCVideoCacher;

public class ConfigManager
{
    public static ConfigModel Config { get; private set; }
    private static readonly ILogger Log = Program.Logger.ForContext<ConfigManager>();
    private static readonly string ConfigFilePath;

    // Events for UI
    public static event Action? OnConfigChanged;

    static ConfigManager()
    {
        Log.Information("Loading config...");
        ConfigFilePath = Path.Join(Program.DataPath, "Config.json");
        Log.Debug("Using config file path: {ConfigFilePath}", ConfigFilePath);

        ConfigModel? newConfig = null;
        try
        {
            if (File.Exists(ConfigFilePath))
                newConfig = Json.Deserialize<ConfigModel>(File.ReadAllText(ConfigFilePath));
            if (newConfig != null)
                Config = newConfig;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load config, creating new one...");
        }

        if (Config == null)
        {
            Log.Information("No valid config found, creating new one...");
            Config = new ConfigModel
            {
                Language = GetSystemLanguage()
            };
            if (!LaunchArgs.HasGui)
                FirstRunConsole();
        }
        else
        {
            Log.Information("Config loaded successfully.");
        }

        if (Config.YtdlpWebServerUrl.EndsWith('/'))
            Config.YtdlpWebServerUrl = Config.YtdlpWebServerUrl.TrimEnd('/');

        // Folds in a legacy PlusConfig.json, repairs and seeds the rule list. Called with
        // the instance rather than reaching for ConfigManager.Config: PlusConfigManager has
        // no static state of its own, so there is no initialiser to re-enter here.
        PlusConfigManager.Initialize(Config);

        Log.Information("Loaded config.");
        TrySaveConfig();
    }

    public static void TrySaveConfig()
    {
        var newConfig = Json.Serialize(Config);
        var oldConfig = File.Exists(ConfigFilePath) ? File.ReadAllText(ConfigFilePath) : string.Empty;
        if (newConfig == oldConfig)
            return;

        Log.Information("Config changed, saving...");
        AtomicFile.WriteAllText(ConfigFilePath, newConfig);
        Log.Information("Config saved.");

        // Nothing else is called from here on purpose. This used to also save the Plus
        // config and call CacheManager.TryFlushCache, which closed a static-initialisation
        // loop: CacheManager's initialiser reads ConfigManager.Config, whose initialiser
        // ends by calling TrySaveConfig, which called back into CacheManager while its
        // CachePath was still null. It only ever worked because the asset dictionary was
        // empty at that moment and TryFlushCache returned early.
        //
        // CacheManager subscribes to OnConfigChanged for the flush instead, and the Plus
        // config is saved by the two view models that actually change it.
        OnConfigChanged?.Invoke();
    }

    private static bool GetUserConfirmation(string prompt, bool defaultValue)
    {
        var defaultOption = defaultValue ? "Y/n" : "y/N";
        var message = $"{prompt} ({defaultOption}):";
        message = message.TrimStart();
        Log.Information("{UserConfirmationMessage}", message);
        var input = Console.ReadLine();
        return string.IsNullOrEmpty(input) ? defaultValue : input.Equals("y", StringComparison.CurrentCultureIgnoreCase);
    }

    private static void FirstRunConsole()
    {
        Log.Information("It appears this is your first time running VRCVideoCacher. Let's create a basic config file.");

        var autoSetup = GetUserConfirmation("Would you like to use VRCVideoCacher for only fixing YouTube videos?", true);
        if (autoSetup)
        {
            Log.Information("Basic config created. You can modify it later in the Config.json file.");
        }
        else
        {
            Config.PatchResonite = GetUserConfirmation("Would you like to enable Resonite support?", false);
        }

        if (OperatingSystem.IsWindows() && GetUserConfirmation("Would you like to add VRCVideoCacher to VRCX auto start?", true))
        {
            AutoStartShortcut.CreateShortcut();
        }

        Log.Information("You'll need to install our companion extension to fetch youtube cookies (This will fix YouTube bot errors)");
        Log.Information("Chrome: https://chromewebstore.google.com/detail/vrcvideocacher-cookies-ex/kfgelknbegappcajiflgfbjbdpbpokge");
        Log.Information("Firefox: https://addons.mozilla.org/en-US/firefox/addon/vrcvideocachercookiesexporter/");
        Log.Information("More info: https://github.com/clienthax/VRCVideoCacherBrowserExtension");
        TrySaveConfig();
    }

    private static string GetSystemLanguage()
    {
        var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return Localizer.Languages.Contains(culture) ? culture : "en";
    }
}


public class ConfigModel
{
    // yt-dlp
    public string YtdlpWebServerUrl = "http://localhost:9696";
    public bool YtdlpUseCookies = true;
    // Opt-in to the new Beta browser extension, which supports app-triggered instant cookie
    // refresh. Off by default: most users run the legacy extension, which only pushes cookies
    // when they visit YouTube. Gates the Beta-only UI in the Cookies panel.
    public bool UseBetaExtension = false;
    public bool YtdlpAutoUpdate = true;
    public bool AutoUpdateVrcVideoCacher = true;
    public string YtdlpAdditionalArgs = string.Empty;
    public string YtdlpDubLanguage = string.Empty;

    // Caching
    public string CachedAssetPath = "";
    public float CacheMaxSizeInGb = 10f;
    public bool CacheHlsPlaylists = true;
    public int CacheHlsMaxLength = 30;
    public bool CacheOnly = false;
    // URLs of JSON manifests listing direct file downloads to mirror into the cache.
    public string[] PreCacheUrls = [];
    // Video URLs (YouTube, VRDancing, ...) resolved through the normal download path
    // and queued at startup if not already cached.
    public string[] PreCacheVideos = [];

    // Patching
    public bool PatchResonite = false;
    public string ResonitePath = "";
    public bool PatchVrChat = true;

    // Video Cacher
    public bool VideoPlayersEnabled = true;
    public bool CloseToTray = true;
    public bool StartMinimized = false;
    public bool StartWithSteamVr = true;
    public bool CookieSetupCompleted = false;
    public bool ErrorPopups = true;

    // Localization
    public string Language = "en";

    // UI state
    public bool HasShownTrayNotice = false;
    public bool HasShownSharedConfigNotice = false;

    /// <summary>
    /// PlusPlus-only settings, nested under one key rather than scattered across the top
    /// level. Upstream VRCVideoCacher reads this same file into its own model and writes it
    /// back, dropping whatever it does not recognise — so this whole block is what it
    /// removes, and keeping it together is what makes that loss legible and recoverable.
    /// </summary>
    public PlusConfigModel Plus = new();

    public static List<UriRule> GetDefaultRules() => DefaultRules.Create();
}
