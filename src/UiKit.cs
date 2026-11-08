using System.Linq;
using GUIFramework;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valheim.SettingsGui;

namespace ModSettings
{
    /// <summary>
    /// Builds widgets by cloning the controls Valheim's own settings pages use, so everything matches the
    /// game's look (sprites, fonts, sounds, gamepad behaviour) without shipping any assets.
    /// </summary>
    internal sealed class UiKit
    {
        public GameObject ToggleTemplate;
        public GameObject SliderTemplate;
        public GameObject DropdownTemplate;
        public GameObject ButtonTemplate;
        public GameObject ScrollbarTemplate;
        public GameObject TooltipTemplate;
        public Image ValueBoxTemplate;
        /// <summary>A key binding button from Keyboard &amp; Mouse; its look (dark box, grey hover tint) is reused for text boxes.</summary>
        public Button BindingButtonTemplate;
        /// <summary>Mouse wheel speed of the vanilla Graphics list.</summary>
        public float ScrollSensitivity = 300f;
        public Image SeparatorTemplate;
        public TMP_Text LabelTemplate;
        public TMP_Text TitleTemplate;

        public bool IsValid => ToggleTemplate && ButtonTemplate && LabelTemplate;

        private static readonly AccessTools.FieldRef<SettingsTooltip, GameObject> s_tooltipObject =
            AccessTools.FieldRefAccess<SettingsTooltip, GameObject>("m_tooltip");
        private static readonly AccessTools.FieldRef<SettingsTooltip, Selectable> s_tooltipSelectable =
            AccessTools.FieldRefAccess<SettingsTooltip, Selectable>("m_selectableOverride");
        private static readonly AccessTools.FieldRef<SettingsTooltip, RectTransform> s_tooltipPosition =
            AccessTools.FieldRefAccess<SettingsTooltip, RectTransform>("m_positionOverride");
        private static readonly AccessTools.FieldRef<SettingsTooltip, SettingsTooltip.TooltipAlignment> s_tooltipAlignment =
            AccessTools.FieldRefAccess<SettingsTooltip, SettingsTooltip.TooltipAlignment>("m_tooltipAlignment");

        public static UiKit FromSettings(Transform settingsRoot)
        {
            Transform content = settingsRoot.Find("Panel/TabContent");
            var kit = new UiKit();

            kit.ToggleTemplate = FindGo(content, "Gameplay/List/ToggleSprint")
                ?? content.GetComponentsInChildren<GuiToggle>(true).FirstOrDefault(t => t.transform.Find("Label"))?.gameObject;
            kit.SliderTemplate = FindGo(content, "Audio/List/MasterVolume")
                ?? content.GetComponentsInChildren<GuiSlider>(true).FirstOrDefault()?.gameObject;
            kit.DropdownTemplate = FindGo(content, "RadialTab/List/Radial Size")
                ?? content.GetComponentsInChildren<GuiDropdown>(true).FirstOrDefault()?.gameObject;
            kit.ButtonTemplate = FindGo(content, "RadialTab/List/Back")
                ?? content.GetComponentsInChildren<GuiButton>(true).FirstOrDefault(b => b.GetComponent<UIGamePad>() == null)?.gameObject;
            kit.ScrollbarTemplate = FindGo(content, "Graphics/Scrollbar")
                ?? content.GetComponentsInChildren<Scrollbar>(true).FirstOrDefault()?.gameObject;
            kit.TooltipTemplate = FindGo(content, "Graphics/SettingsTooltip") ?? FindGo(content, "Gameplay/SettingsTooltip");
            kit.ValueBoxTemplate = FindGo(content, "Graphics/SettingsList/Viewport/Content/GraphicsMode/Stepper/Value")?.GetComponent<Image>()
                ?? kit.DropdownTemplate?.GetComponent<Image>();
            kit.BindingButtonTemplate = (FindGo(content, "KeyboardMouse/List/Bindings/Viewport/Grid/Jump/Button")
                ?? content.Find("KeyboardMouse/List/Bindings")?.GetComponentsInChildren<GuiButton>(true).FirstOrDefault()?.gameObject)
                ?.GetComponent<Button>();
            var graphicsScroll = content.Find("Graphics/SettingsList")?.GetComponent<ScrollRect>();
            if (graphicsScroll != null && graphicsScroll.scrollSensitivity > 1f)
                kit.ScrollSensitivity = graphicsScroll.scrollSensitivity;
            kit.SeparatorTemplate = settingsRoot.Find("Panel/HeaderLine")?.GetComponent<Image>();
            kit.LabelTemplate = kit.ToggleTemplate ? kit.ToggleTemplate.transform.Find("Label")?.GetComponent<TMP_Text>() : null;
            if (kit.LabelTemplate == null)
                kit.LabelTemplate = content.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault();
            kit.TitleTemplate = settingsRoot.Find("Panel/Title")?.GetComponent<TMP_Text>() ?? kit.LabelTemplate;
            return kit;
        }

