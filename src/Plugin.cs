using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace ModSettings
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "lachee.modsettings";
        public const string Name = "Mod Settings";
        public const string Version = "1.0.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<string> TabName;
        internal static ConfigEntry<bool> ShowAdvanced;
        internal static ConfigEntry<bool> StartExpanded;
        internal static ConfigEntry<bool> ShowBepInExConfig;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;

            TabName = Config.Bind("General", "Tab name", "Mods",
                "Text on the settings tab button. Takes effect the next time the settings window opens.");
            StartExpanded = Config.Bind("General", "Start expanded", false,
                "Show every mod's settings expanded when the tab is first opened. By default mods start collapsed so you only see their names.");
            ShowBepInExConfig = Config.Bind("General", "Show BepInEx settings", false,
                new ConfigDescription("Also list BepInEx's own settings (BepInEx.cfg). They only appear while 'Show advanced' is ticked.",
                    null, new ConfigurationManagerAttributes { IsAdvanced = true }));
            // Driven by the checkbox at the top of the tab, so it's hidden from the list itself.
            ShowAdvanced = Config.Bind("General", "Show advanced", false,
                new ConfigDescription("Show settings that mod authors marked as advanced.",
                    null, new ConfigurationManagerAttributes { Browsable = false }));

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }

    [HarmonyPatch(typeof(Settings), "Awake")]
    internal static class SettingsAwakePatch
    {
        // Runs before Settings.InitializeTabs, so the game picks our page up like any other tab.
        private static void Prefix(Settings __instance)
        {
            try
            {
                ModsTabInjector.Inject(__instance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Failed to add the mods tab to the settings window: {e}");
            }
        }
    }

    /// <summary>
    /// Same shape as the class ConfigurationManager looks for, so both that mod and this one respect it.
    /// </summary>
#pragma warning disable 0649
    internal sealed class ConfigurationManagerAttributes
    {
        public bool? Browsable;
        public bool? IsAdvanced;
        public bool? ReadOnly;
        public string Category;
        public string DispName;
        public int? Order;
    }
#pragma warning restore 0649
}
