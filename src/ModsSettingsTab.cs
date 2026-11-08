using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Valheim.SettingsGui;

namespace ModConfig
{
    /// <summary>
    /// The "Mods" settings page. Lists every plugin's config entries in one scrolling list.
    /// Edits are staged and only written when the player presses OK, just like the vanilla tabs;
    /// Back discards them.
    /// </summary>
    public class ModsSettingsTab : MonoBehaviour, ISettingsTab
    {
        // Layout (page is 920 x 550 in the vanilla settings panel).
        private const float TopBarHeight = 38f;
        private const float TopBarPadding = 8f;
        private const float ScrollbarWidth = 20f;
        private const float RowHeight = 34f;
        private const float LabelX = 24f;
        private const float ControlX = 430f;
        private const float ControlWidth = 340f;
        private const float ResetWidth = 84f;

        private static readonly Color ModifiedColor = new Color(1f, 0.72f, 0.3f, 1f);
        private static readonly Dictionary<string, bool> s_collapsed = new Dictionary<string, bool>();

        private static readonly AccessTools.FieldRef<Settings, bool> s_navigationBlocked =
            AccessTools.FieldRefAccess<Settings, bool>("m_navigationBlocked");

        private static readonly KeyCode[] s_modifierKeys =
        {
            KeyCode.LeftShift, KeyCode.RightShift, KeyCode.LeftControl, KeyCode.RightControl,
            KeyCode.LeftAlt, KeyCode.RightAlt, KeyCode.LeftCommand, KeyCode.RightCommand,
            KeyCode.LeftWindows, KeyCode.RightWindows, KeyCode.AltGr,
        };
        private static KeyCode[] s_captureKeys;

        public event Action<string, int> SharedSettingChanged;

        private Settings _settings;
        private TabHandler _tabHandler;
        private UiKit _kit;
        private bool _built;

        private RectTransform _content;
        private ScrollRect _scroll;
        private ScrollRectEnsureVisible _ensureVisible;
        private GameObject _tooltipPopup;
        private TMP_InputField _search;
        private Toggle _advancedToggle;
        private TMP_Text _emptyText;
        private GameObject _lastSelected;

        private readonly List<GroupView> _groups = new List<GroupView>();
        private readonly List<EntryRow> _rows = new List<EntryRow>();

        // Navigation blocking while typing / capturing a key.
        private bool _blocking;
        private int _lastBlockFrame = -10;
        private bool _savedKeyboardInput, _savedTabKeyInput, _savedGamepadInput, _savedNavBlocked;

        // Key capture state.
        private EntryRow _captureRow;
        private TMP_Text _captureLabel;
        private Button _captureButton;
        private int _captureFrame;

        internal void Bind(Settings settings, TabHandler tabHandler)
        {
            _settings = settings;
            _tabHandler = tabHandler;
        }

        // ------------------------------------------------------------------ ISettingsTab

        public void Initialize()
        {
            // The page is built lazily the first time it's opened, so opening Settings stays fast.
        }

        public void Terminate()
        {
            SetBlocking(false);
        }

        public void OnTabOpen(Button backButton, Button okButton)
        {
            if (!_built)
            {
                _built = true;
                try
                {
                    Build();
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Failed to build the mods settings page: {e}");
                }
            }

            var first = _rows.FirstOrDefault(r => r.Root.activeInHierarchy && r.MainSelectable != null)?.MainSelectable;
            if (first != null)
            {
                GuiUtils.SetNavigationUp(backButton, first);
                GuiUtils.SetNavigationUp(okButton, first);
            }
        }

        public void OnOkAsync(OkActionCompletedHandler okActionCompletedCallback)
        {
            try
            {
                ApplyChanges();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Failed to save mod settings: {e}");
            }
            okActionCompletedCallback?.Invoke();
        }

        public void OnBack()
        {
            // Nothing was written yet; discarding the staged values is all Back needs to do.
            EndKeyCapture();
        }

        public void OnSharedSettingChanged(string setting, int value)
        {
        }

        // ------------------------------------------------------------------ saving

