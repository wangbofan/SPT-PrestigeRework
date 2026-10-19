using PrestigeRework.Config;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Helpers.Commerce;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Prestige;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Commerce;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;

namespace PrestigeRework.Services;

/// <summary>
/// Grants a prestige level to the live profile instead of queueing a wipe + character recreation.
/// The reward handling intentionally mirrors SPTarkov.Server.Core.Helpers.Profile.PrestigeHelper.AddPrestigeRewardsToProfile,
/// but only for the prestige level that was just reached (the old profile keeps all earlier rewards).
/// </summary>
[Injectable(InjectionType.Singleton, 200001)]
public class PrestigeGrantService
{
    /// <summary>Set in the constructor so the Harmony prefix can reach the DI built instance.</summary>
    public static PrestigeGrantService? Instance { get; private set; }

    private readonly TemplateTable _templateTable;
    private readonly ProfileHelper _profileHelper;
    private readonly RewardHelper _rewardHelper;
    private readonly MailSendService _mailSendService;
    private readonly SaveServer _saveServer;
    private readonly TimeUtil _timeUtil;
    private readonly ISptLogger<PrestigeGrantService> _logger;

    private PrestigeReworkConfig _config = new();

    public PrestigeGrantService(
        TemplateTable templateTable,
        ProfileHelper profileHelper,
        RewardHelper rewardHelper,
        MailSendService mailSendService,
        SaveServer saveServer,
        TimeUtil timeUtil,
        ISptLogger<PrestigeGrantService> logger)
    {
        _templateTable = templateTable;
        _profileHelper = profileHelper;
        _rewardHelper = rewardHelper;
        _mailSendService = mailSendService;
        _saveServer = saveServer;
        _timeUtil = timeUtil;
        _logger = logger;
        Instance = this;
    }

    public bool IsEnabled => _config.Enabled;

