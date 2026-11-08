using System;
using System.Globalization;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace ModConfig
{
    internal static class ValueUtil
    {
        private static readonly Type[] s_integral =
        {
            typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
            typeof(int), typeof(uint), typeof(long), typeof(ulong)
        };

        private static readonly Type[] s_fractional = { typeof(float), typeof(double), typeof(decimal) };

        public static bool IsIntegral(Type t) => s_integral.Contains(t);
        public static bool IsNumeric(Type t) => IsIntegral(t) || s_fractional.Contains(t);

        public static bool Equal(object a, object b)
        {
            if (Equals(a, b))
                return true;
            if (a == null || b == null || a.GetType() != b.GetType())
                return false;
            try
            {
                var type = a.GetType();
                if (TomlTypeConverter.CanConvert(type))
                    return TomlTypeConverter.ConvertToString(a, type) == TomlTypeConverter.ConvertToString(b, type);
            }
            catch
            {
                // Fall through: treat as different.
            }
            return false;
        }

        /// <summary>Human-readable text for showing a value (dropdown options, tooltips).</summary>
        public static string Display(EntryInfo info, object value)
        {
            if (value == null)
                return "";
            if (value is KeyboardShortcut ks)
                return KeyText(ks);
            if (value is string s)
                return s;
            if (value is float f)
                return f.ToString("0.###", CultureInfo.InvariantCulture);
            if (value is double d)
                return d.ToString("0.###", CultureInfo.InvariantCulture);
            return ToText(info, value);
        }

        /// <summary>Editable text form of a value, matching what the config file stores.</summary>
        public static string ToText(EntryInfo info, object value)
        {
            if (value == null)
                return "";
            try
            {
                if (info.ObjToStr != null)
                    return info.ObjToStr(value);
                if (value is string s)
                    return s;
                var type = info.Entry.SettingType;
                if (TomlTypeConverter.CanConvert(type))
                    return TomlTypeConverter.ConvertToString(value, type);
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"Couldn't convert {info.DisplayName} to text: {e.Message}");
            }
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        public static bool TryFromText(EntryInfo info, string text, out object value)
        {
            value = null;
            try
            {
                var type = info.Entry.SettingType;
                if (info.StrToObj != null)
                    value = info.StrToObj(text);
                else if (type == typeof(string))
                    value = text;
                else if (TomlTypeConverter.CanConvert(type))
                    value = TomlTypeConverter.ConvertToValue(text, type);
                else
                    return false;
                return value != null && type.IsInstanceOfType(value);
            }
            catch
            {
                return false;
            }
        }

        public static bool CanEditAsText(EntryInfo info)
        {
            var type = info.Entry.SettingType;
            return info.StrToObj != null || type == typeof(string) || TomlTypeConverter.CanConvert(type);
        }

        public static string KeyText(KeyboardShortcut shortcut)
        {
            if (shortcut.MainKey == KeyCode.None)
                return "Not set";
            var mods = shortcut.Modifiers.Select(KeyName).ToList();
            mods.Add(KeyName(shortcut.MainKey));
            return string.Join(" + ", mods.ToArray());
        }

        public static string KeyName(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.None: return "Not set";
                case KeyCode.LeftShift: return "L.Shift";
                case KeyCode.RightShift: return "R.Shift";
                case KeyCode.LeftControl: return "L.Ctrl";
                case KeyCode.RightControl: return "R.Ctrl";
                case KeyCode.LeftAlt: return "L.Alt";
                case KeyCode.RightAlt: return "R.Alt";
                default:
                    if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9)
                        return ((int)(key - KeyCode.Alpha0)).ToString();
                    return key.ToString();
            }
        }

        public static object Clamp(ConfigEntryBase entry, object value)
        {
            var acceptable = entry.Description?.AcceptableValues;
            if (acceptable == null || value == null)
                return value;
            try
            {
                return acceptable.Clamp(value);
            }
            catch
            {
                return value;
            }
        }

        public static bool TryParseNumber(string text, out double value)
        {
            text = (text ?? "").Trim().TrimEnd('%').Trim().Replace(',', '.');
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        public static object ToNumber(double value, Type type)
        {
            if (IsIntegral(type))
                value = Math.Round(value);
            return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
        }

        public static string FormatNumber(double value, Type type)
        {
            return IsIntegral(type)
                ? Math.Round(value).ToString(CultureInfo.InvariantCulture)
                : value.ToString("0.####", CultureInfo.InvariantCulture);
        }

        /// <summary>Rounds slider output so dragging doesn't write values like 0.43728181 to the config.</summary>
        public static double RoundForRange(double value, double range)
        {
            int decimals = range >= 100 ? 0 : range >= 10 ? 1 : range >= 1 ? 2 : 3;
            return Math.Round(value, decimals);
        }
    }
}