        private void ApplyChanges()
        {
            EndKeyCapture();
            var touched = new HashSet<ConfigFile>();
            int changed = 0;
            foreach (var row in _rows)
            {
                if (!row.Dirty)
                    continue;
                try
                {
                    row.Info.Entry.BoxedValue = row.Pending;
                    touched.Add(row.Info.Entry.ConfigFile);
                    changed++;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Couldn't set {row.Plugin.Name} / {row.Info.DisplayName}: {e.Message}");
                }
            }

            // Files with SaveOnConfigSet enabled already saved themselves on assignment.
            foreach (var file in touched.Where(f => !f.SaveOnConfigSet))
            {
                try
                {
                    file.Save();
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Couldn't save {file.ConfigFilePath}: {e.Message}");
                }
            }

            if (changed > 0)
                Plugin.Log.LogInfo($"Saved {changed} mod setting(s).");
        }

        // ------------------------------------------------------------------ building

        private void Build()
        {
            _kit = UiKit.FromSettings(_settings.transform);
            if (!_kit.IsValid)
            {
                Plugin.Log.LogWarning("Couldn't find the vanilla settings controls to copy; the mods page will be empty.");
                return;
            }

            var page = (RectTransform)transform;
            BuildTopBar(page);
            BuildScrollView(page);
            _tooltipPopup = _kit.CreateTooltipPopup(page);
            if (_tooltipPopup != null)
                _tooltipPopup.transform.SetAsLastSibling();

            foreach (var plugin in ConfigCollector.Collect())
                BuildGroup(plugin);

            _emptyText = _kit.CreateLabel(_content, "");
            _emptyText.alignment = TextAlignmentOptions.Center;
            _emptyText.gameObject.AddComponent<LayoutElement>().preferredHeight = 120;

            ApplyFilter();
            _scroll.verticalNormalizedPosition = 1f;
        }

        private void BuildTopBar(RectTransform page)
        {
            var bar = UiKit.CreateRect("TopBar", page);
            bar.anchorMin = new Vector2(0, 1);
            bar.anchorMax = new Vector2(1, 1);
            bar.pivot = new Vector2(0.5f, 1);
            bar.offsetMin = new Vector2(TopBarPadding, -(TopBarPadding + TopBarHeight));
            bar.offsetMax = new Vector2(-TopBarPadding, -TopBarPadding);

            _search = _kit.CreateInputField(bar, "Search mods and settings...");
            UiKit.PlaceLeft((RectTransform)_search.transform, 0, 300, 34);
            _search.onValueChanged.AddListener(_ => ApplyFilter());

            _advancedToggle = _kit.CreateToggle(bar);
            UiKit.PlaceLeft((RectTransform)_advancedToggle.transform, 324, 20, 20);
            _advancedToggle.isOn = Plugin.ShowAdvanced.Value;
            _advancedToggle.onValueChanged.AddListener(on =>
            {
                Plugin.ShowAdvanced.Value = on;
                ApplyFilter();
            });
            var advLabel = _kit.CreateLabel(bar, "Show advanced");
            UiKit.PlaceLeft((RectTransform)advLabel.transform, 354, 160, 34);

            var collapse = _kit.CreateButton(bar, "Collapse all", out _);
            UiKit.PlaceRight((RectTransform)collapse.transform, 0, 150, 36);
            collapse.onClick.AddListener(() => SetAllCollapsed(true));

            var expand = _kit.CreateButton(bar, "Expand all", out _);
            UiKit.PlaceRight((RectTransform)expand.transform, 160, 150, 36);
            expand.onClick.AddListener(() => SetAllCollapsed(false));
        }

        private void BuildScrollView(RectTransform page)
        {
            var area = UiKit.CreateRect("ModList", page);
            area.gameObject.SetActive(false);
            UiKit.Stretch(area, 0, 0, TopBarPadding * 2 + TopBarHeight + 4, 0);

            var viewport = UiKit.CreateRect("Viewport", area);
            UiKit.Stretch(viewport, 0, ScrollbarWidth, 0, 0);
            viewport.gameObject.AddComponent<RectMask2D>();
            // An invisible graphic so the mouse wheel works over empty space too.
            var hit = viewport.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);

