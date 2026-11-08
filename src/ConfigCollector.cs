using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;

namespace ModConfig
{
    /// <summary>One mod (or BepInEx itself) and the settings it exposes.</summary>
    internal sealed class PluginConfigInfo
    {
        public string Guid;
        public string Name;
        public string Version;
        public ConfigFile File;
        public List<SectionInfo> Sections = new List<SectionInfo>();
    }

    internal sealed class SectionInfo
    {
        public string Name;
        public List<EntryInfo> Entries = new List<EntryInfo>();
    }

    /// <summary>A config entry plus the ConfigurationManagerAttributes hints its author attached.</summary>
    internal sealed class EntryInfo
    {
        public ConfigEntryBase Entry;
        public string DisplayName;
        public string Description;
        public string Category;
        public int Order;
        public int DeclarationIndex;
        public bool Advanced;
        public bool ReadOnly;
        public bool HideDefaultButton;
        public bool ShowRangeAsPercent;
        public object DefaultValue;
        public Func<object, string> ObjToStr;
        public Func<string, object> StrToObj;
    }

    internal static class ConfigCollector
    {
        public static List<PluginConfigInfo> Collect()
        {
            var result = new List<PluginConfigInfo>();

            foreach (var pluginInfo in Chainloader.PluginInfos.Values)
            {
                BaseUnityPlugin instance = pluginInfo.Instance;
                if (instance == null || instance.Config == null)
                    continue;
                var info = Build(pluginInfo.Metadata.GUID, pluginInfo.Metadata.Name,
                    pluginInfo.Metadata.Version?.ToString(), instance.Config, forceAdvanced: false);
                if (info != null)
                    result.Add(info);
            }

            result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            if (Plugin.ShowBepInExConfig.Value)
            {
                var core = GetCoreConfig();
                if (core != null)
                {
                    var info = Build("BepInEx", "BepInEx", typeof(Chainloader).Assembly.GetName().Version?.ToString(),
                        core, forceAdvanced: true);
                    if (info != null)
                        result.Add(info);
                }
            }

            return result;
        }

        private static ConfigFile GetCoreConfig()
        {
            try
            {
                return AccessTools.Property(typeof(ConfigFile), "CoreConfig")?.GetValue(null, null) as ConfigFile;
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"Couldn't read BepInEx core config: {e.Message}");
                return null;
            }
        }

        private static PluginConfigInfo Build(string guid, string name, string version, ConfigFile file, bool forceAdvanced)
        {
            List<ConfigEntryBase> entries;
            try
            {
                entries = file.Select(kv => kv.Value).Where(e => e != null).ToList();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Couldn't read config entries of {name}: {e.Message}");
                return null;
            }

            var infos = new List<EntryInfo>();
            for (int i = 0; i < entries.Count; i++)
            {
                var info = Describe(entries[i], i);
                if (info == null)
                    continue;
                if (forceAdvanced)
                    info.Advanced = true;
                infos.Add(info);
            }

            if (infos.Count == 0)
                return null;

            var plugin = new PluginConfigInfo { Guid = guid, Name = name, Version = version, File = file };
            foreach (var group in infos.GroupBy(e => e.Category).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                plugin.Sections.Add(new SectionInfo
                {
                    Name = group.Key,
                    // Same ordering as ConfigurationManager: explicit Order first (higher = earlier), then by name.
                    Entries = group.OrderByDescending(e => e.Order)
                        .ThenBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(e => e.DeclarationIndex)
                        .ToList()
                });
            }
            return plugin;
        }

        private static EntryInfo Describe(ConfigEntryBase entry, int index)
        {
            var info = new EntryInfo
            {
                Entry = entry,
                DisplayName = entry.Definition.Key,
                Description = entry.Description?.Description ?? "",
                Category = entry.Definition.Section,
                DeclarationIndex = index,
                DefaultValue = entry.DefaultValue,
            };

            var tags = entry.Description?.Tags;
            if (tags == null)
                return info;

            foreach (var tag in tags)
            {
                if (tag == null)
                    continue;
                var type = tag.GetType();
                if (type.Name != "ConfigurationManagerAttributes")
                {
                    // Some mods tag entries with a bare "Advanced" string.
                    if (tag is string s && s.Equals("Advanced", StringComparison.OrdinalIgnoreCase))
                        info.Advanced = true;
                    continue;
                }

                if (Read<bool?>(tag, type, "Browsable") == false)
                    return null;

                info.Advanced = Read<bool?>(tag, type, "IsAdvanced") ?? info.Advanced;
                info.ReadOnly = Read<bool?>(tag, type, "ReadOnly") ?? false;
                info.HideDefaultButton = Read<bool?>(tag, type, "HideDefaultButton") ?? false;
                info.ShowRangeAsPercent = Read<bool?>(tag, type, "ShowRangeAsPercent") ?? false;
                info.Order = Read<int?>(tag, type, "Order") ?? 0;

                var dispName = Read<string>(tag, type, "DispName");
                if (!string.IsNullOrEmpty(dispName))
                    info.DisplayName = dispName;
                var category = Read<string>(tag, type, "Category");
                if (!string.IsNullOrEmpty(category))
                    info.Category = category;
                var description = Read<string>(tag, type, "Description");
                if (!string.IsNullOrEmpty(description))
                    info.Description = description;

                var defaultValue = ReadMember(tag, type, "DefaultValue");
                if (defaultValue != null && entry.SettingType.IsInstanceOfType(defaultValue))
                    info.DefaultValue = defaultValue;

                info.ObjToStr = Read<Func<object, string>>(tag, type, "ObjToStr");
                info.StrToObj = Read<Func<string, object>>(tag, type, "StrToObj");
            }

            return info;
        }

        private static T Read<T>(object tag, Type type, string member)
        {
            var value = ReadMember(tag, type, member);
            return value is T t ? t : default;
        }

        private static object ReadMember(object tag, Type type, string member)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                var field = type.GetField(member, flags);
                if (field != null)
                    return field.GetValue(tag);
                var prop = type.GetProperty(member, flags);
                if (prop != null && prop.GetIndexParameters().Length == 0)
                    return prop.GetValue(tag, null);
            }
            catch
            {
                // Malformed attribute class; just ignore this hint.
            }
            return null;
        }
    }
}
