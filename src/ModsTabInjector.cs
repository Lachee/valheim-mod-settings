using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ModConfig
{
    /// <summary>
    /// Adds a "Mods" tab button and an (initially empty) page to a freshly instantiated settings window.
    /// Must run before Settings.InitializeTabs so the game registers our page as one of its tabs.
    /// </summary>
    internal static class ModsTabInjector
    {
        public static void Inject(Settings settings)
        {
            Transform panel = settings.transform.Find("Panel");
            Transform tabButtons = panel ? panel.Find("TabButtons") : null;
            Transform tabContent = panel ? panel.Find("TabContent") : null;
            TabHandler handler = tabButtons ? tabButtons.GetComponent<TabHandler>() : null;
            if (handler == null || tabContent == null)
            {
                Plugin.Log.LogWarning("Settings window layout not recognised; the mods tab was not added.");
                return;
            }

            if (handler.m_tabs.Any(t => t.m_page != null && t.m_page.GetComponent<ModsSettingsTab>() != null))
                return;

            var template = handler.m_tabs.LastOrDefault(t => t.m_button != null);
            if (template == null)
            {
                Plugin.Log.LogWarning("Settings window has no tab buttons to copy; the mods tab was not added.");
                return;
            }

            Button button = CreateButton(template.m_button, tabButtons);
            RectTransform page = CreatePage(tabContent);

            var tab = page.gameObject.AddComponent<ModsSettingsTab>();
            tab.Bind(settings, handler);

            handler.m_tabs.Add(new TabHandler.Tab
            {
                m_button = button,
                m_page = page,
                m_default = false,
                m_onClick = new UnityEvent(),
            });
        }

        private static Button CreateButton(Button template, Transform parent)
        {
            GameObject go = Object.Instantiate(template.gameObject, parent, false);
            go.name = "Mods";
            go.transform.SetAsLastSibling();

            var button = go.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();

            // The last vanilla tab carries the "next tab" gamepad hint on its right edge. Our tab is now last,
            // so hand that hint over instead of showing two.
            Transform clonedHint = go.transform.Find("KeyHint");
            Transform originalHint = template.transform.Find("KeyHint");
            if (clonedHint != null)
                Object.DestroyImmediate(clonedHint.gameObject);
            if (originalHint != null && template.transform.GetSiblingIndex() == parent.childCount - 2)
                originalHint.SetParent(go.transform, false);

            foreach (var text in go.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.transform.parent == go.transform || text.name == "LabelSelected")
                    text.text = Plugin.TabName.Value;
            }
            return button;
        }

        private static RectTransform CreatePage(Transform tabContent)
        {
            var go = new GameObject("Mods", typeof(RectTransform));
            go.SetActive(false);
            var rt = (RectTransform)go.transform;
            rt.SetParent(tabContent, false);
            UiKit.Stretch(rt);
            go.AddComponent<CanvasGroup>();
            return rt;
        }
    }
}