            _content = UiKit.CreateRect("Content", viewport);
            _content.anchorMin = new Vector2(0, 1);
            _content.anchorMax = new Vector2(1, 1);
            _content.pivot = new Vector2(0.5f, 1);
            _content.offsetMin = Vector2.zero;
            _content.offsetMax = Vector2.zero;
            var layout = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 2;
            layout.padding = new RectOffset(8, 8, 8, 12);
            _content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _scroll = area.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = viewport;
            _scroll.content = _content;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = _kit.ScrollSensitivity;
            _scroll.inertia = true;

            if (_kit.ScrollbarTemplate)
            {
                var sbGo = Instantiate(_kit.ScrollbarTemplate, area, false);
                sbGo.name = "Scrollbar";
                sbGo.SetActive(true);
                var tutorial = sbGo.transform.Find("Tutorial");
                if (tutorial != null)
                    DestroyImmediate(tutorial.gameObject);
                var sbRt = (RectTransform)sbGo.transform;
                sbRt.anchorMin = new Vector2(1, 0);
                sbRt.anchorMax = new Vector2(1, 1);
                sbRt.pivot = new Vector2(1, 1);
                sbRt.anchoredPosition = Vector2.zero;
                sbRt.sizeDelta = new Vector2(Mathf.Max(sbRt.sizeDelta.x, 11f), 0);
                var scrollbar = sbGo.GetComponent<Scrollbar>();
                scrollbar.onValueChanged = new Scrollbar.ScrollEvent();
                scrollbar.direction = Scrollbar.Direction.BottomToTop;
                _scroll.verticalScrollbar = scrollbar;
                _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            }

            _ensureVisible = area.gameObject.AddComponent<ScrollRectEnsureVisible>();
            AccessTools.Field(typeof(ScrollRectEnsureVisible), "maskTransform")?.SetValue(_ensureVisible, viewport);

            area.gameObject.SetActive(true);
        }

        private void BuildGroup(PluginConfigInfo plugin)
        {
            var group = new GroupView { Plugin = plugin, SearchName = plugin.Name.ToLowerInvariant() };
            if (!s_collapsed.ContainsKey(plugin.Guid))
                s_collapsed[plugin.Guid] = !Plugin.StartExpanded.Value;

            // Header: a full-width game button that collapses / expands the mod.
            var header = UiKit.CreateRect("Mod: " + plugin.Name, _content);
            header.gameObject.AddComponent<LayoutElement>().preferredHeight = 42;
            var button = _kit.CreateButton(header, plugin.Name, out var label);
            UiKit.Stretch((RectTransform)button.transform, 0, 0, 3, 3);
            if (label != null)
            {
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.margin = new Vector4(16, 0, 16, 0);
                label.enableAutoSizing = false;
                label.fontSize = _kit.LabelTemplate.fontSize * 1.1f;
                label.richText = true;
            }
            button.onClick.AddListener(() =>
            {
                s_collapsed[plugin.Guid] = !s_collapsed[plugin.Guid];
                ApplyFilter();
            });
            group.Header = header.gameObject;
            group.HeaderLabel = label;
            group.HeaderButton = button;

            foreach (var section in plugin.Sections)
            {
                var sectionView = new SectionView();
                var secRt = UiKit.CreateRect("Section: " + section.Name, _content);
                secRt.gameObject.AddComponent<LayoutElement>().preferredHeight = 30;
                var heading = _kit.CreateHeading(secRt, section.Name, _kit.LabelTemplate.fontSize * 1.05f);
                UiKit.Stretch((RectTransform)heading.transform, LabelX - 8, 0, 0, 2);
                var line = _kit.CreateSeparator(secRt);
                var lineRt = (RectTransform)line.transform;
                lineRt.anchorMin = new Vector2(0, 0);
                lineRt.anchorMax = new Vector2(1, 0);
                lineRt.pivot = new Vector2(0.5f, 0);
                lineRt.offsetMin = new Vector2(LabelX - 8, 0);
                lineRt.offsetMax = new Vector2(-8, 2);
                sectionView.Root = secRt.gameObject;

                foreach (var entry in section.Entries)
                {
                    var row = BuildRow(plugin, section, entry);
                    if (row != null)
                        sectionView.Rows.Add(row);
                }
                group.Sections.Add(sectionView);
            }

            var spacer = UiKit.CreateRect("Spacer", _content);
            spacer.gameObject.AddComponent<LayoutElement>().preferredHeight = 10;
            group.Spacer = spacer.gameObject;

            _groups.Add(group);
        }

