using System.Text.Json;
using System.Text.Json.Serialization;
using SPTarkov.Common.Models.Logging;

namespace PrestigeRework.Config;

/// <summary>
/// Config for the prestige rework. Written to / read from config/config.json next to the mod dll.
/// </summary>
public class PrestigeReworkConfig
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    /// <summary>Master switch. When false nothing is changed and the vanilla prestige flow is used.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Level required to claim prestige 1, 2, 3 and 4 (index 0..3).
    /// If an entry is missing or <= 0 the value found in the prestige template is used.
    /// </summary>
    public List<int> RequiredLevels { get; set; } = [15, 25, 35, 45];

    /// <summary>
    /// true  = every prestige level only requires the level (all other requirements are hidden/removed).
    /// false = keep the vanilla requirements and only lower the level requirement.
    /// </summary>
    public bool KeepOnlyLevelCondition { get; set; } = true;

    /// <summary>
    /// The character is never reset, so transferring items is meaningless.
    /// true  = empty the transfer stash filter so no item can be selected (players just press next).
    /// false = leave the vanilla transfer filters untouched.
    /// </summary>
    public bool DisableItemTransfer { get; set; } = true;

    /// <summary>Grant the prestige achievement (same ids the vanilla flow uses).</summary>
    public bool GrantAchievements { get; set; } = true;

    /// <summary>How long reward items stay in the inbox of the player (seconds, 31536000 = 1 year).</summary>
    public long RewardMailStorageSeconds { get; set; } = 31536000;

    [JsonIgnore]
    public string FilePath { get; set; } = string.Empty;

    public int GetRequiredLevel(int index, int fallbackLevel)
    {
        if (index >= 0 && index < RequiredLevels.Count && RequiredLevels[index] > 0)
        {
            return RequiredLevels[index];
        }

        return fallbackLevel > 0 ? fallbackLevel : 1;
    }

    public static PrestigeReworkConfig Load<TLogger>(ISptLogger<TLogger> logger)
    {
        var modDir = Path.GetDirectoryName(typeof(PrestigeReworkConfig).Assembly.Location) ?? ".";
        var path = Path.Combine(modDir, "config", "config.json");
        var defaults = new PrestigeReworkConfig { FilePath = path };

        try
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(defaults, SerializerOptions));
                logger.Info($"[PrestigeRework] Created default config: {path}");
                return defaults;
            }

            var loaded = JsonSerializer.Deserialize<PrestigeReworkConfig>(File.ReadAllText(path), SerializerOptions);
            if (loaded == null)
            {
                logger.Warning($"[PrestigeRework] Config at {path} was empty, using defaults.");
                return defaults;
            }

            loaded.FilePath = path;
            return loaded;
        }
        catch (Exception ex)
        {
            logger.Error($"[PrestigeRework] Failed to read config at {path}, using defaults: {ex.Message}");
            return defaults;
        }
    }
}
