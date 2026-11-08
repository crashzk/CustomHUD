# Configuration

Advanced settings for server owners. Nothing here is required: the defaults are
tuned for the best overall experience, and installation is covered in the
[README](../README.md#installation).

## Files

CustomHUD reads two files. Both reload live.

| File | Owner | Holds |
|------|-------|-------|
| `addons/swiftlys2/configs/menus/menus.jsonc` | SwiftlyS2 core | Renderer selection, key binds, page size, menu sounds |
| `addons/swiftlys2/configs/plugins/CustomHUD/config.jsonc` | CustomHUD | Default navigation, hints, layout profiles, panels, native text capture |

Every CustomHUD setting sits under a `"CustomHUD"` section.
[`config.example.jsonc`](../config.example.jsonc) is an annotated copy of the
defaults.

## Core Settings (`menus.jsonc`)

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `Menus.Renderer` | string | unset | `"customhud.panorama"` draws every menu with CustomHUD |
| `Menus.<menuId>.Renderer` | string | unset | The same for one menu id; wins over the server-wide value |
| `Menus.InputMethod` | string | unset | The navigation mode players start in until they run `!setnav`: core's `buttons` or `chat`, or CustomHUD's `wasd`, `numbers` or `mouse`, which core ignores. Wins over CustomHUD's `DefaultNavMode`. `chat` makes them `buttons` players, who type `!N` on core's Chat menus |
| `MenuLayout.MaxVisibleLines` | int | `5` | Page size in keyboard modes, clamped to `1`-`32`. Mouse layouts ignore it |

Key binds for `buttons` mode (default Shift / F / E / Tab) live in the same
file; see SwiftlyS2's
[menu configuration](https://swiftlys2.net/docs/development/menu-system#server-configuration).

## Quick Reference

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `RendererId` | string | `"customhud.panorama"` | Id the renderer registers under, the value `menus.jsonc` names. Raw nodes addressed to `customhud.panorama` keep working after a rename |
| `DetailedLogging` | bool | `false` | Log every compose, diff, apply and input decision with a `[CustomHUD]` prefix |
| `DefaultNavMode` | string | `"buttons"` | Mode for players who never ran `!setnav`, when `menus.jsonc` sets no `Menus.InputMethod`: `wasd`, `buttons`, `numbers` or `mouse`. Anything else means `buttons` |
| `Hints` | object | `{}` | Footer hint overrides, see [Hints](#hints) |
| `LayoutRoot` | string | `"panorama/layout/custom_game/customhud/"` | Prefix for every layout `File` without a `/` of its own |
| `Default` | profile | `null` | Keyboard layout for any mode without its own profile. `null` = the list, `{}` = the classic `menu.vxml` |
| `Wasd`, `Buttons`, `Numbers` | profile | `null` | Layout for one keyboard mode; wins over `Default` |
| `Mouse` | profile | `null` | Mouse layout for menus without a mouse rule. `null` = the mouse list |
| `Overrides` | object | `{}` | Exact menu id → profile, for one menu's variant layout |
| `MouseLayouts` | object | `{}` | Mouse-only rules by exact id or trailing-`*` prefix; win over the built-in rules |
| `PageViews` | object | `{}` | Menu id or trailing-`*` prefix → view, see [Plugin Pages](PluginIntegration.md#plugin-pages-mouse) |
| `Panels` | object | `versus`, `ready`, `timeout` | Panel profiles for `CustomHUD.Panels.v1`, see [Panels](#panels) |
| `NativeText` | object | off | Native HUD text capture, see [Native Text Capture](#native-text-capture) |

## Commands

### Player Commands

| Command | Aliases | Permission | Description |
|---------|---------|------------|-------------|
| `!hudscale [percent]` | - | None | Set your menu scale, snapped to the nearest step (`80`, `90`, `100`, `110`, `125`, `150` by default). No argument lists the steps |
| `!setnav [mode]` | - | None | Choose how you navigate menus: `wasd`, `buttons`, `numbers` or `mouse`. No argument shows your current mode |

`!setnav` applies to every menu that player opens, whatever `menus.jsonc` or
the plugin chose.

### Numbers-Mode Commands

These act only for players in `numbers` mode, on menus CustomHUD draws.

| Command | Aliases | Permission | Description |
|---------|---------|------------|-------------|
| `!1` … `!6` | `css_1`…`css_6`, `ms_1`…`ms_6` | None | Select that choice on the current page |
| `!7` | `css_7`, `ms_7` | None | Previous page |
| `!8` | `css_8`, `ms_8` | None | Next page |
| `!9` | `css_9`, `ms_9` | None | Close the menu |

Players bind their number keys to the console names, e.g.
`bind 1 "slot1; sw_1"`. Binds already pointing at `css_N` or `ms_N` from a
CounterStrikeSharp or MatchZy server keep working.

CustomHUD has no admin commands.

## Navigation Modes

| Mode | Input | Handled by |
|------|-------|------------|
| `wasd` | W / S move, E select, R or Tab close | CustomHUD |
| `buttons` | Whatever `menus.jsonc` binds (default Shift / F / E / Tab) | Core |
| `numbers` | `1`-`6` select, `7` / `8` page, `9` close. Tab does not close | CustomHUD |
| `mouse` | Click. Footer `<` back, `×` close, `‹` / `›` page (only with more than one page); sliders and selectors step with their own `‹` `›`. Tab does not close | CustomHUD |

- `buttons` is core's own name for keyboard input, the word `menus.jsonc` uses
  for `"InputMethod"`. Its keys are bound there, not in CustomHUD.
- Until a player runs `!setnav`, `Menus.InputMethod` in `menus.jsonc` picks
  their mode, and `DefaultNavMode` only when it names none. The log says which:
  `Players who have not run !setnav navigate in wasd mode`.
- In `wasd` mode CustomHUD ignores any key the menu's own keymap already handles,
  so the cursor never moves twice. W and S still move the player as well: core
  has no input freeze.
- In `mouse` mode the cursor is captured per player while a menu is open.
- In `mouse` and `numbers` modes, on Buttons-input menus, CustomHUD removes Tab
  before core dispatches menu keys. Focus, chat input and submenus survive, with
  no exit sound or close callback; other players and renderers keep their normal
  binds.
- Back (`<`) returns to the parent menu, or closes a root menu. A layout's close
  button (`×`) leaves every level at once, and Back is then hidden wherever it
  has nowhere to go.
- A menu whose plugin unbound Close (as MixScrims' picks do) has no way out in
  any mode: no `×`, no `<`, and R, Tab and `9` do nothing.
- Mouse and numbers input plays no menu sounds; see
  [Known Limitations](PluginIntegration.md#known-limitations).

## Pages

Outside Mouse mode a menu shows one fixed page at a time. A page holds no more
than the smallest of:

- `MaxVisibleLines` lines from `menus.jsonc`,
- the menu's own `ItemsPerPage` (core's default is `5`; plugins raise it with
  `WithItemsPerPage`),
- the layout's `RowSlots`,
- six choices in numbers mode, the keys there are. A disabled entry takes no
  key, so it costs none of the six.

Moving past a page's last row opens the next page, past the first row the
previous one; both ends wrap. The footer shows the page (`2/5`, with a dot per
page) while there is more than one.

Core itself scrolls a window one row at a time; CustomHUD cuts fixed pages from
the top. **Chat-input menus** are the exception: core resolves `!N` against its
own window, so that window is drawn unchanged.

Mouse layouts set `ShowAllRows`: they draw every row and scroll with the wheel
or a scrollbar. A list longer than the layout's slots pages - the mouse list in
24-row chunks, the tile grid in 60 with footer paging buttons, and the
WeaponSkins locker through a `«` / `»` card at each end of its 128-card grid.

## Layout Resolution

Which profile draws a menu; the first match wins.

| Mode | Resolution order |
|------|------------------|
| `mouse` | `MouseLayouts` (exact id, then longest trailing-`*` prefix) → built-in mouse rules → exact `Overrides` → `Mouse` → the mouse list |
| `wasd`, `buttons`, `numbers` | Exact `Overrides` → the mode's profile → `Default` → the plugin's built-in list → the list |

The built-in mouse rules cover `weaponskins.*`, `shopcore.*`, `mapchooser.*`,
`ranks.*`, `weaponstats.*`, `customcombos.*`, `prestige.*`, `vipcore.*`,
`admins.*` and `mixscrims.*`. They live in code, so an update reaches existing
servers; a matching `MouseLayouts` entry replaces one.

## Layout Profile Settings

A profile points at one compiled layout and names what the renderer writes into
it. A partial profile is **not** merged with `Default`: unset fields take the
built-in defaults below, so repeat `Panels` and `Classes` if your layout renames
them.

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `File` | string | `"menu.vxml_c"` | Compiled layout, resolved against `LayoutRoot`. The `_c` suffix is required |
| `RowSlots` | int | `20` | Rows the layout declares, `1`-`256`. Must match the authored layout |
| `ShowAllRows` | bool | `false` | Draw the whole list in `RowSlots`-sized chunks that scroll, ignoring `MaxVisibleLines` and `ItemsPerPage` |
| `PageTiles` | bool | `false` | With `ShowAllRows` in Mouse mode, page a longer list through a `«` / `»` card at each end of the grid |
| `Panels.Shell`, `Body`, `Title`, `Footer` | string | `shell`, `options`, `title`, `footer` | Panel ids the renderer addresses |
| `Panels.Back`, `Previous`, `Next` | string | `nav_back`, `nav_previous`, `nav_next` | Mouse footer buttons; `""` = the layout has none |
| `Panels.Close` | string | `""` | A `×` that leaves every submenu level at once. With one, Back shows only where it has somewhere to go |
| `Variables.PagePanel`, `PageVar` | string | `page`, `page` | Page counter, written only while a menu has more than one page |
| `Variables.PageFormat` | string | `"{0} / {1}"` | Counter text: `{0}` = page, `{1}` = page count |
| `Classes.ScaleSteps` | int[] | `[80, 90, 100, 110, 125, 150]` | Scale steps the layout styles. `!hudscale` offers those of `Default`, or of the built-in list |
| `Sidebar` | object | `null` | Root-menu sidebar, see below |
| `Card` | object | `null` | Inspect card for `hero` pages (`Slots` = `8`), see [Card Pages](PluginIntegration.md#card-pages) |
| `Page` | object | `null` | Locker parts: eyebrow, crumb, chips, tabs, column heads, regions and views |
| `ColorIcons` | object | `{}` | Text colour → icon token, e.g. rarity colours to `lvl-N` |
| `BlankOnOpen` | array | `[]` | Dialog variables blanked on open, since Panorama draws an unset `{s:var}` literally |
| `Row.*`, `Classes.*` | string | shipped ids | Row panel and variable patterns (`{0}` = row index) and the CSS classes toggled on them |

**Sidebar** - root `SubmenuComponent` entries as shortcuts, root dividers as
rules between them. The entry the player is anywhere beneath is marked: the one
whose component id equals that page's menu id, else whose label equals its
title.

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `Sidebar.Slots` | int | `10` | Entries the layout declares |
| `Sidebar.AutoOpen` | int | `-1` | Entry a root menu opens straight into in Mouse mode (dividers not counted); backing out of it closes the menu. `-1` shows the root page |
| `Sidebar.Icons` | object | `{}` | Entry component id (else label) → icon token from `customhud_icons.vcss`. Unmapped entries keep the layout's default icon; `config.example.jsonc` lists the tokens |
| `Sidebar.CountVar`, `CountClass` | string | `""`, `"has-count"` | Splits a trailing count (`Knives (12)`) into a badge. An empty `CountVar` keeps the label whole |
| `Sidebar.DividerClass` | string | `"divider"` | Class on the slot of a root `DividerComponent` |
| `Sidebar.RootButton`, `ButtonPrefix`, `LabelVar`, `IconPanel`, `IconClass` | string | `nav_root`, `category_`, `category`, `category_{0}_icon`, `ico-{0}` | Layout addressing |

Every field, with a comment on each, is in
[`src/Config/CustomHudConfig.cs`](../src/Config/CustomHudConfig.cs).

## Hints

`Hints` replaces a footer hint in every language, and `""` hides it. Without an
entry, hints come from the player's translation file (`customhud.hint.<key>`).

| Key | Shown on |
|-----|----------|
| `wasd`, `buttons`, `numbers`, `mouse` | Menus in that navigation mode |
| `chat` | Menus whose input method is chat |
| `<key>.view` | The same, on menus with nothing to select (a vote tally kept open after voting) |

`{up}`, `{down}`, `{select}` and `{close}` expand to the menu's keymap. In Mouse
mode `{close}` names the layout's close button (`×`, or `<` without one).

```jsonc
"Hints": {
  "numbers": "1-6 pick · 7/8 page · 9 exit",
  "numbers.view": "9 exit"
}
```

- An entry for a mode replaces the list's key legend with that line.
- An entry equal to the English default counts as unset, because builds before
  translations wrote every default into the file.
- An overridden mode hint also shows on view-only menus until you override its
  `.view` too, so your text is never mixed with a translated one.

## Panels

Standalone panels other plugins draw through the
[Panels API](PluginIntegration.md#panels-api), keyed by profile name. Entries merge over the
defaults, so list only the ones you restyle.

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `Panels.<name>.File` | string | per profile | `panel_versus.vxml_c`, `panel_ready.vxml_c` or `panel_timeout.vxml_c` |
| `Panels.<name>.BlankVariables` | array | per profile | Dialog variables set to `""` on spawn |
| `Panels.<name>.DurationSeconds` | float | `0` | Used when the caller passes none; `<= 0` stays up until hidden |
| `Panels.<name>.ShellPanel` | string | `"shell"` | Root panel |
| `Panels.<name>.HiddenClass` | string | `"hidden"` | Class that hides the shell |
| `Panels.<name>.VariablePanel` | string | `"shell"` | Panel the dialog variables are set on |

## Native Text Capture

Takes over the game's own HUD text, so messages from plugins that know nothing
about CustomHUD still render in your style. It covers every
`CUserMessageTextMsg` destination, which is what `SendCenter`, `SendAlert`,
`SendChat` and `SendMessage` emit.

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `NativeText.Enabled` | bool | `false` | Master switch. While `false`, no hook is installed |
| `NativeText.Default` | profile | below | Used by any captured kind without a profile of its own |
| `NativeText.Center`, `Alert`, `Notify` | profile | `null` | Destinations 4, 6 and 1 |
| `NativeText.Chat`, `ChatEndOfRound`, `Console` | profile | `null` | Destinations 3, 5 and 2. Capturing these is rarely wanted |

Each profile:

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `Capture` | bool | `false` | Take over this kind; `false` passes it through untouched |
| `File` | string | `"hud_text.vxml_c"` | Layout. `hud_alert.vxml_c` also ships |
| `DurationSeconds` | float | `5` | Time on screen; `<= 0` stays until replaced |
| `Suppress` | bool | `true` | Hide the native message. `false` draws both, which helps while authoring a layout |
| `KindClass` | string | `"kind-{0}"` | Class naming the kind (`{0}` = lowercase kind), so one layout can style each |
| `ShellPanel`, `HiddenClass`, `TextPanel`, `TextVariable` | string | `shell`, `hidden`, `text`, `text` | Layout addressing |

> [!NOTE]
> `SendCenterHTML` is not covered. It is not a `TextMsg`: it is buffered and
> re-sent every tick as a game event, and the same buffer carries legacy
> `Core.MenusAPI` menu text. See [PublicPort.md](PublicPort.md) §6.

## Translations

Everything CustomHUD says to players comes from
`resources/translations/<language>.jsonc`, shipped in English (`en`) and
Lithuanian (`lt`). SwiftlyS2 picks the language - the player's game language
while `core.UsePlayerLanguage` is on (its default), otherwise `core.Language` -
and falls back to `en` for a missing file or key.

| Keys | Used for |
|------|----------|
| `customhud.prefix`, `customhud.nav.*`, `customhud.scale.*` | `!setnav` and `!hudscale` replies |
| `customhud.hint.<key>` | Footer hints, one per [Hints](#hints) key |
| `customhud.legend.<verb>` | Words in the list's key legend (`move`, `select`, `pick`, `page`, ...) |
| `customhud.eyebrow.<plugin>` | Plugin names over a page's title; see [Eyebrows](#eyebrows) |
| `customhud.lead`, `customhud.maps.*`, `customhud.ranks.*`, `customhud.shop.*`, `customhud.vip.*`, `customhud.weaponskins.*`, `customhud.weaponstats.*`, `customhud.prestige.*` | Text on the designed plugin pages |

To add a language, copy `en.jsonc` to `<code>.jsonc`. Chat replies take
SwiftlyS2 colour tags (`[green]`, `[default]`); hints must not, because
Panorama would draw the control character. Core's own menu strings, such as an
input row's "type in chat" prompt, come from SwiftlyS2's translation files.

### Eyebrows

A submenu's eyebrow, the gold line over its title, is its root menu's title. A
menu that is its own root takes the first of these that exists:

1. `customhud.eyebrow.<plugin>`, where `<plugin>` is the first part of the
   menu's id: `missions` for `missions.main`. `DetailedLogging` logs each id as
   `applied … menu=…`.
2. The eyebrow of a plugin CustomHUD has pages for (`shop`, `maps`, `ranks`,
   `vip`, `admins`, `mix`).
3. `customhud.eyebrow.default`, for every other plugin.

Only the second ships, so the server shows no eyebrow over a plugin it has no
pages for until you add a key. One added to `en.jsonc` alone reaches players
of every language. An empty value (`""`) shows no line for that plugin, and
colour tags are dropped. An update replaces the translation files, so copy
your keys back into the new `en.jsonc`.

```jsonc
"customhud.eyebrow.default": "Rampage.lt",
"customhud.eyebrow.missions": "Missions",
"customhud.eyebrow.k4arenas": ""
```

## Theming

Every colour the shipped layouts draw comes from `customhud_tokens.vcss`, one
ladder per hue, so editing the accent's ladder re-themes every menu, page and
panel. Layouts are client-side: rebuild and publish the Workshop addon after a
change, or players keep the old look. See
[DesignLanguage.md](DesignLanguage.md#tokens) and
[PanoramaAuthoring.md](PanoramaAuthoring.md).

## Example Configuration

**Mouse by default, with variant layouts, views and hints:**

```jsonc
{
  "CustomHUD": {
    "DefaultNavMode": "mouse",
    "Hints": { "numbers": "1-6 pick · 7/8 page · 9 exit", "numbers.view": "9 exit" },
    "PageViews": {
      "mapchooser.eof-vote*": "maps-rows",
      "weaponskins.weapons.paintkits": "compact"
    },
    "Overrides": {
      "mixscrims.team-pick": { "File": "menu_teampick.vxml_c", "RowSlots": 10 },
      "shopcore.buy.items": { "File": "menu_shop.vxml_c" }
    }
  }
}
```

**Capture center and alert text:**

```jsonc
{
  "CustomHUD": {
    "NativeText": {
      "Enabled": true,
      "Center": { "Capture": true, "File": "hud_text.vxml_c", "DurationSeconds": 5 },
      "Alert": { "Capture": true, "File": "hud_alert.vxml_c" }
    }
  }
}
```

## Upgrading an Existing Config

SwiftlyS2 adds new keys to an existing `config.jsonc` but never changes a value
it already holds, and never adds keys inside `MouseLayouts` or `Overrides`.
That is why the built-in layouts live in code.

- A config written before the list shipped pins the classic layouts in `Default`
  and `Mouse`. Set both to `null` (or delete them) for the list, or keep them to
  stay on the classic look. CustomHUD logs once at load while either is set
  (`config.jsonc sets "Default" (menu.vxml_c) …`).
- A `MouseOverrides` section is no longer read. CustomHUD warns about it at load;
  move any rule you added yourself to `MouseLayouts`.
- Profiles in the file keep the values they were generated with. When an update
  changes a shipped layout's slot count, edit `RowSlots` to match
  `config.example.jsonc`, or delete the profile.

> [!IMPORTANT]
> A profile pointing at a layout the player's Workshop addon does not contain
> draws **nothing**, and the server cannot detect it. Keep profiles in step with
> the addon version you ship.

## Troubleshooting

| Problem | Possible Cause | Solution |
|---------|----------------|----------|
| Menus still draw as center-screen HTML | `menus.jsonc` does not name `customhud.panorama`, or the plugin uses `Core.MenusAPI` | Set `"Renderer"`. A `Core.MenusAPI` plugin must be ported to `Core.Menu` first |
| Every menu fails to open | `Renderer` names CustomHUD, but CustomHUD failed to load | Look for the `Renderer 'customhud.panorama' registered` line; scope the renderer to one menu id while testing |
| Screen blank, though `DetailedLogging` shows `applied slot=…` | The player lacks the Workshop addon, or a profile's `File` names a layout the addon does not ship | Serve the addon to every player; keep profiles in step with the addon version |
| The classic look instead of the list | `config.jsonc` from an older build pins `Default` / `Mouse` | Set both to `null`, see [Upgrading an Existing Config](#upgrading-an-existing-config) |
| Panel visible but empty, or text in the wrong places | Panel ids, dialog variables or `Row` patterns do not match the layout | Fix the profile's names; inspect the panel with the F6 Panorama debugger |
| A plugin's pages draw as plain rows | The plugin is missing, older than [the table](PluginIntegration.md#provider-integrations) needs, or publishes another contract | Read the `[CustomHUD]` log line naming it; update the plugin and CustomHUD together |
| Navigation mode or scale is forgotten | No Cookies plugin (preferences last until a restart), Cookies added after startup, or a LAN server with SteamID 0 | Install Cookies and restart the server: plugin exports are discovered at startup |
| Number keys do nothing | The player is not in `numbers` mode, or the keys are not bound | `!setnav numbers`, then bind keys to `sw_1`…`sw_9` |
| Log warns `Tab reached the menu router…` | Core closed a menu despite CustomHUD's Tab mask | [Report it](https://github.com/shmitzas/CustomHUD/issues/new?template=bug-report.yml) with your SwiftlyS2 version |
| A menu has no way out | The plugin unbound Close | Expected: the plugin requires a choice |

For a full pass over every surface, see the [testing guide](TestingGuide.md).
