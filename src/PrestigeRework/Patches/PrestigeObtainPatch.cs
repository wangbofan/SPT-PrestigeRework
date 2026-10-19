using HarmonyLib;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Prestige;
using PrestigeRework.Services;

namespace PrestigeRework.Patches;

/// <summary>
/// Replaces the vanilla prestige flow (queue pending prestige, flag the profile as wiped, wait for a character
/// recreation) with an immediate grant of the next prestige level to the current character.
/// </summary>
public class PrestigeObtainPatch : AbstractPatch
{
    public PrestigeObtainPatch() : base("com.wangbofan.prestigerework.obtain")
    {
    }

    protected override System.Reflection.MethodBase? GetTargetMethod()
    {
        return AccessTools.Method(typeof(PrestigeController), nameof(PrestigeController.ObtainPrestige), [typeof(MongoId), typeof(ObtainPrestigeRequestList)]);
    }

    [PatchPrefix]
    public static bool Prefix(MongoId sessionId, ObtainPrestigeRequestList request, ref Task __result)
    {
        var service = PrestigeGrantService.Instance;
        if (service == null || !service.IsEnabled)
        {
            // Nothing initialized yet (or disabled): fall back to the vanilla wipe flow.
            return true;
        }

        __result = service.ObtainAsync(sessionId, request);
        return false;
    }
}