        private static GameObject FindGo(Transform root, string path)
        {
            var t = root ? root.Find(path) : null;
            return t ? t.gameObject : null;
        }

        // ---------------------------------------------------------------- layout helpers

        public static void Stretch(RectTransform rt, float left = 0, float right = 0, float top = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>Places a rect at a fixed x/width from the parent's left edge, vertically centred.</summary>
        public static void PlaceLeft(RectTransform rt, float x, float width, float height)
        {
            rt.anchorMin = new Vector2(0, 0.5f);
            rt.anchorMax = new Vector2(0, 0.5f);
            rt.pivot = new Vector2(0, 0.5f);
            rt.anchoredPosition = new Vector2(x, 0);
            rt.sizeDelta = new Vector2(width, height);
        }

        public static void PlaceRight(RectTransform rt, float rightInset, float width, float height)
        {
            rt.anchorMin = new Vector2(1, 0.5f);
            rt.anchorMax = new Vector2(1, 0.5f);
            rt.pivot = new Vector2(1, 0.5f);
            rt.anchoredPosition = new Vector2(-rightInset, 0);
            rt.sizeDelta = new Vector2(width, height);
        }

        public static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        // ---------------------------------------------------------------- widgets

        public TMP_Text CreateLabel(Transform parent, string text, float fontScale = 1f)
        {
            var rt = CreateRect("Label", parent);
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            CopyTextStyle(LabelTemplate, tmp);
            tmp.fontSize = LabelTemplate.fontSize * fontScale;
            tmp.enableAutoSizing = false;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.richText = true;
            tmp.text = text;
            return tmp;
        }

        public TMP_Text CreateHeading(Transform parent, string text, float size)
        {
            var rt = CreateRect("Heading", parent);
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            CopyTextStyle(TitleTemplate, tmp);
            tmp.fontSize = size;
            tmp.enableAutoSizing = false;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.richText = true;
            tmp.text = text;
            return tmp;
        }

        private static void CopyTextStyle(TMP_Text from, TMP_Text to)
        {
            to.font = from.font;
            to.fontSharedMaterial = from.fontSharedMaterial;
            to.fontStyle = from.fontStyle;
            to.color = from.color;
            to.characterSpacing = from.characterSpacing;
            to.raycastTarget = false;
        }

        public Image CreateSeparator(Transform parent)
        {
            var rt = CreateRect("Separator", parent);
            var img = rt.gameObject.AddComponent<Image>();
            if (SeparatorTemplate)
            {
                img.sprite = SeparatorTemplate.sprite;
                img.type = SeparatorTemplate.type;
                img.color = SeparatorTemplate.color;
                img.material = SeparatorTemplate.material;
            }
            else
            {
                img.color = new Color(1f, 1f, 1f, 0.15f);
            }
            img.raycastTarget = false;
            return img;
        }

        public Toggle CreateToggle(Transform parent)
        {
            var go = Object.Instantiate(ToggleTemplate, parent, false);
            go.name = "Toggle";
            go.SetActive(true);
            StripTooltip(go);
            DestroyChild(go.transform, "Label");
            var toggle = go.GetComponent<Toggle>();
            toggle.onValueChanged = new Toggle.ToggleEvent();
            toggle.group = null;
            toggle.interactable = true;
            return toggle;
        }

        public Slider CreateSlider(Transform parent)
        {
            if (!SliderTemplate)
                return null;
            var go = Object.Instantiate(SliderTemplate, parent, false);
            go.name = "Slider";
            go.SetActive(true);
            StripTooltip(go);
            DestroyChild(go.transform, "Label");
            DestroyChild(go.transform, "Value");
            var slider = go.GetComponent<Slider>();
            slider.onValueChanged = new Slider.SliderEvent();
            slider.interactable = true;
            return slider;
        }

        public TMP_Dropdown CreateDropdown(Transform parent)
        {
            if (!DropdownTemplate)
                return null;
            var go = Object.Instantiate(DropdownTemplate, parent, false);
            go.name = "Dropdown";
            go.SetActive(true);
            StripTooltip(go);
            var dropdown = go.GetComponent<TMP_Dropdown>();
            // The template has two children called "Label": the dropdown's caption and the row label. Drop the latter.
            foreach (Transform child in go.transform.Cast<Transform>().ToList())
            {
                if (child.name == "Label" && (dropdown.captionText == null || child != dropdown.captionText.transform))
                    Object.DestroyImmediate(child.gameObject);
            }
            dropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
            dropdown.interactable = true;
            dropdown.ClearOptions();
            return dropdown;
        }

        public Button CreateButton(Transform parent, string text, out TMP_Text label)
        {
            var go = Object.Instantiate(ButtonTemplate, parent, false);
            go.name = "Button";
            go.SetActive(true);
            StripTooltip(go);
            foreach (var pad in go.GetComponentsInChildren<UIGamePad>(true))
                Object.DestroyImmediate(pad);
            DestroyChild(go.transform, "KeyHint");
            var button = go.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.interactable = true;
            label = go.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                // The template insets its label 10px on every side, which leaves no vertical room on our
                // shorter buttons; TMP's ellipsis mode then hides the whole line. Use the full height instead.
                Stretch(label.rectTransform, 8, 8, 0, 0);
                label.text = text;
                label.enableAutoSizing = true;
                label.fontSizeMin = 10;
                label.fontSizeMax = LabelTemplate.fontSize;
                label.enableWordWrapping = false;
                label.overflowMode = TextOverflowModes.Ellipsis;
            }
            return button;
        }

