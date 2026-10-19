using Range = SemanticVersioning.Range;
using SPTarkov.Server.Core.Models.Spt.Mod;
using Version = SemanticVersioning.Version;

namespace PrestigeRework;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.wangbofan.prestigerework";
    public string Name { get; init; } = "Prestige Rework";
    public string Author { get; init; } = "wangbofan";
    public List<string>? Contributors { get; init; } = null;
    public Version Version { get; init; } = new("1.0.0", false);
    public Range SptVersion { get; init; } = new("~4.1.6", false);
    public bool HasPrepatcher { get; init; } = false;
    public List<string>? Incompatibilities { get; init; } = null;
    public Dictionary<string, Range>? ModDependencies { get; init; } = null;
    public string? Url { get; init; } = "https://github.com/wangbofan/SPT-PrestigeRework";
    public string License { get; init; } = "MIT";
}
