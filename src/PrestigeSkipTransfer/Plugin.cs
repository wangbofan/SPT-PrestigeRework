using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace PrestigeRework.SkipTransfer
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class PrestigeSkipTransferPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.wangbofan.prestigerework.skiptransfer";
        public const string PluginName = "PrestigeSkipTransfer";
        public const string PluginVersion = "1.1.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> SkipConfirmation;
        internal static ConfigEntry<bool> SkipItemTransfer;

        private void Awake()
        {
            Log = Logger;

            SkipConfirmation = Config.Bind(
                "General",
                "SkipConfirmation",
                true,
                "跳过转生的确认页：打开转生界面后直接发送转生请求并播放转生动画。设为 false 恢复确认页。");

            SkipItemTransfer = Config.Bind(
                "General",
                "SkipItemTransfer",
                true,
                "跳过转生的物品转移界面。SkipConfirmation 为 true 时它基本不会出现，仅在请求失败回退时起作用。");

            Log.LogInfo(PluginName + " " + PluginVersion + " loaded (SkipConfirmation = " + SkipConfirmation.Value + ", SkipItemTransfer = " + SkipItemTransfer.Value + ").");
            PrestigeScreenSkipPatch.Install();
        }
    }
}