    public void Initialize(PrestigeReworkConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Replaces PrestigeController.ObtainPrestige: grants the next prestige level to the current character.
    /// </summary>
    public async Task ObtainAsync(MongoId sessionId, ObtainPrestigeRequestList? request)
    {
        try
        {
            await ObtainInternalAsync(sessionId, request);
        }
        catch (Exception ex)
        {
            _logger.Error($"[PrestigeRework] Failed to grant prestige to session {sessionId}: {ex}");
        }
    }

    private async Task ObtainInternalAsync(MongoId sessionId, ObtainPrestigeRequestList? request)
    {
        var fullProfile = _profileHelper.GetFullProfile(sessionId);
        var pmc = fullProfile?.CharacterData?.PmcData;
        if (fullProfile == null || pmc == null)
        {
            _logger.Error($"[PrestigeRework] No PMC profile found for session {sessionId}, prestige aborted.");
            return;
        }

        var elements = _templateTable.Prestige?.Elements;
        if (elements == null || elements.Count == 0)
        {
            _logger.Error("[PrestigeRework] The prestige template table is empty, prestige aborted.");
            return;
        }

        pmc.Prestige ??= new Dictionary<string, long>();

        var index = -1;
        for (var i = 0; i < elements.Count; i++)
        {
            if (!pmc.Prestige.ContainsKey(elements[i].Id))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            _logger.Warning($"[PrestigeRework] Profile {fullProfile.ProfileInfo?.ProfileId} already has every prestige level, nothing to grant.");
            return;
        }

        var element = elements[index];
        var targetPrestigeLevel = index + 1;
        var currentPrestigeLevel = pmc.Info?.PrestigeLevel ?? 0;
        if (currentPrestigeLevel >= targetPrestigeLevel)
        {
            _logger.Warning($"[PrestigeRework] Profile {fullProfile.ProfileInfo?.ProfileId} is already prestige {currentPrestigeLevel}, next level ({targetPrestigeLevel}) was already reached. Nothing to grant.");
            return;
        }

        var requiredLevel = _config.GetRequiredLevel(index, GetLevelConditionValue(element));
        var playerLevel = pmc.Info?.Level ?? 0;
        if (playerLevel < requiredLevel)
        {
            _logger.Warning($"[PrestigeRework] Profile {fullProfile.ProfileInfo?.ProfileId} is level {playerLevel}, prestige {targetPrestigeLevel} requires level {requiredLevel}. Request ignored.");
            return;
        }

        var transferredItems = request?.Count ?? 0;
        var rewardItems = new List<Item>();
        var customizedRewards = 0;

        foreach (var reward in element.Rewards ?? new List<Reward>())
        {
            switch (reward.Type)
            {
                case RewardType.CustomizationDirect:
                    _profileHelper.AddHideoutCustomisationUnlock(fullProfile, reward, "prestige");
                    customizedRewards++;
                    break;

                case RewardType.Skill:
                    if (Enum.TryParse<SkillTypes>(reward.Target, true, out var skill))
                    {
                        _profileHelper.AddSkillPointsToPlayer(pmc, skill, reward.Value ?? 0.0, useSkillProgressRateMultiplier: false, adjustSkillExpForLowLevels: false);
                    }
                    else
                    {
                        _logger.Error($"[PrestigeRework] Unable to parse skill reward target to SkillTypes: {reward.Target}");
                    }

                    break;

                case RewardType.Item:
                    if (reward.Items is { Count: > 0 })
                    {
                        rewardItems.AddRange(reward.Items);
                    }
                    else if (!string.IsNullOrEmpty(reward.Target))
                    {
                        // Fallback for a reward that only carries a template id
                        rewardItems.Add(new Item { Id = new MongoId(reward.Target), Template = new MongoId(reward.Target) });
                    }
                    else
                    {
                        _logger.Warning($"[PrestigeRework] Item reward {reward.Id} has neither items nor a target, skipping.");
                    }

                    break;

                case RewardType.ExtraDailyQuest:
                    if (!string.IsNullOrEmpty(reward.Target))
                    {
                        fullProfile.AddExtraRepeatableQuest(new MongoId(reward.Target), reward.Value ?? 1);
                    }
                    else
                    {
                        _logger.Warning($"[PrestigeRework] ExtraDailyQuest reward {reward.Id} has no target, skipping.");
                    }

                    break;

                case RewardType.Stub:
                    // Placeholder reward in the live data (no target, no value) - nothing to grant.
                    break;

                default:
                    _logger.Warning($"[PrestigeRework] Unhandled prestige reward type: {reward.Type} Id: {reward.Id}");
                    break;
            }
        }

        if (_config.GrantAchievements && PrestigeHelper.PrestigeAchievements.TryGetValue(element.Id, out var achievementId))
        {
            pmc.Achievements ??= new Dictionary<MongoId, long>();
            if (!pmc.Achievements.ContainsKey(achievementId))
            {
                _rewardHelper.AddAchievementToProfile(fullProfile, achievementId);
            }
        }

        if (rewardItems.Count > 0)
        {
            _mailSendService.SendSystemMessageToPlayer(sessionId, string.Empty, rewardItems, _config.RewardMailStorageSeconds);
        }

        pmc.Prestige[element.Id] = _timeUtil.GetTimeStamp();
        if (pmc.Info != null)
        {
            pmc.Info.PrestigeLevel = targetPrestigeLevel;
        }

        // The vanilla flow queues a wipe; make sure no stale wipe request survives.
        if (fullProfile.SptData != null)
        {
            fullProfile.SptData.PendingPrestige = null;
        }

        await _saveServer.SaveProfileAsync(sessionId);

        _logger.Success(
            $"[PrestigeRework] Profile {fullProfile.ProfileInfo?.ProfileId} claimed prestige {targetPrestigeLevel} at level {playerLevel} " +
            $"(required {requiredLevel}): {customizedRewards} customisation unlock(s), {rewardItems.Count} item(s) mailed, " +
            $"{transferredItems} transferred item(s) ignored (character is not reset).");
    }

    private static int GetLevelConditionValue(PrestigeElement element)
    {
        var levelCondition = element.Conditions?.FirstOrDefault(condition =>
            string.Equals(condition.ConditionType, "Level", StringComparison.OrdinalIgnoreCase));

        return levelCondition?.Value is { } value ? (int)value : 0;
    }
}
