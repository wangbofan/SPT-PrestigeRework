using System.Reflection;
using PrestigeRework.Config;
using PrestigeRework.Patches;
using PrestigeRework.Services;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace PrestigeRework;

/// <summary>
/// Runs after the database has been loaded (and before the web server starts):
/// 1. rewrites the prestige level requirements to configurable player levels,
/// 2. enables the patch that turns "obtain prestige" into a direct reward claim.
/// </summary>
[Injectable(InjectionType.Singleton, 200001)]
public class PrestigeReworkMod : IOnLoad
{
    private readonly TemplateTable _templateTable;
    private readonly PrestigeGrantService _grantService;
    private readonly ISptLogger<PrestigeReworkMod> _logger;

    private PrestigeObtainPatch? _patch;

    public PrestigeReworkMod(TemplateTable templateTable, PrestigeGrantService grantService, ISptLogger<PrestigeReworkMod> logger)
    {
        _templateTable = templateTable;
        _grantService = grantService;
        _logger = logger;
    }

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var config = PrestigeReworkConfig.Load(_logger);
        _grantService.Initialize(config);

        if (!config.Enabled)
        {
            _logger.Warning("[PrestigeRework] Disabled in config, the vanilla prestige flow (character wipe) stays active.");
            return Task.CompletedTask;
        }

        ApplyRequiredLevels(config);

        try
        {
            _patch = new PrestigeObtainPatch();
            _patch.Enable();
            _logger.Success($"[PrestigeRework] Loaded: prestige level requirements set to {string.Join(" / ", GetLevels(config))}, character wipe disabled.");
        }
        catch (Exception ex)
        {
            _logger.Error($"[PrestigeRework] Failed to enable the obtain patch, the vanilla wipe flow stays active: {ex}");
        }

        return Task.CompletedTask;
    }

    private void ApplyRequiredLevels(PrestigeReworkConfig config)
    {
        var elements = _templateTable.Prestige?.Elements;
        if (elements == null || elements.Count == 0)
        {
            _logger.Error("[PrestigeRework] Prestige template is empty, level requirements were not changed.");
            return;
        }

        for (var i = 0; i < elements.Count; i++)
        {
            var element = elements[i];
            element.Conditions ??= new List<QuestCondition>();

            var levelCondition = element.Conditions.FirstOrDefault(condition =>
                string.Equals(condition.ConditionType, "Level", StringComparison.OrdinalIgnoreCase));

            var requiredLevel = config.GetRequiredLevel(i, levelCondition?.Value is { } value ? (int)value : 0);

            if (levelCondition == null)
            {
                levelCondition = new QuestCondition
                {
                    Id = GenerateConditionId(),
                    DynamicLocale = false,
                    ConditionType = "Level",
                    CompareMethod = ">=",
                    Index = 0,
                };
                element.Conditions.Insert(0, levelCondition);
            }

            levelCondition.Value = requiredLevel;
            levelCondition.CompareMethod = ">=";
            levelCondition.Index = 0;

            if (config.KeepOnlyLevelCondition)
            {
                element.Conditions.RemoveAll(condition => !ReferenceEquals(condition, levelCondition));
            }

            if (config.DisableItemTransfer)
            {
                var filters = element.TransferConfigs?.StashConfig?.Filters;
                if (filters?.IncludedItems != null && filters.IncludedItems.Count > 0)
                {
                    filters.IncludedItems = new List<MongoId>();
                }
            }
        }
    }

    private IEnumerable<int> GetLevels(PrestigeReworkConfig config)
    {
        var elements = _templateTable.Prestige?.Elements ?? new List<PrestigeElement>();
        for (var i = 0; i < elements.Count; i++)
        {
            yield return config.GetRequiredLevel(i, 0);
        }
    }

    private static MongoId GenerateConditionId()
    {
        return new MongoId(Guid.NewGuid().ToString("N")[..24]);
    }
}
