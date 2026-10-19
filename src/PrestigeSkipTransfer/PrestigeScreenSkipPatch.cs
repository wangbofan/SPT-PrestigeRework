using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;

namespace PrestigeRework.SkipTransfer
{
    /// <summary>
    /// ObtainPrestigeScreen runs three states: 0 = confirmation, 1 = item transfer, 2 = obtain animation.
    ///   - "next" on the confirmation state asks for state 1 (the pointless transfer screen);
    ///   - opening the screen asks for state 0 (the confirmation page).
    /// We intercept those two transitions, send the prestige request through the original client code
    /// (PrestigeTransferItemsState.SendItemsAccept) and jump straight to the animation state.
    /// </summary>
    internal static class PrestigeScreenSkipPatch
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string ScreenTypeName = "EFT.UI.ObtainPrestigeScreen";

        private static MethodInfo _showAnotherState;
        private static FieldInfo _transferStateField;
        private static FieldInfo _currentStateField;
        private static MethodInfo _sendItemsAccept;
        private static bool _running;

        internal static void Install()
        {
            try
            {
                var screenType = AccessTools.TypeByName(ScreenTypeName);
                if (screenType == null)
                {
                    Error("type " + ScreenTypeName + " not found - is this SPT 4.1.6?");
                    return;
                }

                _showAnotherState = screenType.GetMethod("ShowAnotherState", Instance, null, new[] { typeof(int) }, null);
                _transferStateField = screenType.GetField("_transferItemsState", Instance);
                _currentStateField = screenType.GetField("_currentSelectedStateIndex", Instance);
                if (_showAnotherState == null || _transferStateField == null || _currentStateField == null)
                {
                    Error("could not resolve ObtainPrestigeScreen.ShowAnotherState / _transferItemsState / _currentSelectedStateIndex");
                    return;
                }

                _sendItemsAccept = _transferStateField.FieldType.GetMethod("SendItemsAccept", Instance, null, Type.EmptyTypes, null);
                if (_sendItemsAccept == null)
                {
                    Error("could not resolve PrestigeTransferItemsState.SendItemsAccept");
                    return;
                }

                var prefix = typeof(PrestigeScreenSkipPatch).GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic);
                var harmony = new Harmony(PrestigeSkipTransferPlugin.PluginGuid);
                harmony.Patch(_showAnotherState, prefix: new HarmonyMethod(prefix));

                Info("patched " + ScreenTypeName + ".ShowAnotherState -> confirmation / item transfer states are skipped.");
            }
            catch (Exception e)
            {
                Error("patch failed: " + e);
            }
        }

        private static bool Prefix(object __instance, int stateIndex, ref Task __result)
        {
            if (__instance == null || _running)
            {
                return true;
            }

            bool skipConfirmation = PrestigeSkipTransferPlugin.SkipConfirmation?.Value ?? false;
            bool skipTransfer = PrestigeSkipTransferPlugin.SkipItemTransfer?.Value ?? false;
            if (!skipConfirmation && !skipTransfer)
            {
                return true;
            }

            int current;
            try
            {
                current = (int)_currentStateField.GetValue(__instance);
            }
            catch (Exception e)
            {
                Error("could not read _currentSelectedStateIndex: " + e);
                return true;
            }

            // the screen was just opened: state 0 with no state selected yet
            if (skipConfirmation && stateIndex == 0 && current < 0)
            {
                Info("skipping the confirmation screen for " + __instance.GetType().Name + ".");
                _running = true;
                __result = RunAsync(__instance, 0);
                return false;
            }

            // "next" on the confirmation state: the original code would show the transfer screen
            if (skipTransfer && stateIndex == 1 && current == 0)
            {
                Info("skipping the item transfer screen for " + __instance.GetType().Name + ".");
                _running = true;
                __result = RunAsync(__instance, 1);
                return false;
            }

            return true;
        }

        private static async Task RunAsync(object screen, int fallbackState)
        {
            try
            {
                object transfer;
                try
                {
                    transfer = _transferStateField.GetValue(screen);
                }
                catch (Exception e)
                {
                    Error("could not read _transferItemsState: " + e);
                    await ShowAsync(screen, fallbackState);
                    return;
                }

                if (transfer == null)
                {
                    Error("_transferItemsState is null, falling back to the vanilla screen");
                    await ShowAsync(screen, fallbackState);
                    return;
                }

                bool accepted;
                try
                {
                    var task = _sendItemsAccept.Invoke(transfer, null) as Task<bool>;
                    if (task == null)
                    {
                        throw new InvalidOperationException("SendItemsAccept did not return Task<bool>");
                    }

                    accepted = await task;
                }
                catch (Exception e)
                {
                    Error("prestige request failed: " + e);
                    await ShowAsync(screen, fallbackState);
                    return;
                }

                if (!accepted)
                {
                    // the server (or the session) rejected it; let the player see the normal screen
                    Warning("the server did not accept the prestige request, showing the vanilla screen");
                    await ShowAsync(screen, fallbackState);
                    return;
                }

                await ShowAsync(screen, 2);
            }
            catch (Exception e)
            {
                Error("unexpected error: " + e);
            }
            finally
            {
                _running = false;
            }
        }

        private static Task ShowAsync(object screen, int stateIndex)
        {
            try
            {
                var task = _showAnotherState.Invoke(screen, new object[] { stateIndex }) as Task;
                return task ?? Task.CompletedTask;
            }
            catch (Exception e)
            {
                Error("ShowAnotherState(" + stateIndex + ") failed: " + e);
                return Task.CompletedTask;
            }
        }

        private static void Info(string message) => PrestigeSkipTransferPlugin.Log?.LogInfo(message);
        private static void Warning(string message) => PrestigeSkipTransferPlugin.Log?.LogWarning(message);
        private static void Error(string message) => PrestigeSkipTransferPlugin.Log?.LogError(message);
    }
}