        private EntryRow BuildRow(PluginConfigInfo plugin, SectionInfo section, EntryInfo info)
        {
            var root = UiKit.CreateRect(info.DisplayName, _content);
            root.gameObject.AddComponent<LayoutElement>().preferredHeight = RowHeight;

            var row = new EntryRow
            {
                Info = info,
                Plugin = plugin,
                Root = root.gameObject,
                Pending = info.Entry.BoxedValue,
                SearchText = $"{plugin.Name} {section.Name} {info.DisplayName} {info.Entry.Definition.Key} {info.Description}".ToLowerInvariant(),
            };

            row.Label = _kit.CreateLabel(root, info.DisplayName);
            UiKit.PlaceLeft((RectTransform)row.Label.transform, LabelX, ControlX - LabelX - 12, RowHeight);
            row.LabelColor = row.Label.color;
            row.Label.raycastTarget = true; // so hovering the name shows the tooltip

            var control = UiKit.CreateRect("Control", root);
            UiKit.PlaceLeft(control, ControlX, ControlWidth, RowHeight);

            try
            {
                BuildEditor(row, control);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Couldn't build an editor for {plugin.Name} / {info.DisplayName}: {e.Message}");
                foreach (Transform child in control)
                    Destroy(child.gameObject);
                BuildReadOnly(row, control);
            }

            if (!info.HideDefaultButton && !info.ReadOnly && row.Editable)
            {
                row.Reset = _kit.CreateButton(root, "Reset", out _);
                UiKit.PlaceRight((RectTransform)row.Reset.transform, 8, ResetWidth, 30);
                row.Reset.onClick.AddListener(() => row.SetPending(info.DefaultValue, true));
            }

            if (info.ReadOnly)
            {
                foreach (var selectable in control.GetComponentsInChildren<Selectable>(true))
                    selectable.interactable = false;
            }

            _kit.AttachTooltip(root.gameObject, _tooltipPopup, row.MainSelectable, (RectTransform)row.Label.transform,
                info.DisplayName, BuildTooltipText(info));

            row.RefreshControl?.Invoke();
            row.UpdateState();
            _rows.Add(row);
            return row;
        }

