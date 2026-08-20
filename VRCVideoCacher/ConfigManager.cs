using System.Globalization;
using Jeek.Avalonia.Localization;
using Newtonsoft.Json;
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
                newConfig = JsonConvert.DeserializeObject<ConfigModel>(File.ReadAllText(ConfigFilePath));
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

        Log.Information("Loaded config.");
        TrySaveConfig();
    }

    public static void TrySaveConfig()
    {
        var newConfig = JsonConvert.SerializeObject(Config, Formatting.Indented);
        var oldConfig = File.Exists(ConfigFilePath) ? File.ReadAllText(ConfigFilePath) : string.Empty;
        if (newConfig == oldConfig)
            return;

        Log.Information("Config changed, saving...");
        File.WriteAllText(ConfigFilePath, newConfig);
        Log.Information("Config saved.");
        OnConfigChanged?.Invoke();
        CacheManager.TryFlushCache();
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
            Config.CacheYouTube = GetUserConfirmation("Would you like to cache/download Youtube videos?", true);
            if (Config.CacheYouTube)
            {
                var maxResolution = GetUserConfirmation("Would you like to cache/download Youtube videos in 4k?", true);
                Config.CacheYouTubeMaxResolution = maxResolution ? 2160 : 1080;
            }

            var vrDancingPyPyChoice = GetUserConfirmation("Would you like to cache/download VRDancing & PyPyDance videos?", true);
            Config.CacheVrDancing = vrDancingPyPyChoice;
            Config.CachePyPyDance = vrDancingPyPyChoice;

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
    public bool CacheYouTube = true;
    public int CacheYouTubeMaxResolution = 1080;
    public int CacheYouTubeMaxLength = 120;
    public bool CachePyPyDance = true;
    public bool CacheVrDancing = true;
    public bool CacheHlsPlaylists = true;
    public int CacheHlsMaxLength = 30;
    public bool CacheOnly = false;
    // Rules Engine
    public List<UriRule> UriRules = GetDefaultRules();
    // Cache Rules
    public string[] BlockedUrls = ["https://na2.vrdancing.club/sampleurl.mp4"];
    public string BlockRedirect = "https://www.youtube.com/watch?v=byv2bKekeWQ";
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
    public bool RedirectVRDancing = false;
    public bool ErrorPopups = true;

    // Localization
    public string Language = "en";

    // UI state
    public bool HasShownTrayNotice = false;

    public static List<UriRule> GetDefaultRules()
    {
        return
        [
            new UriRule
            {
                Name = "VRDancing EU to NA Redirect",
                Pattern = @"^https?:\/\/eu2\.vrdancing\.club\/weekend\/(.*)$",
                Action = RuleAction.Redirect,
                RedirectTarget = "https://na2.vrdancing.club/weekend/$1",
                Enabled = false
            },
            new UriRule
            {
                Name = "YouTube Music Redirect",
                Pattern = @"^https?:\/\/music\.youtube\.com\/(?:watch|playlist)?\?(?:.*?&)?v=([^&]+).*$",
                Action = RuleAction.Redirect,
                RedirectTarget = "https://youtube.com/watch?v=$1",
                Enabled = false
            },
            new UriRule
            {
                Name = "MightyGym CDN Direct",
                Pattern = @"^https?:\/\/(?:[a-zA-Z0-9-]+\.)*mightygymcdn\.nyc3\.cdn\.digitaloceanspaces\.com(?:[\/?#]|$)",
                Action = RuleAction.Direct,
                Enabled = true
            },
            new UriRule
            {
                Name = "Illumination Media Direct",
                Pattern = @"^https?:\/\/(?:[a-zA-Z0-9-]+\.)*(?:imvrcdn\.com|illumination\.media)(?:[\/?#]|$)",
                Action = RuleAction.Direct,
                Enabled = true
            },
            new UriRule
            {
                Name = "Virtual Film Institute Direct",
                Pattern = @"^https?:\/\/(?:[a-zA-Z0-9-]+\.)*virtualfilm\.institute(?:[\/?#]|$)",
                Action = RuleAction.Direct,
                Enabled = true
            },
            new UriRule
            {
                Name = "Block Rickrolls",
                Pattern = @"^https?://(?:www\.)?youtube\.com/watch\?v=(?:dQw4w9WgXcQ|jzmz6K8K4L0|XfELJU1mRMg)",
                Action = RuleAction.Block,
                Enabled = true
            },
            new UriRule
            {
                Name = "YouTube",
                Pattern = @"^https?:\/\/(?:[a-zA-Z0-9-]+\.)*(?:youtube\.com|youtu\.be|youtube-nocookie\.com)(?:[\/?#]|$)",
                Action = RuleAction.Cache,
                MaxResolution = 1080,
                MaxDurationMinutes = 120,
                Enabled = true
            },
            new UriRule
            {
                Name = "PyPyDance",
                Pattern = @"^https?:\/\/(?:[a-zA-Z0-9-]+\.)*pypydance\.com(?:[\/?#]|$)",
                Action = RuleAction.Cache,
                Enabled = true
            },
            new UriRule
            {
                Name = "VRDancing",
                Pattern = @"^https?:\/\/(?:[a-zA-Z0-9-]+\.)*vrdancing\.club(?:[\/?#]|$)",
                Action = RuleAction.Cache,
                Enabled = true
            },
            new UriRule
            {
                Name = "Everything else",
                Pattern = @".*",
                Action = RuleAction.Direct,
                Enabled = true
            }
        ];
    }
}