        /// <summary>A copy of the vanilla key binding button, falling back to a regular button.</summary>
        public Button CreateKeyButton(Transform parent, out TMP_Text label)
        {
            if (!BindingButtonTemplate)
                return CreateButton(parent, "", out label);
            var go = Object.Instantiate(BindingButtonTemplate.gameObject, parent, false);
            go.name = "KeyButton";
            go.SetActive(true);
            StripTooltip(go);
            var button = go.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.interactable = true;
            label = go.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.enableAutoSizing = true;
                label.fontSizeMin = 10;
                label.fontSizeMax = LabelTemplate.fontSize;
                label.enableWordWrapping = false;
                label.overflowMode = TextOverflowModes.Ellipsis;
            }
            return button;
        }

        public TMP_InputField CreateInputField(Transform parent, string placeholder)
        {
            var rt = CreateRect("Input", parent);
            var go = rt.gameObject;
            go.SetActive(false);

            var bg = go.AddComponent<Image>();
            var bgTemplate = BindingButtonTemplate ? BindingButtonTemplate.targetGraphic as Image : null;
            if (bgTemplate == null)
                bgTemplate = ValueBoxTemplate;
            if (bgTemplate)
            {
                bg.sprite = bgTemplate.sprite;
                bg.type = bgTemplate.type;
                bg.color = bgTemplate.color;
                bg.material = bgTemplate.material;
                bg.pixelsPerUnitMultiplier = bgTemplate.pixelsPerUnitMultiplier;
            }
            else
            {
                bg.color = new Color(0f, 0f, 0f, 0.5f);
            }

            var area = CreateRect("Text Area", rt);
            Stretch(area, 8, 8, 2, 2);
            area.gameObject.AddComponent<RectMask2D>();

            var text = CreateLabel(area, "");
            Stretch((RectTransform)text.transform);
            text.overflowMode = TextOverflowModes.Overflow;
            text.richText = false;
            text.raycastTarget = true;

            var ph = CreateLabel(area, placeholder);
            Stretch((RectTransform)ph.transform);
            ph.fontStyle = FontStyles.Italic;
            ph.color = new Color(text.color.r, text.color.g, text.color.b, 0.4f);

            var input = go.AddComponent<GuiInputField>();
            input.targetGraphic = bg;
            if (BindingButtonTemplate)
            {
                // Same hover / selected highlight as the vanilla key binding boxes.
                input.transition = Selectable.Transition.ColorTint;
                input.colors = BindingButtonTemplate.colors;
            }
            input.textViewport = area;
            input.textComponent = text;
            input.placeholder = ph;
            input.fontAsset = text.font;
            input.pointSize = text.fontSize;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.richText = false;
            input.customCaretColor = true;
            input.caretColor = text.color;
            input.selectionColor = new Color(1f, 0.7f, 0.3f, 0.35f);
            input.onFocusSelectAll = true;
            input.restoreOriginalTextOnEscape = true;

            go.SetActive(true);
            return input;
        }

        /// <summary>Clones the settings tooltip popup into <paramref name="parent"/> so it isn't hidden with another tab.</summary>
        public GameObject CreateTooltipPopup(Transform parent)
        {
            if (!TooltipTemplate)
                return null;
            var go = Object.Instantiate(TooltipTemplate, parent, false);
            go.name = "SettingsTooltip";
            go.SetActive(false);
            // Never let the popup steal the hover from the row it describes.
            var group = go.GetComponent<CanvasGroup>() ?? go.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            var bg = go.transform.Find("Background") as RectTransform;
            if (bg != null)
                bg.sizeDelta = new Vector2(380, bg.sizeDelta.y);
            return go;
        }

        public void AttachTooltip(GameObject host, GameObject popup, Selectable selectable, RectTransform anchor, string topic, string text)
        {
            if (popup == null || selectable == null || string.IsNullOrEmpty(text))
                return;
            var tip = host.AddComponent<SettingsTooltip>();
            s_tooltipObject(tip) = popup;
            s_tooltipSelectable(tip) = selectable;
            s_tooltipPosition(tip) = anchor;
            s_tooltipAlignment(tip) = SettingsTooltip.TooltipAlignment.Bottom;
            tip.SetTexts(topic, text);
        }

        private static void StripTooltip(GameObject go)
        {
            foreach (var tip in go.GetComponentsInChildren<SettingsTooltip>(true))
                Object.DestroyImmediate(tip);
        }

        private static void DestroyChild(Transform parent, string name)
        {
            var child = parent.Find(name);
            if (child != null)
                Object.DestroyImmediate(child.gameObject);
        }
    }
}
