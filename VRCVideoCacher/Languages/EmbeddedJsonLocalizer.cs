using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;
using Jeek.Avalonia.Localization;

namespace VRCVideoCacher.Languages;

public class EmbeddedJsonLocalizer : BaseLocalizer
{
    private const string Prefix = "VRCVideoCacher.Languages.";
    private const string Suffix = ".loc.json";
    private const string FallbackLanguageCode = "en";

    private static readonly Serilog.ILogger Log = Program.Logger.ForContext<EmbeddedJsonLocalizer>();

    private FrozenDictionary<string, string> _languageStrings = FrozenDictionary<string, string>.Empty;

    // English stays loaded alongside the active language so that a key missing from a
    // translation renders real text instead of surfacing the raw "ru:SomeKey" marker.
    private FrozenDictionary<string, string> _fallbackStrings = FrozenDictionary<string, string>.Empty;

    public EmbeddedJsonLocalizer()
    {
        Reload();
        OnLanguageChanged();
        FireLanguageChanged();
    }

    public override void Reload()
    {
        foreach (var resourceName in GetLanguageResourceNames())
        {
            var langId = resourceName[Prefix.Length..^Suffix.Length];
            // Guard against duplicates: Get() calls Reload() whenever _hasLoaded is false,
            // so this can run more than once over the same resource list.
            if (!_languages.Contains(langId))
                _languages.Add(langId);
        }

        _fallbackStrings = LoadLanguage(FallbackLanguageCode) ?? FrozenDictionary<string, string>.Empty;

        ValidateLanguage();
        _hasLoaded = true;
        UpdateDisplayLanguages();
    }

    protected override void OnLanguageChanged()
    {
        // A config naming a language we no longer ship — hand-edited, or a translation
        // dropped between releases — used to reach .First() here and throw
        // InvalidOperationException out of App.InitializeLocalization, killing the app
        // before the window ever appeared. Fall back to English instead.
        var strings = LoadLanguage(_language);
        if (strings == null)
        {
            Log.Warning("No embedded strings for language '{Language}'; falling back to '{Fallback}'.",
                _language, FallbackLanguageCode);
            strings = _fallbackStrings;
        }

        _languageStrings = strings;
    }

    public override string Get(string key)
    {
        if (!_hasLoaded)
        {
            Reload();
        }

        if (_languageStrings.TryGetValue(key, out var value))
            return value;

        // Untranslated key: show the English text rather than "<lang>:<key>" at the user.
        if (_fallbackStrings.TryGetValue(key, out var fallback))
        {
            Log.Debug("Missing '{Language}' translation for key '{Key}'; using English.", _language, key);
            return fallback;
        }

        return key;
    }

    private static IEnumerable<string> GetLanguageResourceNames() =>
        Assembly.GetExecutingAssembly()
            .GetManifestResourceNames()
            .Where(r => r.StartsWith(Prefix, StringComparison.Ordinal) &&
                        r.EndsWith(Suffix, StringComparison.Ordinal));

    /// <summary>
    /// Loads one embedded language file, or null when there is no resource for it.
    /// </summary>
    private static FrozenDictionary<string, string>? LoadLanguage(string? languageId)
    {
        if (string.IsNullOrEmpty(languageId))
            return null;

        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream($"{Prefix}{languageId}{Suffix}");
            if (stream == null)
                return null;

            using var document = JsonDocument.Parse(stream);
            return document.RootElement
                .EnumerateObject()
                // A non-string value would be a mistake in the file; fall back to the key
                // so the UI shows something identifiable rather than throwing.
                .ToDictionary(p => p.Name, p => p.Value.GetString() ?? p.Name)
                .ToFrozenDictionary();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to parse embedded language file for '{Language}'.", languageId);
            return null;
        }
    }
}
