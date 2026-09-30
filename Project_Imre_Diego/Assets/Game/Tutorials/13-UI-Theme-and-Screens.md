# UI: theme, screens, and adding a menu

[All tutorials](../README.md)

All menus and the HUD use **UI Toolkit**. The look lives in one stylesheet; screens are built in code from a small shared kit, so a new menu gets the same visual language automatically.

| File | Role |
| --- | --- |
| `Assets/Game/UI/Theme/GameUI.uss` | Every colour, font, size, spacing value, state and transition |
| `Assets/Game/UI/Theme/GameTheme.tss` | Theme used by the panel: Unity defaults plus GameUI.uss |
| `Assets/Game/UI/GamePanelSettings.asset` | Scales the UI from a 1920×1080 reference to any resolution or aspect ratio |
| `Assets/Game/UI/Fonts` | IM Fell English SC (titles), EB Garamond (navigation, subtitles), Inter (functional text) and Roboto Mono (codes, values), with licences in `LICENSES.md` |
| `Scripts/UI/UIKit.cs` | Builders: `NewScreen`, `NewMenuItem`, `NewLink`, `NewSection`, `NewSlider`, `NewToggle`, `NewField`, `Show` |
| `Scripts/UI/GameUI.cs` | Menu, lobby, options, loading, pause, end screen and HUD |

## The design: the house is the interface

There are **no panels or boxes** anywhere in the menus:
- Words sit directly over the living Mansion.
- Soft shadow gradients at the screen edges keep them readable: `MenuColumn` darkens the left, `RightShade` the right.
- The centre of the hall is left open as negative space.

The layout is asymmetric. The main column sits on the left, and secondary information (the lobby's party and house, the options content) floats on the right. Both drift a couple of pixels, very slowly, as if hung in the air of the hall.

**Palette** (variables at the top of `GameUI.uss`):
- near-black `--bg`, charcoal and deep-brown tints;
- aged ivory `--ivory` / `--text` for text;
- a muted crimson `--accent` / `--accent-bright`, used sparingly: kickers, rules, the hover line, slider fill, seat numerals.

Keep red for accents; small text in red is hard to read on black.

**Type:**
- **IM Fell English SC:** titles and section titles.
- **EB Garamond:** navigation, subtitles, italic notes.
- **Inter:** functional captions and small actions.
- **Roboto Mono:** codes and values.

## Interaction

- **Menu items** (`NewMenuItem`) are words. On hover or keyboard/gamepad focus, the word brightens and eases right, a thin crimson line draws itself under it, and a small ◂ mark slides in.
  - The word, line and mark share a `menu-item-wrap` that shrinks to the word. A Button with child elements stops measuring its text, so never add children to the Button itself.
  - Variants: `menu-item--primary` (main action) and `menu-item--danger` (leaving).
  - `button.SetCurrent(true)` keeps the line and mark drawn, as for the open options section.
- **Links** (`NewLink`) are small underlined actions (COPY, RETRY VOICE) whose line warms to crimson.
- **Fields** are a single line to write on.
- **Sliders** are a hairline with a crimson thread up to a small diamond handle that grows on hover.
- **Switches** are the words ON and OFF; the chosen word is lit and underlined.
- **Selectors** (Map, Difficulty) show the value between two thin arrows. They never open a list, so they can't run off-screen, and they work with any input.

## Screens

**Main menu.** The Main Menu scene's **Menu Backdrop** loads the real Grand Hall and applies the Mansion atmosphere. It then:
- drifts the camera slowly along a looping path;
- lets the lanterns flicker and dust drift;
- plays a synthesised ambience (`MenuAmbience`): house drone, draughts and creaks, with no audio asset.

Rare events, never jump scares:
- a shadow crosses the gallery lanterns every 28–55 s;
- every 38–75 s, either the door at the top of the stairs eases a hand's width open with a creak and settles, or footsteps cross the gallery above.

The door is the Ordinary Door's Hinge model only, with no networking. Assign it in **Menu Backdrop > Door Prefab**.

The title block surfaces slowly out of the dark when the menu opens, and the title very rarely stutters like a lantern.

**Lobby.**
- **Left column:** a kicker, the "Gathering" title, the lobby code in large monospace with a COPY link, then Start, Options and Leave.
- **Right:** *Survivors* (numbered seats in roman numerals, open seats shown as waiting) and *The house* (Map, Difficulty, and the rooms, objectives and monster count).

**Options.**
- **Left:** the column with section navigation (Audio, Controls, Video), Back, and a note.
- **Right:** titled sections with a hairline running out from each title, and one setting per row.

Settings are pushed to `LocalSettings` as they change and saved when the screen closes, so there is no Apply button. Opened from Pause, the running game behind is darker (`screen--options-game`). Switching sections fades and slides the page in.

**Pause** and **end of round** use the same column over the still-running world. Pause never changes `Time.timeScale`, because the game is multiplayer.

## Adding a screen

```csharp
var credits = NewScreen(root, "Credits", "screen--backdrop");
var side = MenuColumn(credits);
NewLabel("THE MANSION", "menu-kicker").AddTo(side);
NewLabel("CREDITS", "menu-title", "menu-title--small").AddTo(side);
NewBox("menu-rule").AddTo(side);
NewLabel("Made by …", "menu-subtitle").AddTo(side);
NewMenuItem(side, "BACK", () => showCredits = false, "menu-item--primary");
// in Update:  Show(credits, showCredits);
```

Settings go in sections: `NewSection(parent, title, description)`, then `NewSlider(...)` or `NewToggle(...)`. Set values without firing callbacks with `SetQuiet(...)`.

`Show` fades a screen in. Screens only *display* state: inventory, life state, lobby and round data are read from their own systems every frame, and labels update only when a value changes.

## HUD notes

- **Prompts** show the key badge only when the action is possible (`PlayerInteraction.PromptAvailable`). Requirement text such as "Needs a medkit to revive …" is shown muted, without the key. Downed, dead and spectating players never get prompts.
- **Inventory** shows three slots read directly from `PlayerInventory`; the selected slot is raised and outlined. Radio and flashlight state appear as tags.
- **Downed** shows the bleed-out time and bar from the replicated bleed-out timer, the same source that drives the red vignette (`DownedVignette`), so the two cannot drift apart.
- A small development line (map, seed, room count) appears only in the editor and development builds.

`GameUI.SetPaused(bool)` and `GameUI.ShowOptions()` are public so other input (for example a gamepad Start button) can use them.