        private static string BuildTooltipText(EntryInfo info)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(info.Description))
                parts.Add(info.Description.Trim());

            var acceptable = info.Entry.Description?.AcceptableValues;
            if (acceptable != null)
            {
                var type = acceptable.GetType();
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(AcceptableValueRange<>))
                {
                    var min = type.GetProperty("MinValue")?.GetValue(acceptable, null);
                    var max = type.GetProperty("MaxValue")?.GetValue(acceptable, null);
                    parts.Add($"Range: {ValueUtil.Display(info, min)} to {ValueUtil.Display(info, max)}");
                }
            }

            if (info.DefaultValue != null)
                parts.Add($"Default: {ValueUtil.Display(info, info.DefaultValue)}");
            if (info.Advanced)
                parts.Add("(Advanced setting)");
            return string.Join("\n", parts.ToArray());
        }

        // ------------------------------------------------------------------ editors

        private void BuildEditor(EntryRow row, RectTransform area)
        {
            var info = row.Info;
            var type = info.Entry.SettingType;
            var acceptable = info.Entry.Description?.AcceptableValues;
            var acceptableType = acceptable?.GetType();
            bool isList = acceptableType != null && acceptableType.IsGenericType &&
                          acceptableType.GetGenericTypeDefinition() == typeof(AcceptableValueList<>);
            bool isRange = acceptableType != null && acceptableType.IsGenericType &&
                           acceptableType.GetGenericTypeDefinition() == typeof(AcceptableValueRange<>);

            if (type == typeof(bool))
                BuildToggle(row, area);
            else if (type == typeof(KeyboardShortcut) || type == typeof(KeyCode))
                BuildKeyBinding(row, area);
            else if (isList && _kit.DropdownTemplate)
                BuildDropdown(row, area, ((Array)acceptableType.GetProperty("AcceptableValues").GetValue(acceptable, null)).Cast<object>().ToList());
            else if (type.IsEnum && !type.IsDefined(typeof(FlagsAttribute), false) && _kit.DropdownTemplate)
                BuildDropdown(row, area, Enum.GetValues(type).Cast<object>().ToList());
            else if (isRange && ValueUtil.IsNumeric(type) && _kit.SliderTemplate)
                BuildSlider(row, area,
                    Convert.ToDouble(acceptableType.GetProperty("MinValue").GetValue(acceptable, null)),
                    Convert.ToDouble(acceptableType.GetProperty("MaxValue").GetValue(acceptable, null)));
            else if (type == typeof(Color))
                BuildColor(row, area);
            else if (ValueUtil.CanEditAsText(info))
                BuildText(row, area);
            else
                BuildReadOnly(row, area);
        }

        private void BuildToggle(EntryRow row, RectTransform area)
        {
            var toggle = _kit.CreateToggle(area);
            UiKit.PlaceLeft((RectTransform)toggle.transform, 0, 20, 20);
            toggle.onValueChanged.AddListener(on => row.SetPending(on, false));
            row.RefreshControl = () => toggle.SetIsOnWithoutNotify(row.Pending is bool b && b);
            row.MainSelectable = toggle;
            row.Editable = true;
        }

        private void BuildDropdown(EntryRow row, RectTransform area, List<object> values)
        {
            var dropdown = _kit.CreateDropdown(area);
            UiKit.PlaceLeft((RectTransform)dropdown.transform, 0, ControlWidth, 32);
            dropdown.AddOptions(values.Select(v => ValueUtil.Display(row.Info, v)).ToList());
            dropdown.onValueChanged.AddListener(i =>
            {
                if (i >= 0 && i < values.Count)
                    row.SetPending(values[i], false);
            });
            row.RefreshControl = () =>
            {
                int index = values.FindIndex(v => ValueUtil.Equal(v, row.Pending));
                if (index < 0)
                {
                    // Current value isn't one of the options (e.g. an undefined enum value); show it anyway.
                    values.Add(row.Pending);
                    dropdown.AddOptions(new List<string> { ValueUtil.Display(row.Info, row.Pending) });
                    index = values.Count - 1;
                }
                dropdown.SetValueWithoutNotify(index);
                dropdown.RefreshShownValue();
            };
            row.MainSelectable = dropdown;
            row.Editable = true;
        }

        private void BuildSlider(EntryRow row, RectTransform area, double min, double max)
        {
            var type = row.Info.Entry.SettingType;
            bool percent = row.Info.ShowRangeAsPercent && max > min;
            var slider = _kit.CreateSlider(area);
            UiKit.PlaceLeft((RectTransform)slider.transform, 0, 228, 20);
            slider.wholeNumbers = ValueUtil.IsIntegral(type);
            slider.minValue = (float)min;
            slider.maxValue = (float)max;

            var input = _kit.CreateInputField(area, "");
            UiKit.PlaceLeft((RectTransform)input.transform, 240, ControlWidth - 240, 30);
            input.contentType = TMP_InputField.ContentType.Standard;
            input.characterValidation = TMP_InputField.CharacterValidation.None;

            string Format(object v)
            {
                double d = Convert.ToDouble(v);
                return percent
                    ? Math.Round((d - min) / (max - min) * 100.0).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%"
                    : ValueUtil.FormatNumber(d, type);
            }

            slider.onValueChanged.AddListener(v =>
            {
                double d = slider.wholeNumbers ? Math.Round(v) : ValueUtil.RoundForRange(v, max - min);
                row.SetPending(ValueUtil.ToNumber(d, type), false);
                input.SetTextWithoutNotify(Format(row.Pending));
            });
            input.onEndEdit.AddListener(text =>
            {
                if (ValueUtil.TryParseNumber(text, out double d))
                {
                    if (percent)
                        d = min + d / 100.0 * (max - min);
                    row.SetPending(ValueUtil.ToNumber(d, type), true);
                }
                else
                {
                    row.RefreshControl();
                }
            });
            row.RefreshControl = () =>
            {
                slider.SetValueWithoutNotify((float)Convert.ToDouble(row.Pending));
                input.SetTextWithoutNotify(Format(row.Pending));
            };
            row.MainSelectable = slider;
            row.Editable = true;
        }

        private void BuildText(EntryRow row, RectTransform area)
        {
            var info = row.Info;
            var input = _kit.CreateInputField(area, info.Entry.SettingType.Name);
            UiKit.PlaceLeft((RectTransform)input.transform, 0, ControlWidth, 30);
            input.onEndEdit.AddListener(text =>
            {
                if (ValueUtil.TryFromText(info, text, out var value))
                    row.SetPending(value, true);
                else
                    row.RefreshControl();
            });
            row.RefreshControl = () => input.SetTextWithoutNotify(ValueUtil.ToText(info, row.Pending));
            row.MainSelectable = input;
            row.Editable = true;
        }

        private void BuildColor(EntryRow row, RectTransform area)
        {
            var input = _kit.CreateInputField(area, "#RRGGBBAA");
            UiKit.PlaceLeft((RectTransform)input.transform, 0, ControlWidth - 44, 30);

            var swatchFrame = UiKit.CreateRect("Swatch", area);
            UiKit.PlaceLeft(swatchFrame, ControlWidth - 34, 34, 30);
            var frameImg = swatchFrame.gameObject.AddComponent<Image>();
            if (_kit.ValueBoxTemplate)
            {
                frameImg.sprite = _kit.ValueBoxTemplate.sprite;
                frameImg.type = _kit.ValueBoxTemplate.type;
                frameImg.color = _kit.ValueBoxTemplate.color;
            }
            frameImg.raycastTarget = false;
            var swatchRt = UiKit.CreateRect("Color", swatchFrame);
            UiKit.Stretch(swatchRt, 5, 5, 5, 5);
            var swatch = swatchRt.gameObject.AddComponent<Image>();
            swatch.raycastTarget = false;

            input.onEndEdit.AddListener(text =>
            {
                text = text.Trim();
                if (!text.StartsWith("#"))
                    text = "#" + text;
                if (ColorUtility.TryParseHtmlString(text, out var color))
                    row.SetPending(color, true);
                else
                    row.RefreshControl();
            });
            row.RefreshControl = () =>
            {
                var c = row.Pending is Color col ? col : Color.white;
                input.SetTextWithoutNotify("#" + ColorUtility.ToHtmlStringRGBA(c));
                swatch.color = c;
            };
            row.MainSelectable = input;
            row.Editable = true;
        }

        private void BuildKeyBinding(EntryRow row, RectTransform area)
        {
            var button = _kit.CreateKeyButton(area, out var label);
            UiKit.PlaceLeft((RectTransform)button.transform, 0, ControlWidth, 30);
            button.onClick.AddListener(() => BeginKeyCapture(row, button, label));
            row.RefreshControl = () =>
            {
                if (label == null)
                    return;
                label.text = row.Pending is KeyboardShortcut ks ? ValueUtil.KeyText(ks)
                    : row.Pending is KeyCode kc ? ValueUtil.KeyName(kc) : "";
            };
            row.MainSelectable = button;
            row.Editable = true;
        }

        private void BuildReadOnly(EntryRow row, RectTransform area)
        {
            var text = _kit.CreateLabel(area, "");
            UiKit.Stretch((RectTransform)text.transform);
            var baseColor = text.color;
            text.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0.6f);
            row.RefreshControl = () => text.text = ValueUtil.Display(row.Info, row.Pending);
            row.MainSelectable = null;
            row.Editable = false;
        }

        // ------------------------------------------------------------------ filtering

        private void SetAllCollapsed(bool collapsed)
        {
            foreach (var group in _groups)
                s_collapsed[group.Plugin.Guid] = collapsed;
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            if (_content == null)
                return;

            string query = (_search != null ? _search.text : "").Trim().ToLowerInvariant();
            bool searching = query.Length > 0;
            bool showAdvanced = Plugin.ShowAdvanced.Value;
            bool anyGroup = false;

            foreach (var group in _groups)
            {
                bool nameMatch = searching && group.SearchName.Contains(query);
                bool expanded = searching || !s_collapsed[group.Plugin.Guid];
                bool groupVisible = false;

                foreach (var section in group.Sections)
                {
                    bool sectionVisible = false;
                    foreach (var row in section.Rows)
                    {
                        bool match = (showAdvanced || !row.Info.Advanced) &&
                                     (!searching || nameMatch || row.SearchText.Contains(query));
                        row.Root.SetActive(expanded && match);
                        sectionVisible |= match;
                    }
                    section.Root.SetActive(expanded && sectionVisible);
                    groupVisible |= sectionVisible;
                }

                group.Header.SetActive(groupVisible);
                group.Spacer.SetActive(groupVisible);
                anyGroup |= groupVisible;

                if (group.HeaderLabel != null)
                {
                    int count = group.Sections.Sum(s => s.Rows.Count(r => showAdvanced || !r.Info.Advanced));
                    string version = string.IsNullOrEmpty(group.Plugin.Version) ? "" : $"  <size=75%><alpha=#99>v{group.Plugin.Version}";
                    group.HeaderLabel.text = $"{(expanded ? "-" : "+")}  {group.Plugin.Name}{version}" +
                                             $"<alpha=#66>  ({count} setting{(count == 1 ? "" : "s")})";
                }
            }

            if (_emptyText != null)
            {
                _emptyText.gameObject.SetActive(!anyGroup);
                _emptyText.text = searching ? "No settings match your search." : "No mods with configurable settings were found.";
                _emptyText.transform.SetAsLastSibling();
            }
        }

        // ------------------------------------------------------------------ per-frame

        private void Update()
        {
            if (!_built)
                return;

            if (_captureRow != null)
                UpdateKeyCapture();

            bool wantBlock = _captureRow != null || IsTyping();
            if (wantBlock)
                _lastBlockFrame = Time.frameCount;
            // Hold the block one extra frame so the Escape that ends typing doesn't also close Settings.
            SetBlocking(wantBlock || Time.frameCount - _lastBlockFrame <= 1);

            // Keep the gamepad selection on screen.
            var es = EventSystem.current;
            var selected = es != null ? es.currentSelectedGameObject : null;
            if (selected != _lastSelected)
            {
                _lastSelected = selected;
                if (selected != null && _ensureVisible != null && ZInput.IsGamepadActive() &&
                    selected.transform.IsChildOf(_content))
                {
                    var target = selected.transform;
                    while (target.parent != null && target.parent != _content)
                        target = target.parent;
                    _ensureVisible.CenterOnItem((RectTransform)target);
                }
            }
        }

        private void OnDisable()
        {
            EndKeyCapture();
            SetBlocking(false);
        }

        private static bool IsTyping()
        {
            var es = EventSystem.current;
            var go = es != null ? es.currentSelectedGameObject : null;
            if (go == null)
                return false;
            var input = go.GetComponent<TMP_InputField>();
            return input != null && input.isFocused;
        }

        /// <summary>
        /// Stops the settings window reacting to keys (Q/E/Tab switch tabs, Escape closes) while the player types or binds a key.
        /// </summary>
        private void SetBlocking(bool block)
        {
            if (block == _blocking)
                return;
            _blocking = block;
            if (_tabHandler != null)
            {
                if (block)
                {
                    _savedKeyboardInput = _tabHandler.m_keybaordInput;
                    _savedTabKeyInput = _tabHandler.m_tabKeyInput;
                    _savedGamepadInput = _tabHandler.m_gamepadInput;
                    _tabHandler.m_keybaordInput = false;
                    _tabHandler.m_tabKeyInput = false;
                    _tabHandler.m_gamepadInput = false;
                }
                else
                {
                    _tabHandler.m_keybaordInput = _savedKeyboardInput;
                    _tabHandler.m_tabKeyInput = _savedTabKeyInput;
                    _tabHandler.m_gamepadInput = _savedGamepadInput;
                }
            }
            if (_settings != null)
            {
                if (block)
                {
                    _savedNavBlocked = s_navigationBlocked(_settings);
                    s_navigationBlocked(_settings) = true;
                }
                else
                {
                    s_navigationBlocked(_settings) = _savedNavBlocked;
                }
            }
        }

        // ------------------------------------------------------------------ key capture

        private void BeginKeyCapture(EntryRow row, Button button, TMP_Text label)
        {
            EndKeyCapture();
            _captureRow = row;
            _captureButton = button;
            _captureLabel = label;
            _captureFrame = Time.frameCount;
            if (label != null)
                label.text = "Press a key  (Esc: cancel, Backspace: clear)";
            // Deselect so Enter/Space don't re-trigger the button while binding.
            EventSystem.current?.SetSelectedGameObject(null);
        }

        private void EndKeyCapture()
        {
            if (_captureRow == null)
                return;
            var row = _captureRow;
            var button = _captureButton;
            _captureRow = null;
            _captureButton = null;
            _captureLabel = null;
            row.RefreshControl?.Invoke();
            if (button != null && button.isActiveAndEnabled)
                EventSystem.current?.SetSelectedGameObject(button.gameObject);
        }

        private void UpdateKeyCapture()
        {
            if (Time.frameCount <= _captureFrame)
                return;

            var input = UnityInput.Current;
            if (input.GetKeyDown(KeyCode.Escape) || input.GetKeyDown(KeyCode.Mouse0))
            {
                EndKeyCapture();
                return;
            }
            if (input.GetKeyDown(KeyCode.Backspace))
            {
                CommitKey(KeyCode.None, new KeyCode[0]);
                return;
            }

            if (s_captureKeys == null)
            {
                s_captureKeys = input.SupportedKeyCodes
                    .Where(k => k != KeyCode.None && k != KeyCode.Mouse0 && !s_modifierKeys.Contains(k))
                    .Distinct()
                    .ToArray();
            }

            foreach (var key in s_captureKeys)
            {
                if (!input.GetKeyDown(key))
                    continue;
                CommitKey(key, s_modifierKeys.Where(SafeGetKey).Distinct().ToArray());
                return;
            }

            // A modifier released on its own binds the modifier itself (e.g. Left Alt).
            foreach (var mod in s_modifierKeys)
            {
                if (!SafeGetKeyUp(mod))
                    continue;
                CommitKey(mod, s_modifierKeys.Where(m => m != mod && SafeGetKey(m)).Distinct().ToArray());
                return;
            }
        }

        private static bool SafeGetKey(KeyCode key)
        {
            try { return UnityInput.Current.GetKey(key); }
            catch { return false; }
        }

        private static bool SafeGetKeyUp(KeyCode key)
        {
            try { return UnityInput.Current.GetKeyUp(key); }
            catch { return false; }
        }

        private void CommitKey(KeyCode main, KeyCode[] modifiers)
        {
            var row = _captureRow;
            if (row == null)
                return;
            if (row.Info.Entry.SettingType == typeof(KeyCode))
                row.SetPending(main, false);
            else
                row.SetPending(main == KeyCode.None ? KeyboardShortcut.Empty : new KeyboardShortcut(main, modifiers), false);
            EndKeyCapture();
        }

        // ------------------------------------------------------------------ view models

        private sealed class GroupView
        {
            public PluginConfigInfo Plugin;
            public string SearchName;
            public GameObject Header;
            public GameObject Spacer;
            public TMP_Text HeaderLabel;
            public Button HeaderButton;
            public readonly List<SectionView> Sections = new List<SectionView>();
        }

        private sealed class SectionView
        {
            public GameObject Root;
            public readonly List<EntryRow> Rows = new List<EntryRow>();
        }

        private sealed class EntryRow
        {
            public EntryInfo Info;
            public PluginConfigInfo Plugin;
            public GameObject Root;
            public TMP_Text Label;
            public Color LabelColor;
            public Button Reset;
            public Selectable MainSelectable;
            public bool Editable;
            public string SearchText;
            public object Pending;
            public Action RefreshControl;

            public bool Dirty => !ValueUtil.Equal(Pending, Info.Entry.BoxedValue);

            public void SetPending(object value, bool refreshControl)
            {
                Pending = ValueUtil.Clamp(Info.Entry, value);
                if (refreshControl)
                    RefreshControl?.Invoke();
                UpdateState();
            }

            public void UpdateState()
            {
                // Unsaved edits are tinted so it's obvious what OK will write.
                if (Label != null)
                    Label.color = Dirty ? ModifiedColor : LabelColor;
                if (Reset != null)
                    Reset.interactable = !ValueUtil.Equal(Pending, Info.DefaultValue);
            }
        }
    }
}
