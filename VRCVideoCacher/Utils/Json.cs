using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace VRCVideoCacher.Utils;

/// <summary>
/// The single System.Text.Json configuration used for every file and API payload.
///
/// Three of these settings exist specifically to reproduce what Newtonsoft did, because
/// these options read and write files that already exist on every user's disk:
///
///   IncludeFields  — ConfigModel's twenty-five settings are public fields, not properties,
///                    and STJ ignores fields unless told otherwise. Without this, every
///                    setting would read back as its default: a silent config reset for
///                    everyone on upgrade.
///   Encoder        — the default encoder escapes &amp;, + and &lt; as \u00XX, which would turn
///                    every URL and every regex pattern in the rule list into something
///                    unreadable. These files go to disk and are never embedded in HTML,
///                    so relaxed escaping is the correct trade.
///   WriteIndented  — matches Newtonsoft's Formatting.Indented, so a saved file stays
///                    diffable and hand-editable.
///
/// Enums serialise as numbers in both libraries, so RuleAction values already on disk are
/// read back unchanged.
/// </summary>
public static class Json
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            IncludeFields = true,
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,

            // Tolerate a hand-edited file rather than discarding the whole config over a
            // stray comma or comment.
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,

            // Newtonsoft matched property names case-insensitively; config files and the
            // GitHub API both rely on that.
            PropertyNameCaseInsensitive = true,
        };

        // Source-generated metadata first, reflection second. The generated context covers
        // everything this application serialises, so the reflection resolver is only a
        // fallback — but keeping it means a type added later still works before somebody
        // remembers to register it.
        options.TypeInfoResolverChain.Add(AppJsonContext.Default);
        options.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());
        return options;
    }

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}
