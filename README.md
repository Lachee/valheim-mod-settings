# Mod Settings

Adds a **Mods** tab to Valheim's own Settings window. It lists every installed mod's BepInEx config settings in one scrolling page. Like [BepInEx.ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager), but built from the game's native UI: the same checkboxes, sliders, dropdowns, buttons, scrollbar and tooltips the vanilla settings tabs use.

## Features

- A **Mods** tab next to Gameplay, Controls, Graphics, Audio and Accessibility. It's in the Settings window from both the main menu and the in-game Esc menu.
- Every mod gets a header you can click to expand or collapse it. Mods start collapsed. Under each header, settings are grouped by config section.
- The editor depends on the setting's type:
  - **On/off** settings get a checkbox.
  - **Ranged numbers** get a slider plus a box where you can type an exact value.
  - **Enums and fixed lists** get a dropdown.
  - **Key bindings** (`KeyboardShortcut` / `KeyCode`) get a button that waits for you to press a key. Modifiers like Shift+J are supported. Esc cancels and Backspace clears the binding.
  - **Colours** get a hex box with a colour preview.
  - **Anything else BepInEx can convert** (text, numbers, vectors, ...) gets a text box.
- Hover a setting, or select it with a gamepad, to see its description, range and default value.
- Each setting has a **Reset** button that restores its default.
- Edits are only saved when you press **OK**. **Back** throws them away. Settings you've changed but not saved yet show in orange.
- A **search box** filters by mod name, section, setting name or description.
- **Show advanced** reveals settings that mod authors marked as advanced.
- It reads the same `ConfigurationManagerAttributes` hints as ConfigurationManager: `Browsable`, `IsAdvanced`, `ReadOnly`, `Order`, `DispName`, `Category`, `HideDefaultButton`, `DefaultValue`, `ShowRangeAsPercent`, `ObjToStr`/`StrToObj`. Mods that already support ConfigurationManager look right here too.

ConfigurationManager's IMGUI `CustomDrawer`s can't run in uGUI. Settings that use one get the normal editor for their type instead.

## Configuration

`BepInEx/config/lachee.modsettings.cfg` (or edit these from the Mods tab itself):

| Setting | Default | Description |
| --- | --- | --- |
| Tab name | `Mods` | Text on the tab button. |
| Start expanded | `false` | Show every mod expanded when the tab opens. Mods start collapsed by default. |
| Show BepInEx settings | `false` | Also list BepInEx's own `BepInEx.cfg`. These only show while *Show advanced* is ticked. |

## Building

```sh
dotnet build -c Release                               # builds and copies the DLL into the Amethyst mod folder
dotnet build -c Release -p:DeployToGame=false         # build only
dotnet build -c Release -p:ValheimDir=/path/to/Valheim
```

Releases are built by `.github/workflows/release.yml` when you push a `v*` tag.

## How it works

A Harmony prefix on `Settings.Awake` runs before the game registers its tabs. It clones the last tab button and adds a new page that implements the game's `ISettingsTab` interface. From then on the game treats the page like a vanilla tab: it gets `OnTabOpen`, OK calls `OnOkAsync` to save, and Back calls `OnBack` to discard. The page is built the first time you open it. Its controls are clones of the vanilla settings controls, so they match the game's look and work with a mouse or a gamepad.
