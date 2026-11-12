# Configuration

Advanced settings for server owners. Nothing here is required: the defaults are
tuned for the best overall experience, and installation is covered in the
[README](../README.md#installation).

## Files

CustomHUD reads two files. Both reload live.

| File | Owner | Holds |
|------|-------|-------|
| `addons/swiftlys2/configs/menus/menus.jsonc` | SwiftlyS2 core | Renderer selection, key binds, page size, menu sounds |
| `addons/swiftlys2/configs/plugins/CustomHUD/config.jsonc` | CustomHUD | Where the start mode and page size come from, a layout name per plugin, message and panel layouts |

Every CustomHUD setting sits under a `"CustomHUD"` section.
[`config.example.jsonc`](../config.example.jsonc) is the file as CustomHUD
writes it on first start: SwiftlyS2 writes each setting's `[Description]` above
it, and back-fills a setting a newer build adds, but never a dictionary key. So
the file holds names, never layouts: those live in `BuiltInLayouts`, where a
release can improve them.

## Core Settings (`menus.jsonc`)

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `Menus.Renderer` | string | unset | `"customhud.panorama"` draws every menu with CustomHUD |
| `Menus.<menuId>.Renderer` | string | unset | The same for one menu id; wins over the server-wide value |
| `Menus.InputMethod` | string | unset | The navigation mode players start in until they run `!setnav`, while CustomHUD's `MenuConfig` is `"swiftlys2"`: core's `buttons` or `chat`, or CustomHUD's `wasd`, `numbers` or `mouse`, which core ignores. Unset or `chat` makes them `buttons` players, who type `!N` on core's Chat menus |
| `MenuLayout.MaxVisibleLines` | int | `5` | Page size in keyboard modes while `MenuConfig` is `"swiftlys2"`, clamped to `1`-`32`. Mouse layouts ignore it |

Key binds for `buttons` mode (default Shift / F / E / Tab) live in the same
file; see SwiftlyS2's
[menu configuration](https://swiftlys2.net/docs/development/menu-system#server-configuration).

## Quick Reference

Written into `config.jsonc`:

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `MenuConfig` | string | `"swiftlys2"` | Where the start mode and page size come from: `"swiftlys2"` = `menus.jsonc` above, `"customhud"` = the next two settings. Anything else reads `menus.jsonc` |
| `DefaultNavMode` | string | `"buttons"` | Mode for players who never ran `!setnav`, under `"customhud"`: `wasd`, `buttons`, `numbers` or `mouse`. Anything else means `buttons` |
| `MaxVisibleLines` | int | `5` | Page size in keyboard modes under `"customhud"`, clamped to `1`-`32` |
| `Menus.<Plugin>.Mouse`, `.Keyboard` | string | `menu_locker_v2`, `menu_list_v2`; WeaponSkins `menu_skins_mouse_v2`, `menu_list_skins_v2`; `Other` `menu_list_mouse_v2`, `menu_list_v2` | Layout per plugin and mode, by file name without `.vxml_c`; `_v2` is CS2's store look, the rest the first designs. See [Layout Resolution](#layout-resolution) |
| `Messages.Center`, `Alert`, `Notify` | string | `"game"` | Native HUD text capture, see [Native Text Capture](#native-text-capture) |
| `Panels.Versus`, `Ready`, `Timeout` | string | `panel_versus`, `panel_ready`, `panel_timeout` | Layout per panel for `CustomHUD.Panels.v1`, see [Panels](#panels) |
| `DetailedLogging` | bool | `false` | Log every compose, diff, apply and input decision with a `[CustomHUD]` prefix |

Read when added by hand, never written (`[JsonIgnore]`):

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `RendererId` | string | `"customhud.panorama"` | Id the renderer registers under, the value `menus.jsonc` names. Raw nodes addressed to `customhud.panorama` keep working after a rename. Restart to apply |
| `LayoutRoot` | string | `"panorama/layout/custom_game/customhud/"` | Prefix for every layout `File` without a `/` of its own |
| `Hints` | object | `{}` | Footer hint overrides, see [Hints](#hints) |
| `PageViews` | object | `{}` | Menu id or trailing-`*` prefix → view, see [Plugin Pages](PluginIntegration.md#plugin-pages-mouse) |
| `MenuOverrides` | object | `{}` | Menu id or trailing-`*` prefix → `{ "Mouse", "Keyboard" }` names; wins over `Menus` |
| `MenuLayouts`, `MessageLayouts`, `PanelLayouts` | object | `{}` | Layouts of the operator's own by name: a menu profile ([below](#layout-profile-settings)), a message profile or a panel profile |
| `Messages.Chat`, `ChatEndOfRound`, `Console` | string | `"game"` | Capture of chat and console text, rarely wanted |

A value that names nothing is logged at load and on every reload as
`[CustomHUD] config.jsonc: <what applies instead>` (`CustomHudConfig.Problems`),
and one the binder cannot convert keeps the last good config.

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

### Admin Commands

| Command | Aliases | Permission | Description |
|---------|---------|------------|-------------|
| `!ch_test center [text]` | - | `custom_hud` | Send yourself a centre message through SwiftlyS2's `SendCenter`, as any plugin would, and reply with what draws it: the layout `Messages.Center` names, or the game. Your own text replaces the sample. See [Native Text Capture](#native-text-capture) |
| `!ch_test alert [text]` | - | `custom_hud` | The same for an alert, through `SendAlert` and `Messages.Alert` |

## Navigation Modes

| Mode | Input | Handled by |
|------|-------|------------|
| `wasd` | W / S move, A / D change a value, E select, R or Tab close | CustomHUD |
| `buttons` | Whatever `menus.jsonc` binds (default Shift / F / E / Tab) | Core |
| `numbers` | `1`-`6` select, `7` / `8` page, `9` close; A / D change the slider or selector picked last | CustomHUD |
| `mouse` | Click. Footer `<` back, `×` close, `‹` / `›` page (only with more than one page); sliders and selectors step with their own `‹` `›` | CustomHUD |

- `buttons` is core's own name for keyboard input, the word `menus.jsonc` uses
  for `"InputMethod"`. Its keys are bound there, not in CustomHUD.
- Until a player runs `!setnav`, their mode comes from where `MenuConfig`
  points: `Menus.InputMethod` in `menus.jsonc` (unset is `buttons`), or
  `DefaultNavMode`. The log says which:
  `Players who have not run !setnav navigate in wasd mode`.
- In `wasd` mode only W, S, A, D, E, R and Tab work. Where the menu's own keymap
  binds one of them, core handles it and CustomHUD does not, so it never acts
  twice. On Buttons-input menus, core's binds on any other key (Shift and F at
  its defaults) are removed before core sees them; a key a menu binds to an
  action of its own keeps working. W and S still move the player as well: core
  has no input freeze.
- In `mouse` mode the cursor is captured per player while a menu is open.
- In `mouse` and `numbers` modes, on Buttons-input menus, CustomHUD removes
  core's menu binds before core sees them: Shift, F, A, D, E and Tab at its
  defaults. Neither mode draws core's cursor, so those keys would act on the row
  last clicked or picked, and E would activate it again. `numbers` keeps A and
  D: picking a selector by number does not change it, so they are how a numbers
  player does. A key a menu binds to an action of its own keeps working. Focus,
  chat input and submenus survive, with no exit sound or close callback; other
  players and renderers keep their normal binds.
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

- `MaxVisibleLines` lines, from `menus.jsonc`, or from `config.jsonc` under
  `"MenuConfig": "customhud"`,
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

Which profile draws a menu (`CustomHudConfig.Resolve`); the first name that is a
layout for that plugin and mode wins.

1. `MenuOverrides`: the menu id exactly, then the longest trailing-`*` prefix.
2. The plugin's entry in `Menus`, `Mouse` or `Keyboard` by the player's mode.
3. The plugin's default, `MenuChoices`' own value.

A name is looked up in `MenuLayouts` first, then in `BuiltInLayouts`, by the
file it names (`.vxml_c` optional, any case):

| Layout, and its first design | Mouse | Keyboard |
|------------------------------|-------|----------|
| `menu_locker_v2`, `menu_locker` | ShopCore, MapChooser, Ranks, VIPCore, Admins and MixScrims: their `LockerProfiles` profile (`admins.vote` the ballot) | none |
| `menu_skins_mouse_v2`, `menu_skins_mouse` | WeaponSkins: `WeaponSkinsLockerV2()`, `WeaponSkinsLocker()` | none |
| `menu_list_mouse_v2`, `menu_list_mouse` | `ListProfiles.Mouse()` | none |
| `menu_list_v2`, `menu_list_skins_v2`, `menu_list`, `menu_list_skins` | none | `ListProfiles.Plugins()` for the plugin, else `ListProfiles.Keys()`, in that file |
| `menu_mouse` | `MenuLayoutProfile.MouseGrid()` | none |
| `menu`, `menu_shop`, `menu_skins`, `menu_teampick` | none | The classic list in that file |

The plugin comes from the menu id: `weaponskins.`, `shopcore.`, `mapchooser.`,
`ranks.` (with `weaponstats.`, `customcombos.` and `prestige.`), `vipcore.`,
`admins.` and `mixscrims.`; anything else is `Other`. Every built-in profile is
built once, since `FrameComposer` fingerprints a profile per instance.

## Layout Profile Settings

A `MenuLayouts` entry points at one compiled layout and names what the renderer
writes into it. It is **not** merged with a built-in layout: unset fields take
the defaults below, so repeat `Panels` and `Classes` if your layout renames
them. The exception is an entry naming a `Base`: a restyled copy of a shipped
layout, which draws that layout's profile, per plugin and menu, in its own
`File` (`MenuLayoutProfile.WithFile`), every other field ignored.

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `File` | string | `"menu.vxml_c"` | Compiled layout, resolved against `LayoutRoot`. The `_c` suffix is required |
| `Base` | string | `""` | A shipped layout's name: draw its profile in this `File`, ignoring every other field. The copy must keep every panel id |
| `RowSlots` | int | `20` | Rows the layout declares, `1`-`256`. Must match the authored layout |
| `ShowAllRows` | bool | `false` | Draw the whole list in `RowSlots`-sized chunks that scroll, ignoring `MaxVisibleLines` and `ItemsPerPage` |
| `PageTiles` | bool | `false` | With `ShowAllRows` in Mouse mode, page a longer list through a `«` / `»` card at each end of the grid |
| `Panels.Shell`, `Body`, `Title`, `Footer` | string | `shell`, `options`, `title`, `footer` | Panel ids the renderer addresses |
| `Panels.Back`, `Previous`, `Next` | string | `nav_back`, `nav_previous`, `nav_next` | Mouse footer buttons; `""` = the layout has none |
| `Panels.Close` | string | `""` | A `×` that leaves every submenu level at once. With one, Back shows only where it has somewhere to go |
| `Variables.PagePanel`, `PageVar` | string | `page`, `page` | Page counter, written only while a menu has more than one page |
| `Variables.PageFormat` | string | `"{0} / {1}"` | Counter text: `{0}` = page, `{1}` = page count |
| `Variables.CrumbPanel`, `CrumbVar` | string | `title`, `""` | A row layout's crumb: the title of the page Back returns to. An empty `CrumbVar` writes nothing |
| `Variables.InfoPanel`, `InfoVar` | string | `title`, `""` | A row layout's one-line info: the header's `chip` entries, joined with ` · `. An empty `InfoVar` writes nothing |
| `Classes.ScaleSteps` | int[] | `[80, 90, 100, 110, 125, 150]` | Scale steps the layout styles. `!hudscale` offers those of the layout `Menus.Other.Keyboard` names |
| `Classes.Rewind` | string | `""`; `chud-rewind` in the built-in scrolling layouts | With `ShowAllRows`: a class on the shell for three ticks after a new list replaces the last, which the layout styles to shorten its scroll content so the offset clamps to the top. Empty: an empty list is shown for a tick instead. See [PanoramaAuthoring](PanoramaAuthoring.md#scrolling-grids) |
| `Sidebar` | object | `null` | Root-menu sidebar, see below |
| `Tabs` | object | `null` | A row layout's second level as tabs, see below. A `Page` profile has its own |
| `Card` | object | `null` | Inspect card for `hero` pages (`Slots` = `8` fields), see [Card Pages](PluginIntegration.md#card-pages). `SlotTiles` (`0`) draws a row's `slot=` entries as that many tiles under its field |
| `Spot` | object | `null` | A row layout's spotlight: the header entry marked `spot`, drawn over the list. See below |
| `Views` | object | `{}` | A row layout's views: menu id (exact, or a trailing-`*` prefix) → a name the shell carries as `Classes.View` (`view-{0}`). `PageViews` and a header's `view=` win |
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
| `Sidebar.Icons` | object | `{}` | Entry component id (else label) → icon token from `customhud_icons.vcss`. Unmapped entries keep the layout's default icon; `customhud_icons.vcss` lists the tokens |
| `Sidebar.CountVar`, `CountClass` | string | `""`, `"has-count"` | Splits a trailing count (`Knives (12)`) into a badge. An empty `CountVar` keeps the label whole |
| `Sidebar.DividerClass` | string | `"divider"` | Class on the slot of a root `DividerComponent` |
| `Sidebar.Omit` | array | `[]` | Root entries left out, by component id or label: pages the layout reaches another way. A divider that would end the list or follow another goes too. The default WeaponSkins locker omits the four properties pages, which open from its spotlight |
| `Sidebar.RootButton`, `ButtonPrefix`, `LabelVar`, `IconPanel`, `IconClass` | string | `nav_root`, `category_`, `category`, `category_{0}_icon`, `ico-{0}` | Layout addressing |

**Tabs** - in Mouse mode, the plain submenus of a sidebar entry's page drawn as
a strip over the list while the player is on one of them, the open one marked.
Only the pages `Hosts` names are hosts, so a page of item types (WeaponSkins'
knives) stays a grid. Both WeaponSkins lockers tab `weaponskins.weapons`; the
default one shows that page (`AutoOpen` `-1`), the first opens Rifles in its
place.

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `Tabs.Hosts` | array | `[]` | Menu ids of the pages whose submenus are tabs; a trailing `*` matches an id prefix |
| `Tabs.AutoOpen` | int | `0` | Tab a host opens in place of its own page; Back is hidden on its tabs. `-1` shows the page |
| `Tabs.Slots` | int | `8` | Tabs the layout declares |
| `Tabs.Panel`, `Prefix`, `LabelVar`, `CountVar` | string | `tabs`, `tab_`, `tab`, `tab_count` | Layout addressing; a trailing count (`Knives (12)`) goes to `CountVar` |
| `Tabs.HasTabsClass`, `HasCountClass` | string | `has-tabs`, `has-count` | On the shell while the strip shows, and on a tab with a count |

**Spot** - a row layout's spotlight: the item a page is about, from the header
entry marked `spot` (see [Richer Rows](PluginIntegration.md#richer-rows-optional-no-dependency)).
The default WeaponSkins locker draws the finish a weapon wears and the agent on
the player's side with it. Every variable is written on every frame, blank
while the page has no spot.

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `Spot.Panel`, `ThumbPanel` | string | `spot`, `spot_thumb` | The spotlight and the panel its `image=` class goes on |
| `Spot.NameVar`, `TypeVar`, `SubVar`, `NoteVar` | string | `spot_name`, `spot_type`, `spot_sub`, `spot_note` | The entry's text and its `type=`, `sub=` and `note=` |
| `Spot.FactSlots` | int | `4` | `fact=` rows the layout declares |
| `Spot.FactPanel`, `FactLabelVar`, `FactValueVar` | string | `spot_fact_{0}`, `spot_fact_{0}`, `spot_fact_{0}_value` | One fact row, `{0}` = its index |
| `Spot.ShownClass`, `HasSubClass`, `HasNoteClass` | string | `has-spot`, `has-sub`, `has-note` | On the shell while a spot is drawn; on the spotlight with a `sub=` or a `note=` |
| `Spot.LinkButton`, `LinkVar` | string | `spot_link`, `spot_link` | The Button a `link=` draws, and its label; hidden without one. Clicking it opens that root entry, then picks the entry its `pick` names there; Back from the page reached returns to the spotlight's page. Empty for a layout without the button |

Every field, with a comment on each, is in
[`src/Config/CustomHudConfig.cs`](../src/Config/CustomHudConfig.cs).

## Hints

`Hints`, added by hand, replaces a footer hint in every language, and `""`
hides it. Without an entry, hints come from the player's translation file
(`customhud.hint.<key>`).

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
[Panels API](PluginIntegration.md#panels-api), by panel name. `Panels` names the
layout each shipped panel draws in, `panel_versus`, `panel_ready` and `panel_timeout`
by default (`PanelProfile.Defaults()`); a panel it has no entry for draws in the
layout of its own name. A `PanelLayouts` entry is a whole profile, merged with
nothing, and one named like a shipped layout replaces it.

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `PanelLayouts.<name>.File` | string | `""` | Shipped: `panel_versus.vxml_c`, `panel_ready.vxml_c`, `panel_timeout.vxml_c`. A panel without one cannot be shown |
| `PanelLayouts.<name>.BlankVariables` | array | `[]` | Dialog variables set to `""` on spawn |
| `PanelLayouts.<name>.DurationSeconds` | float | `0` | Used when the caller passes none; `<= 0` stays up until hidden |
| `PanelLayouts.<name>.ShellPanel` | string | `"shell"` | Root panel |
| `PanelLayouts.<name>.HiddenClass` | string | `"hidden"` | Class that hides the shell |
| `PanelLayouts.<name>.VariablePanel` | string | `"shell"` | Panel the dialog variables are set on |

## Native Text Capture

Takes over the game's own HUD text, so messages from plugins that know nothing
about CustomHUD still render in your style. It covers every
`CUserMessageTextMsg` destination, which is what `SendCenter`, `SendAlert`,
`SendChat` and `SendMessage` emit.

`Messages` names a layout per destination: `"game"` (the default) passes it
through, `"hud_text"` draws `hud_text.vxml_c` for 5 s, `"hud_alert"` `hud_alert.vxml_c`
for 4 s, `"hud_text_v2"` and `"hud_alert_v2"` the same two in CS2's store look
(`NativeTextProfile.Defaults()`), and any other name a `MessageLayouts`
entry. The hook is installed only while a destination is captured, on the game
thread at load and after each reload (`NativeTextCapture.Sync`), so no restart
is needed.

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `Messages.Center`, `Alert`, `Notify` | string | `"game"` | Destinations 4, 6 and 1 |
| `Messages.Chat`, `ChatEndOfRound`, `Console` | string | `"game"` | Destinations 3, 5 and 2, added by hand. Capturing these is rarely wanted |

Each `MessageLayouts` entry:

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `File` | string | `"hud_text.vxml_c"` | Layout |
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
| `customhud.test.*` | `!ch_test` replies, and the sample centre message and alert it sends without text of its own (`.center`, `.alert`) |
| `customhud.hint.<key>` | Footer hints, one per [Hints](#hints) key |
| `customhud.legend.<verb>` | Words in the list's key legend (`move`, `select`, `pick`, `page`, ...) |
| `customhud.eyebrow.<plugin>` | Plugin names over a page's title; see [Eyebrows](#eyebrows) |
| `customhud.lead`, `customhud.maps.*`, `customhud.ranks.*`, `customhud.shop.*`, `customhud.vip.*`, `customhud.weaponskins.*`, `customhud.weaponstats.*`, `customhud.prestige.*` | Text on the designed plugin pages |

To add a language, copy `en.jsonc` to `<code>.jsonc`. Chat replies take
SwiftlyS2 colour tags (`[green]`, `[default]`); hints and `!ch_test`'s samples
must not, because Panorama would draw the control character. Core's own menu strings, such as an
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
"customhud.eyebrow.default": "My Server",
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

**Mouse by default from CustomHUD's own settings, the classic list for other
plugins' keyboard menus, variant layouts, captured text, views and hints:**

```jsonc
{
  "CustomHUD": {
    "MenuConfig": "customhud",
    "DefaultNavMode": "mouse",
    "Menus": {
      "Other": { "Mouse": "menu_list_mouse_v2", "Keyboard": "menu" }
    },
    "Messages": { "Center": "hud_text", "Alert": "hud_alert" },
    "MenuOverrides": {
      "mixscrims.team-pick": { "Keyboard": "menu_teampick" },
      "shopcore.buy.items": { "Keyboard": "menu_shop" }
    },
    "PageViews": {
      "mapchooser.eof-vote*": "maps-rows",
      "weaponskins.weapons.paintkits": "compact"
    },
    "Hints": { "numbers": "1-6 pick · 7/8 page · 9 exit", "numbers.view": "9 exit" }
  }
}
```

## Upgrading an Existing Config

SwiftlyS2 adds new keys to an existing `config.jsonc`, but never changes or
removes a value it already holds, and never adds keys inside a dictionary.
That is why the file holds names, and the layouts they stand for live in code.

- A file from before layout names keeps `Default`, `Wasd`, `Buttons`,
  `Numbers`, `Mouse`, `Overrides`, `MouseLayouts`, `MouseOverrides`,
  `NativeText`, `Design` and a `Panels` of whole profiles, none of which is
  read: the `_v2` in a layout's name now picks its design. The new
  `MenuConfig`, `MaxVisibleLines`, `Menus` and `Messages` are added below them;
  `Panels` cannot be while the old one holds profiles, so the shipped panels
  draw in their own layouts. CustomHUD warns at load (`config.jsonc still has …
  from an older CustomHUD …`), and again when `NativeText.Enabled` was on:
  capture is off until `Messages` names a layout. Deleting the file and
  restarting writes a fresh one.
- `DefaultNavMode` applies only under `"MenuConfig": "customhud"`. A file that
  set it while `menus.jsonc` named no `Menus.InputMethod` now starts players in
  `buttons` mode, and the load warns.
- `Hints`, `PageViews`, `RendererId` and `LayoutRoot` keep working where an older
  file wrote them.

> [!IMPORTANT]
> A layout of your own pointing at a file the player's Workshop addon does not
> contain draws **nothing**, and the server cannot detect it. Keep `MenuLayouts`,
> `MessageLayouts` and `PanelLayouts` in step with the addon version you ship.

## Troubleshooting

| Problem | Possible Cause | Solution |
|---------|----------------|----------|
| Menus still draw as center-screen HTML | `menus.jsonc` does not name `customhud.panorama`, or the plugin uses `Core.MenusAPI` | Set `"Renderer"`. A `Core.MenusAPI` plugin must be ported to `Core.Menu` first |
| Every menu fails to open | `Renderer` names CustomHUD, but CustomHUD failed to load | Look for the `Renderer 'customhud.panorama' registered` line; scope the renderer to one menu id while testing |
| Screen blank, though `DetailedLogging` shows `applied slot=…` | The player lacks the Workshop addon, or a layout of your own names a file the addon does not ship | Serve the addon to every player; keep your layouts in step with the addon version |
| Settings seem ignored; the load warns `config.jsonc still has … from an older CustomHUD` | The file predates layout names | Delete it and restart, see [Upgrading an Existing Config](#upgrading-an-existing-config) |
| A layout name does nothing; the log says `config.jsonc: … is no … layout …` | The name is no layout for that plugin and mode, such as a keyboard `menu_locker_v2` | Pick one from [Layout Resolution](#layout-resolution) |
| Panel visible but empty, or text in the wrong places | Panel ids, dialog variables or `Row` patterns do not match the layout | Fix the names in your layout's profile; inspect the panel with the F6 Panorama debugger |
| A plugin's pages draw as plain rows | The plugin is missing, older than [the table](PluginIntegration.md#provider-integrations) needs, or publishes another contract | Read the `[CustomHUD]` log line naming it; update the plugin and CustomHUD together |
| Navigation mode or scale is forgotten | No Cookies plugin (preferences last until a restart), Cookies added after startup, or a LAN server with SteamID 0 | Install Cookies and restart the server: plugin exports are discovered at startup |
| Number keys do nothing | The player is not in `numbers` mode, or the keys are not bound | `!setnav numbers`, then bind keys to `sw_1`…`sw_9` |
| Log warns `<key> reached the menu router…` | Core acted on a key despite CustomHUD's mask: closed the menu, moved its cursor or activated a row | [Report it](https://github.com/shmitzas/CustomHUD/issues/new?template=bug-report.yml) with your SwiftlyS2 version |
| A menu has no way out | The plugin unbound Close | Expected: the plugin requires a choice |

For a full pass over every surface, see the [testing guide](TestingGuide.md).
