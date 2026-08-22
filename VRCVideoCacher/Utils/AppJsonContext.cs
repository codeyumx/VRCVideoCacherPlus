using System.Text.Json.Serialization;
using VRCVideoCacher.Models;
using VRCVideoCacher.Integrations.VRDancing;
using VRCVideoCacher.Services;

namespace VRCVideoCacher.Utils;

/// <summary>
/// Source-generated serialisation metadata for every type this application reads or writes
/// as JSON.
///
/// Generating the metadata rather than reflecting for it is what makes serialisation
/// trim-safe: the trimmer can see exactly which members are used, instead of the members
/// being reachable only through reflection it cannot follow. That was the whole reason
/// Newtonsoft needed the assembly rooted.
///
/// IncludeFields is set here as well as on the options — ConfigModel is all public fields.
/// </summary>
[JsonSourceGenerationOptions(
    IncludeFields = true,
    WriteIndented = true,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ConfigModel))]
[JsonSerializable(typeof(VersionJson))]
[JsonSerializable(typeof(GitHubRelease))]
[JsonSerializable(typeof(VvcConfig))]
[JsonSerializable(typeof(VRDSongInfo))]
[JsonSerializable(typeof(List<UriRule>))]
[JsonSerializable(typeof(List<BulkPreCache.DownloadInfo>))]
internal partial class AppJsonContext : JsonSerializerContext;
