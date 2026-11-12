# Testing Every CustomHUD Surface

A full pass over everything the plugin can draw, in the order that finds
problems earliest.

CustomHUD is a **renderer** — it draws what `Core.Menu` hands it and opens no
menus of its own — so every menu check here is driven by an installed plugin. The
supported plugins exercise their own layouts; any other plugin (`Other` in
`Menus`) exercises the list (`menu_list_v2`, and `menu_list_mouse_v2` in mouse mode).
Every check reads the default design, `v2`; [§ Design v1](#design-v1) repeats
the pass in the first designs.

## 0. One-time setup

### Install the test plugins

| Plugin | Build | Open with | Exercises |
|---|---|---|---|
| WeaponSkins | the fork | `!ws` | submenus, long lists, inputs, a toggle; the WeaponSkins locker |
| MapDecals | a `Core.Menu` build | `!mapdecal` | no CustomHUD rule: buttons, a toggle, sliders, an input, a submenu; an alert |
| Ranks | the fork | `!rank`, `!stats` | a header divider, a toggle, and Ranks' own pages (a hero card, the rank road, tiles, a podium) |
| ShopCore | the fork | `!shop` | prices in the value column, disabled rows |
| MapChooser | the fork, `feat/migrate_to_new_menu_api` | `!maps`, `!mapsvote`, `!nominate`, `!revote` | the map menu as a sidebar, the live tally, disabled rows with a reason, map art |
| VIPCore | the fork, `feat/migrate_to_new_menu_api` | `!vip` | disabled rows |
| MixScrims | `MixScrims-SwiftlyS2` | its match flow, `!captain` | menus with no exit, Yes/No ballots, the `mix-*` pages, centre-HTML |
| Admins | the fork, `feat/complete_refactor` | `!admin`, `!vote`, `!votekick`, `!votemap`, `!csay` | the `admins-*` pages drawn from its API, the ballot, centre text |

Each fork's upstream is on the wiki's Supported Plugins page. Only `Core.Menu`
reaches a renderer: the default branches of the MapChooser and VIPCore forks
still use the deprecated `Core.MenusAPI`, which keeps drawing core's
centre-HTML whatever `menus.jsonc` says. `!mapdecal`, `!mapsvote`, `!admin`,
`!vote`, `!votekick`, `!votemap` and `!csay` are admin commands, and so is
CustomHUD's own `!ch_test` (`custom_hud`); grant yourself their permissions.

### Turn on detailed logging

`configs/plugins/CustomHUD/config.jsonc`:

```jsonc
{ "CustomHUD": { "DetailedLogging": true } }
```

### Point menus at the renderer

`configs/menus/menus.jsonc`:

```jsonc
{
  "Menus": { "Renderer": "customhud.panorama" },
  "MenuLayout": { "MaxVisibleLines": 20 }
}
```

**`MaxVisibleLines` defaults to `5`.** It is the page size: under
`"MenuConfig": "swiftlys2"`, the default, CustomHUD reads it from this file and
splits every keyboard menu into fixed pages of at most that
many lines (and the menu's `ItemsPerPage`). It is clamped to `1..32`, and
edits apply live. Mouse mode ignores it: its layouts draw a whole chunk of
rows and scroll.

### Start the compile loop and the server

```powershell
.\scripts\watch-panorama.ps1        # compiles on save into the customhud addon + local client
```

Then start a listen server with the addon mounted:

```
cs2.exe -addon customhud -tools -insecure +sv_lan 1 +map de_dust2
```

The addon has to exist first. Create it once in Workshop Tools; see
[PanoramaAuthoring.md](PanoramaAuthoring.md#the-authoring-loop).

### Confirm the renderer registered

On load the plugin logs:

```
[CustomHUD] Renderer 'customhud.panorama' registered. Select it with "Renderer": "customhud.panorama" in configs/menus/menus.jsonc.
```

No line means no renderer, and **every menu on the server will fail to build**
while `menus.jsonc` names it. Fix that before anything else.

A `config.jsonc` written by a build from before layout names keeps its old
keys, which this build does not read. The plugin then logs, once at load:

```
[CustomHUD] config.jsonc still has Buttons, Default, Design, Mouse, MouseLayouts, NativeText, Numbers, Overrides, Wasd, Panels from an older CustomHUD, which this version does not read. Layouts are now picked by name in Menus, Messages and Panels: delete config.jsonc and restart for a fresh one, then set again what you had changed.
```

Delete the file and restart before checking any layout below, so every check
runs against the defaults. With
`DetailedLogging: true`, each `applied slot=…` line also names the `menu=`
id and the `layout=` the menu was drawn in.

## 1. Does anything draw at all

```
!rank
```

Ranks' menu should appear in CustomHUD's layout. If it does not, work down this
list — the order matters, because each step rules out the one below it:

| Symptom | Likely cause |
|---|---|
| The menu draws as core's centre-HTML | `menus.jsonc` does not name `customhud.panorama`, or the plugin is still on `Core.MenusAPI` |
| An "Error parsing layout and style files" dialog | a stylesheet failed to parse and the game dropped **all** of it; the dialog names the file and line. `resourcecompiler` does not catch these (see [PanoramaAuthoring.md](PanoramaAuthoring.md#why-panorama-silently-eats-mistakes)) |
| The classic look, though the list ships | the server runs a build from before the list, or its `config.jsonc` names `"menu"` or `"menu_mouse"` in `Menus` or `MenuOverrides`. The layout is chosen on the server; the client's files only draw it |
| Screen blank, though the log shows `applied slot=…` | layout not compiled into `game\csgo_addons\customhud`, game launched without `-addon customhud` and run with `-SkipClient`, a stale published VPK shadowing the loose files, a stale file in `game\csgo\panorama\*\customhud` left by another tool (the script's client-orphan warning lists them), or a `MenuLayouts` entry's `File` names a layout the addon does not ship |
| Panel visible, no text | panel ids / dialog variables in the profile do not match the layout |
| Panel visible, text in wrong places | `Row.PanelId` / `LabelVar` pattern mismatch |
| Rows all stacked on each other | `flow-children` not set — Panorama's default is `none` |

Press **F6** and hover the panel. The debugger gives you the real panel tree
and the computed value for every property, which settles the last four in
seconds.

## 2. Every component type

No plugin sends every component, so this takes two:

```
!rank        # Ranks; Settings adds a toggle
!mapdecal    # MapDecals: Place New Decal once, then Edit Existing Decal → it
```

MapDecals' edit page holds buttons, an `Enabled` toggle, sliders, a
`Decal name` input and a `Position & rotation` submenu of six more sliders.

Check:

- [ ] **Header** — `Ranks`, or MapDecals' `Editing …` — becomes the title
      above the body and stays put while the page turns
- [ ] a plugin's root menu with no eyebrow (MapDecals, K4-Arenas) starts at
      its title, with no empty band over it; its submenus show the path above
      the title
- [ ] with `"customhud.eyebrow.default": "Server"` added to CustomHUD's
      `en.jsonc`, every such root shows `SERVER` over its title, and one with
      its own key (`customhud.eyebrow.k4arenas`) shows that instead; `""`
      hides it again for that plugin
- [ ] **Footer** sits below as the legend: a keycap or two and a word for
      each thing the focused row answers to, in your nav mode's keys and your
      language (`customhud.legend.*`), never core's key list. A `Hints` entry
      for the mode shows that line instead (`customhud.hint.*`)
- [ ] **Title** has no trailing `---` / `───` rule, even though every Ranks
      menu adds a header divider
- [ ] Value components (MapDecals' toggle and sliders, Ranks' toggle) render
      their value, not just their label
- [ ] a submenu (`My rank`, `Position & rotation`) opens, and closing it
      returns to the menu it came from
- [ ] **Input** (`Decal name`), selected with E in wasd, a click or its
      number, prints a "type in chat" prompt, never a raw key such as
      `en.menu.input.chat_hint`
- [ ] each control ends its row: the toggle in a switch, light blue while on;
      each slider in its track and value; the input in a field with a pen; a
      submenu in `›`. In mouse mode a click on the switch flips it, a click on
      a slider's track sets it there, and its `‹` `›` step it one step

Toggle and slider are the ones a layout usually gets wrong: in the classic
layouts they render label *and* value into the same row text, so a layout with
no right-hand slot must not clip them.

No test plugin sends a Choice, Selector, Spacer, ProgressBar or ChatColoredText
row to CustomHUD (Ranks draws My rank's progress as its hero card instead);
those stay unchecked until one does.

## 3. Row states

This is the stylesheet's real test — every visual state a row can be in. The
test plugins send most of them:

- [ ] plain (any button)
- [ ] value in the right-hand slot (tab-separated): ShopCore's prices,
      MapDecals' sliders
- [ ] disabled: ShopCore's credits line on `!shop`, a VIPCore perk your group
      has no access to, MapChooser's `START VOTE` on `!mapsvote` before you
      pick a map
- [ ] truncated (ellipsis overflow): cut at the row's right edge, not after
      core's ~26 characters
- [ ] the selection highlight, moved across all of the above

Danger, a value with an icon badge, a badge from a raw node and marquee need a
plugin that sends them (`danger`, two tabs, `icon=`, a scrolling `Overflow`);
none of the test plugins does, so they stay unchecked.

Watch for state collisions: a row that is both selected and disabled must stay
legible.

The look itself:

- [ ] the title is a bold, uppercase, white heading with no `//` before it; a
      title too long for the panel ends in an ellipsis at its right edge
- [ ] the panel is CS2's navy in a hairline, with no orange anywhere; a dark
      band over the title names the plugin (the root's title, or an
      operator's `customhud.eyebrow.<plugin>`) and, a level down, the parent
      after a rule; another dark band at the foot holds the keys and the page
- [ ] values read in gold; a disabled row's name and art fade, while a pill
      on it saying why stays readable
- [ ] a long label (a WeaponSkins sticker's full name, say) runs to the row's
      right edge before any ellipsis; core's own cut at about 26 characters
      never shows
- [ ] the selected row has a 2px light-blue edge and light blue light fading
      from it, with a light-blue key and a bold name
- [ ] on a bright map (a snow or daylight map) the panel stays dark with the
      map faintly visible through it, and every label stays readable

> Marquee is driven by a CSS class, not by the server. If a scrolling row does
> not animate, the layout is missing the `marquee` class — the server is not
> going to re-send per frame to fake it.

## 4. Pages and panel height

A page is at most `MaxVisibleLines` rows (and the menu's `ItemsPerPage`).
WeaponSkins sets no page size of its own, so its AK-47 finishes (`!ws` →
Weapon Skins → AK-47, 66 rows) are the long list here, and `!mapdecal` the
short one. Test with `MaxVisibleLines` at `5`, then at `20`.

- [ ] at `MaxVisibleLines 5`: rows 1–5 with `1/N` and a dot per page in the
      footer; moving down from row 5 shows rows **6–10** (`2/N`, the second
      dot lit) with row 6 selected — the whole page turns, it does not slide
      one row
- [ ] the last page shows only the rows left and the panel shrinks to them;
      moving up from its first row shows the page before with its last row
      selected
- [ ] down from the last row wraps to page 1, up from row 1 to the last page
- [ ] `!mapdecal` shows no counter; at `MaxVisibleLines 20` the AK-47 list
      turns in pages of 20
- [ ] edit `MaxVisibleLines` in `menus.jsonc` with the menu open: the next
      redraw (move the cursor) uses the new page size
- [ ] the panel's height follows the page, and a short page leaves no empty
      rows visible
- [ ] **numbers**: at `MaxVisibleLines 20` the AK-47 list turns in pages of
      six; `8` shows rows 7–12 keyed `1`–`6`, `8` on the last page wraps to
      page 1, and `7` on page 1 opens the last page
- [ ] **numbers**: a disabled row (ShopCore's credits line on `!shop`) shows
      no key and takes none, so the next row is keyed one after the row
      before it, and the legend's `1-N` counts only the keyed rows
- [ ] a Chat-input menu (`"InputMethod": "chat"` in `menus.jsonc`) keeps
      core's own scrolling window and shows no counter — its `!N` numbers
      must still pick the row they label

These checks are for the keyboard modes. In mouse mode WeaponSkins draws its
own locker (§11), the plugins with pages theirs (§8), and every other plugin
(MapDecals, under `Other`) the mouse list: ten rows show, the rest scroll, and a menu
past 24 rows pages with `‹` `›` in the foot band. Back (`<`) leads the top band
on a page under another, and `×` ends the title's line. The row under the
pointer looks like the keyboard cursor's row (the light-blue edge and light,
a bold name) on a row a click acts on; a plain text line, a meter and a
heading stay still. With `Menus.Other.Mouse` set to `"menu_mouse"`, the tile
grid, the same menu draws three tiles to a line,
every line but the last full, and a list longer than its 60 tiles pages with
`>>`.

## 5. Navigation modes

Mouse and the keyboard modes can draw in different layouts, and every mode
has its own keys, so each needs its own pass.

```
!setnav buttons     # keyboard, core-dispatched (menus.jsonc binds)
!setnav wasd        # W/S move, A/D change, E select, R or Tab close; Shift and F do nothing
!setnav numbers     # 1-6 select, 7/8 page, 9 close, A/D change a picked slider; Shift, F, E and Tab do nothing
!setnav mouse       # click; Shift, F, A, D, E and Tab do nothing
```

After each, reopen `!rank` and MapDecals' edit page. The mode applies to every
menu you open, including ones a plugin opened with its own input method.

- [ ] with `"Menus": { "InputMethod": "wasd" }` in `menus.jsonc`, the log reads
      `Players who have not run !setnav navigate in wasd mode`, and a player
      who never ran `!setnav` opens menus in wasd (`!setnav` with no argument
      says `wasd`); after `!setnav buttons` they stay in buttons. With no
      `InputMethod`, they start in buttons
- [ ] with `"MenuConfig": "customhud"`, `"DefaultNavMode": "mouse"` and
      `"MaxVisibleLines": 8` in `config.jsonc`, the log reads `Players who have
      not run !setnav navigate in mouse mode, and menu pages hold up to 8 lines`;
      a player who never ran `!setnav` opens menus in mouse mode, and after
      `!setnav buttons` the AK-47 list turns in pages of 8 whatever `menus.jsonc` sets
- [ ] **buttons** — selection follows the keys bound in `menus.jsonc`; the
      legend's keycaps name those actual binds
- [ ] **wasd** — W/S move the selection exactly one row per press (not two),
      E selects, R closes; the legend reads `W` `S` Move · `E` Select ·
      `R` Close, and `R` Back in a submenu
- [ ] on a slider or selector row the legend's middle group becomes `A` `D`
      Change (the Left / Right binds in buttons mode), and on a toggle `E`
      Switch
- [ ] **wasd + core's keys** — Shift and F (core's NavigateUp / NavigateDown)
      leave the selection where it is, with no scroll sound, on a root menu and
      in a submenu; Shift still walks and F still inspects the weapon. A second
      player in buttons mode still moves with Shift and F
- [ ] **numbers** — every row shows the key that selects it, numbering counts
      only *selectable* rows (`My rank`'s progress bar must not consume a
      number), and 1–6 activate the right rows on page 2 as well as page 1;
      the legend reads `1-6` and the rows' word, `7` `8` Page on a paged
      menu, then `9` Close
- [ ] **mouse** — a cursor appears while the menu is open, and clicking a row
      activates **that** row; clicking a text row (`My rank`'s `Rank: …`)
      does nothing. The list has no keys and no legend: `<` returns from a
      submenu and `×` closes the menu from any depth
- [ ] **mouse + Tab** — press, hold and release Tab: the same session stays
      open on the same row, with no flicker, exit sound or close/open callbacks.
      Repeat inside a submenu (`Position & rotation`) and while `Decal name`
      is awaiting chat
- [ ] **numbers + Tab** — the same: Tab never closes the menu, and `9` still does
- [ ] **mouse + core's keys** — on MapDecals' edit page, click `Enabled`, then
      press E: it does not flip back, and nothing plays. Click a slider's
      track, then press A and D: its value stays. Shift and F move nothing.
      In game, E still uses a door and A and D still strafe
- [ ] **numbers + core's keys** — the same page: pick `Enabled` by its number,
      then press E: it does not flip back; Shift and F move nothing. Pick a
      slider by its number: it steps once, and A and D then step it further
- [ ] **mouse + Tab isolation** — a second player in buttons mode can still
      close with Tab; a menu using a different renderer is also unchanged
- [ ] the server log shows no `reached the menu router` warning after any of
      the key checks above
- [ ] **mouse → buttons** with the menu open — the cursor disappears and the
      keys move the selection again; closing the menu also releases the cursor
- [ ] **mouse + respawn** with the menu open — the cursor comes back with the
      menu
- [ ] switching mode live swaps the layout without needing a reopen
- [ ] unload CustomHUD, then open a CenterHTML menu: Tab closes normally

No cursor in mouse mode means input capture never reached the entity. Clicks
that do nothing while the cursor *is* visible mean the row is a `Panel` rather
than a `<Button>`, since only buttons report `OnCustomHudClicked`. Check this
with `Menus.Other.Mouse` set to `"menu_mouse"`, the tile grid, as well.

The mouse check is the one worth doing carefully. Body rows include
non-focusable entries, so a layout that renders headings or progress bars as
clickable panels will send clicks that map to the wrong component. Ranks'
Settings is the page for it: a click on `Reset…`, after the reset card's own
text, must open the confirmation, and a click on the switch must flip
`XP messages`. Its sidebar's `Help` (a root button, not a page) must list the
commands in chat and close the menu.

### Language

Switch to Lithuanian: your game language while `core.UsePlayerLanguage` is on,
otherwise `core.Language` in SwiftlyS2's `core.jsonc`.

- [ ] `!setnav`, `!hudscale` and `!ch_test` reply in Lithuanian after a green
      `[CustomHUD]` prefix, with real diacritics (`nustatyta į`) and no raw
      `customhud.` key; `!ch_test center`'s sample reads `CustomHUD bandymas:
      pranešimas ekrano centre`
- [ ] every keyboard mode's legend is Lithuanian (`Judėti`, `Pasirinkti`,
      `Uždaryti`) and fits on one line, including numbers mode on a paged
      menu (`Puslapis`)
- [ ] a `Hints` entry in `config.jsonc` replaces that mode's legend with its
      line in every language; an entry still holding the English default does
      not

## 6. UI scale

```
!hudscale 80
!hudscale 100
!hudscale 150
```

- [ ] each step applies and nothing clips at 150
- [ ] an unlisted value snaps to the nearest configured step

Scale is a class ladder (`hk-scale-{n}`), so a step with no CSS rule silently
does nothing — check all six.

## 7. Native HUD text

Pick a layout for two kinds first:

```jsonc
"Messages": {
  "Center": "hud_text",
  "Alert": "hud_alert"
}
```

Then send each kind with `!ch_test`. It sends through SwiftlyS2's own
`SendCenter` and `SendAlert`, the path any plugin's text takes, and replies
with what draws it under the current `Messages`:

```
!ch_test center          # a sample centre message
!ch_test alert           # a sample alert
!ch_test center <text>   # your own text instead of the sample; alert too
```

The other kinds come from plugins:

- **chat** — any plugin's chat reply, `!ch_test`'s and `!setnav`'s included;
  left uncaptured here, it is the control
- **centre-HTML** — MixScrims' warmup ready list (`ShowReadyStatusInCenterHtml`)
  or its vote-kick progress

`!csay <text>` (Admins) and MapDecals' ping hint (`!mapdecal` → Place New
Decal → a material) send a centre message and an alert from a real plugin, if
you want to repeat a check with one. None of the test plugins sends notify or
console text.

- [ ] `!ch_test center` replies `Messages.Center is hud_text, so CustomHUD
      draws it.`, `!ch_test alert` the same with `Alert` and `hud_alert`, and
      each draws in that layout while the native HUD stays empty. With
      `DetailedLogging`, each logs `captured native text slot=… kind=Center`
      (or `Alert`)
- [ ] uncaptured kinds (chat) still render natively, unchanged
- [ ] text auto-hides after its layout's time: 5 s for `hud_text` and
      `hud_text_v2`, 4 s for `hud_alert` and `hud_alert_v2`
- [ ] a second message replaces the first rather than stacking
      (`!ch_test center` twice)
- [ ] the setting applies on save, with no restart: `"Center": "game"` and the
      next `!ch_test center` replies that the game draws it, and the game's
      own centre text shows; `"hud_text"` and it is captured again
- [ ] a name that is no layout, `"Center": "hud_txet"`, replies
      `Messages.Center "hud_txet" names no layout, so the game draws it.`, and
      the game's own centre text shows
- [ ] a `MessageLayouts` layout with `"Suppress": false` shows **both** —
      useful while authoring
- [ ] `"Center": "hud_text_v2"` and `"Alert": "hud_alert_v2"` draw the same
      boxes in the same places in CS2's store look, with no orange anywhere: a
      hairline round the navy on every side; the centre message's toast with a
      2px light blue (selectedNavColor) left edge and cream text, the alert
      with red edges top and bottom and red capitals; no side of either frame
      thicker than the hairline or in its edge's colour
- [ ] with `"Center": "hud_alert_v2"`, `!ch_test center` draws in the alert
      with light blue edges
- [ ] a long Lithuanian line wraps inside both `_v2` boxes, its letters whole
      and the edge running the full height: `!ch_test center Dėmesio, komandos
      draugai: priešininkai užėmė šiaurės tašką, būtina grįžti į gynybą ir
      išlaikyti pozicijas iki raundo pabaigos`, then the same with `alert`. If
      chat cuts the line short, send it from the console instead:
      `sw_ch_test center "Dėmesio, …"`

**Centre-HTML is never captured, by design.** It is not a `TextMsg`; it is
buffered and re-sent every tick as a game event. Seeing MixScrims' ready list
render natively while `!ch_test center` renders in CustomHUD is the expected
result, not a bug — see [PublicPort.md](PublicPort.md) §6.

## 8. Variants, plugin pages and panels

### Variant layouts

`menu_teampick`, `menu_shop` and `menu_skins` are opt-in keyboard layouts:
point a menu id at one by name
through `MenuOverrides` in `config.jsonc`. MapDecals' edit page
stands in for a skins menu, since it carries a toggle, sliders and an input:

```jsonc
"MenuOverrides": {
  "mixscrims.team-pick": { "Keyboard": "menu_teampick" },
  "shopcore.buy.items":  { "Keyboard": "menu_shop" },
  "mapdecals.edit":      { "Keyboard": "menu_skins" }
}
```

They are keyboard layouts, so check them in a keyboard mode.

- [ ] team pick: MixScrims' captain sees the players in `menu_teampick`; it
      sends names only, so the rows carry no FACEIT badge or ELO
- [ ] shop: every item's price right-aligned
- [ ] team pick and shop: the left edge is gold (these are first-design
      variants, opted into per menu)
- [ ] skins: MapDecals' toggle, slider and input rows render in the variant's
      look
- [ ] the title is the same bold, uppercase, white heading in all three
- [ ] without the overrides each falls back to the list
- [ ] with `Menus.Other` set to `"menu_mouse"` for `Mouse` and `"menu"` for `Keyboard`,
      every other plugin's keyboard menus draw in the classic `menu.vxml` and
      its mouse menus as tiles; set them back to `"menu_list_mouse_v2"` and
      `"menu_list_v2"` and the lists return with no reconnect

### Plugin pages

```
!setnav mouse
!shop
!rank
!vip
!mapsvote
!admin
```

Their `"menu_locker_v2"`, the default in `Menus`, sends `shopcore.*`, `mapchooser.*`,
`ranks.*` (with Ranks'
modules `weaponstats.*`, `customcombos.*` and `prestige.*`), `vipcore.*`,
`admins.*` and `mixscrims.*` to `menu_locker_v2`, each id in the view
`LockerProfiles` maps it to; `PageViews` in `config.jsonc` picks another
(`maps-rows`, `shop-list`). Ranks and its modules, ShopCore, MapChooser,
WeaponSkins, VIPCore and Admins pages are drawn from those plugins' APIs
([Provider Integrations](PluginIntegration.md#provider-integrations)); the server log names each integration CustomHUD bound
at startup, and a missing one leaves that plugin's plain rows. MixScrims sends
plain rows today, so a page shows its labels, values and states in its view's
frame. MixScrims' pages are checked with its match flow and Admins' with its
own pass (§11).

On every page:

- [ ] a root of submenus is skipped and its first entry opens (`!shop` on
      Buy, `!rank` on My rank); every sidebar entry has an icon, and the rail
      is headed by the root's title in white capitals on the bar's band
- [ ] the frame is CS2's store look, with no orange anywhere: a hairline round
      the navy, the open rail entry and the open tab in light blue
      (selectedNavColor) with a 2px edge on the entry, a dark bar with `<`,
      the parent's title and a rule, the title, the chips and a plain `×`, and
      a dark band at the foot holding the page's hint
- [ ] MapChooser's vote, which opens with no root, folds the sidebar away and
      shows `MAP VOTE` at the head of the bar, before its title
- [ ] a page under a tab shows its parent's title before its own; the tab
      strip shows only on a sidebar entry's own submenus, so a page one level
      deeper (a shop product) drops it
- [ ] no `{s:…}` placeholder anywhere, and no part of one page left on the next
- [ ] the cards fill each line evenly, with no lone card wrapping onto a line
      of its own; a wrap there means the line ran exactly full

Per plugin:

- [ ] Shop: Buy's categories are tabs, a category's items are cards with
      their name and price, and an item opens its product page
- [ ] a category with more cards than fit (Smoke Colors' nine) scrolls to its
      last card with the mouse wheel and by dragging its scrollbar; so does any
      other locker page or detail pane that overflows
- [ ] Maps: on `!mapsvote` pick a few maps (a workshop one too, if the pool
      has one) and start the vote. Stock maps draw over their screenshot and
      logo, a workshop map as a striped card in its prefix's colour with its
      prefix large (`awp_…` as `AWP`), and Extend, when offered, in orange
      with a glow behind its icon. Under each map's name sits a small boxed
      label with its kind: `Stock map`, or `Workshop map` in blue
- [ ] each map card carries its share (`45%`), its votes and a meter; the
      leader reads `Leading` with a trophy, in gold with a gold meter, and your
      pick has a light-blue outline and a `Your vote` tag
- [ ] `!nominate` lists the pool as map cards with the same art
- [ ] Ranks: each page draws as designed from Ranks' API, in the art its
      `Scoreboard.RankMode` gives the scoreboard (a CS Rating badge in Premier,
      the skill-group emblem otherwise). My rank (`ranks-rank`): the hero card
      with the way to the next rank above the rank road, your rank marked `You`
      and the ones above it locked; the road's heading and its count (`7 ranks`)
      share one line, the count inside the card's right edge; a ladder longer
      than ten ranks shows the seven around yours and a `Show all ranks` button
      under the road.
      `!top` (`ranks-board`): the top three on a podium, the rest a table, and
      you pinned under it when you are not listed. Main statistics
      (`ranks-stats`): nine tiles, meters only where there is a share; ten from
      Ranks 1.3, `Playtime` last, the four rows shorter so all fit under the
      tabs, in the keyboard list too (a page of the list at a time).
      Settings: the `XP messages` switch and the reset fenced off in red. The
      rank chip beside every title, module pages included
- [ ] Ranks 1.3's playtime board (`!ttop`, or Leaderboard → Playtime) draws
      as `!top` does: a podium, the table with each player's rank art and
      tier, the playtime as the figure (`5d 19h 15m`), and you pinned under it;
      in Premier the rows keep the tier's colour without a CS Rating, which
      would be the player's XP
- [ ] Ranks in `Custom` mode with the wiki's example (`"RankMode": 4`,
      `CustomRankBase` `26283401`, `CustomRankMax` `100`, a 100-rank ladder,
      Workshop `3340561607` in AddonsManager): the chip, the hero card and the
      road show the same numbered badges as the scoreboard. `Show all ranks`
      pages the road with every node's badge: 47 to a page in mouse mode
      (`100 ranks · 48–94 shown`, the last of them not cut), and in the
      keyboard list as many as a page of the list holds
- [ ] in the keyboard list, Ranks' pages are no longer than other menus' (5
      lines with core's default `MaxVisibleLines`): My rank shows the hero card,
      the XP, the progress and `Show all ranks`, and adds the road only where
      three ranks of it fit; `Show all ranks` lists the ranks as cards like
      MapChooser's maps, a 3px gap apart, yours marked `You`
- [ ] with `CustomRankBase` set to a number CustomHUD has no class for, ranks
      keep their colour and show no icon, never an empty frame
- [ ] in the keyboard list, rank badges sit on the row with no dark tile
      behind them, and the chip's badge fits inside the chip
- [ ] rank badges are as sharp as the text around them at every size: the My
      rank hero card, the road, the podium, the board's rows and the chips, in
      Competitive and with a custom set
- [ ] Ranks' sidebar ends in `Help`, and `Admin commands` for an admin; each
      lists its commands in chat and closes the menu
- [ ] each Ranks page's footer says what it is for (`Your progress to the next
      rank`, `Click a player for their statistics`) rather than `Click to
      select`; a keyboard mode keeps its key legend
- [ ] Ranks' facts take no click and no key: a tile or a road node does
      nothing when clicked, and in a keyboard mode the cursor still moves
      through them with no number beside them
- [ ] Ranks with its modules installed: Statistics' tabs include Weapon stats,
      Overall Hit Stats, Combos and Prestige, and each opens on the same page,
      sidebar and tabs still in place, never in the plain mouse list; Weapon
      stats draws as weapon cards (`ranks-weapons`), and `<` returns to
      Statistics with no flicker
- [ ] VIP (VIPCore 1.2.0 with its modules): `!vip` opens Perks under a VIP
      sidebar (Perks, Customize, Gun menu, then VIP users and Grant VIP for an
      admin). Perks shows your tier, its expiry with days left and every group
      you hold over a card per perk with its glyph and one-line description;
      a switch flips in place, and a perk your group has no access to is
      dimmed and a click does nothing
- [ ] Customize: field-of-view cards with a dial that opens with the value,
      `Default` on the game's own and `In use` on yours; chat tags drawn as a
      chat line with your name, the one in use outlined. A click switches it
      and the outline moves
- [ ] Gun menu (`!guns`): primaries with their class over each render, the
      pistols beneath, your picks outlined; the loadout bar names them, and
      `Give loadout` gives both and closes. The chips count picks left and the
      seconds until the menu closes
- [ ] VIP users (`!vip_manage`): group tabs with counts, the table with each
      VIP's groups, end date and time left (red when three days or fewer), the
      selected VIP's grants beside it; the expanded grant's box holds its
      extend steps, each labelled (`+1 d` … `+1 y`, in Lithuanian `+1 mėn.`
      whole), `Make permanent` and `Remove`, and both bottom buttons are
      boxed
- [ ] Grant VIP (VIPCore 1.4.0): one choice a page. The players online with
      their team or group badge; then the groups as cards with their glyph
      and perk count; then the lengths, each with the date it would end;
      then This server and All servers. Picking a scope grants the VIP and
      returns to the player list, and **Back** steps back a page at a time
- [ ] Grant VIP on VIPCore 1.3.x, a single page: group cards three to a
      line, scope, length pills and the grant spelled out next to
      `Grant VIP`; with seven to nine groups the cards go compact, and past
      that `Grant VIP` stays whole at the bottom while the form scrolls
- [ ] in Lithuanian (see [Language](#language)), `Leading` reads `Pirmauja`
      and the vote's eyebrow `ŽEMĖLAPIŲ BALSAVIMAS`

The same plugins draw in the list in a keyboard mode (`!setnav wasd`), which
keeps what their pages know about a menu that is its own root:

- [ ] `!shop`, `!rank` and `!vip` opened from chat show their plugin's name
      over the title (`SHOP`, `RANKS`, `VIP`); a submenu shows the menus
      above it there instead
- [ ] VIP Perks lists each group you hold, its end date and the days left
      (or `Permanent`), then the perks; no tier chip, no group statuses
- [ ] the cursor never rests on a label: in the Gun menu, **W**/**S** step
      from the last pistol to `Give loadout` and back without stopping on the
      loadout line, and the page opens on the first gun rather than the
      `Primary` heading. A page of nothing but labels still takes the cursor
- [ ] Grant VIP is the same four pages as with the mouse
- [ ] MapChooser's vote shows `MAP VOTE` there, each stock map's screenshot
      at the start of its row, and each share filling its row from the left:
      blue, the leader's gold, your own pick's light blue even when it leads

### Panels

Panels (`versus`, `ready`, `timeout`, drawn in `panel_versus`, `panel_ready` and
`panel_timeout` by default) are drawn for a
plugin that calls the `CustomHUD.Panels.v1` shared interface, and none of the
test plugins does yet. Once one does:

- [ ] each panel appears with every variable filled in
- [ ] the state classes (`live`, `is-ready` / `not-ready`, `team-ct` /
      `team-t`) change the look
- [ ] a panel and an open menu coexist without overlapping
- [ ] a panel hides when its duration runs out, and on `Hide` / `HideAll`

## 9. Spectators and the transmit rule

This one cannot be checked alone and is the most common source of
"it works on my machine" HUD bugs.

With a second client (or a bot plus a friend):

1. Player A opens `!rank`.
2. Player B spectates player A.

- [ ] B sees **their own** HUD, never A's menu
- [ ] B reconnecting while A's menu is open still shows B nothing
- [ ] A dying with the menu open does not leak it to whoever they spectate

A client binds its HUD panel to a player slot when the panel is **created** and
never re-binds, so a bystander who was watching at spawn time would otherwise
render the owner's state. Entity transmit restriction is the only guard, and it
has to be re-applied per joining client.

## 10. Lifecycle

- [ ] closing a menu removes the panel
- [ ] disconnecting with a menu open leaves nothing behind for the next player
      in that slot
- [ ] `sw plugins reload CustomHUD` (or equivalent) tears down cleanly, and
      menus still work afterwards
- [ ] changing `config.jsonc` while a menu is open applies on the next redraw
- [ ] a misspelt name (`"Keyboard": "menu_lst_v2"` under `Menus.ShopCore`) logs one
      `config.jsonc: Menus.ShopCore.Keyboard "menu_lst_v2" is no keyboard layout for
      ShopCore, so it draws in "menu_list_v2".` on save, and ShopCore keeps the list
- [ ] a `config.jsonc` from before layout names logs the retired-keys warning
      at load (see "Confirm the renderer registered") and still draws every
      plugin as before; with its `NativeText` capture on, a second warning
      names `Messages`, and nothing is captured until a kind is set there
- [ ] a map change does not leave an orphan panel

With `DetailedLogging: true` the apply path logs `applied slot=… menu=…
vars=N classes=N layout=… ms build=… compose=… diff=… write=… total=…`: the
writes it sent, and the game-thread time of each step (`build` includes the
plugin integration's draw; `write` is the native calls alone, so `write` over
`vars + classes` prices one write). A trailing ` respawn` marks a new entity,
` rewind` a new list's scroll rewind and ` rewound` its end three ticks later.
A healthy idle menu logs **nothing** — the diff suppresses
unchanged frames. Continuous logging on a static menu means something is
invalidating every tick, which is worth finding before it ships.

- [ ] Back from a plugin page to its parent (a ShopCore item to its category)
      swaps the page in place, with no blink: its `applied` line has no
      ` respawn`
- [ ] a long list scrolled to the bottom, then a different list (another tab,
      Next page): the new one opens at the top with no empty frame between, and
      the log shows ` rewind` then ` rewound`
- [ ] Back out of a skipped root's page (VIP Perks) still closes the menu, and
      the cursor goes with it
- [ ] a navigation click's `total=` stays under 2 ms

## 11. Plugin by plugin

Last, a full pass per plugin. MapDecals has already proven the point in §2 and
§5: it has never heard of CustomHUD, uses `Core.Menu`, and draws in it with no
changes.

A plugin still on the deprecated `Core.MenusAPI` will **not** be affected: that
is a separate pipeline that never reaches a renderer, and it keeps drawing its
own centre-HTML.

- [ ] on VIPCore's default branch, `!vip` draws core's centre-HTML whatever
      `menus.jsonc` says, and closes as it always has

The WeaponSkins fork is ported
to `Core.Menu` and makes a good full pass: `!ws` covers submenus, back
navigation, focus restore on the in-hand weapon, disabled rows and text inputs
(wear, seed, sticker offsets).

- [ ] every WeaponSkins submenu draws in the CustomHUD layout, none in
      centre-HTML
- [ ] picking a finish for the weapon in hand shows it at once — no `!wp`,
      respawn or reconnect; ammo is unchanged and the weapon stays in hand
- [ ] picking a knife finish switches to that knife already painted; as T,
      the knife menu's Reset gives back the T default knife
- [ ] `!setnav mouse`: the finish you have on is marked in its weapon's grid
      (green rising from the tile's foot and a green check), and so are your
      knife and glove type, their finish, your agent and your music kit;
      picking another moves the mark at once, Reset takes it while nothing is
      applied, and after a team switch the other side's picks show instead
- [ ] a hovered tile lightens and its picture lifts; a hovered marked tile
      stays green
- [ ] backing out of a submenu lands on the row you entered it from, on the
      page that row belongs to
- [ ] buttons/WASD with `MaxVisibleLines 5`: the AK-47 finish list shows five
      per page with a `1/N` counter; `S` past the fifth row turns to the
      next page, never a one-row slide
- [ ] buttons/WASD/numbers: the `!ws` root shows no rule row between Music
      Kits and Weapon Properties, and numbers mode skips no key there
- [ ] buttons/WASD/numbers: Weapon Skins lists Rifles, Pistols, SMGs,
      Shotguns, Snipers, Heavy and Other, each pictured by your own finish in
      it (else an AK-47, a Desert Eagle, a P90, an XM1014, an AWP, a Negev and a
      Zeus as they ship) with `7 guns · 4 skinned` at the right; the header's
      chips count every gun and every skinned one on each page, not just that
      page's; the weapon in hand's category is focused, and each category lists
      only its guns
- [ ] with `"Renderer": "centerhtml"` (core's default), the `!ws` root has no
      line of `─` between Music Kits and Weapon Properties, and every
      WeaponSkins page lists five entries even with `MaxVisibleLines 20`
- [ ] `!setnav mouse`: every `weaponskins.*` menu uses the store-look locker
      (`menu_skins_mouse_v2`). `!ws` opens on Weapon Skins' categories as a
      mosaic: Rifles large at the left, Pistols and SMGs stacked beside it,
      Shotguns, Snipers, Heavy and Other in a row under them. Each shows your
      finish in it (its usual gun first) with the gun and finish named at the
      top left behind a dot in the finish's rarity, else that gun as it ships,
      and `7 guns · 4 skinned` under its name; the bar reads `35 guns · 10
      skinned` at the right. The sidebar reads Weapon Skins, Knifes, Gloves,
      Agents, Music Kits, Graffiti and Pins, with no properties entries, and
      Weapon Skins is active
- [ ] each sidebar entry has its icon: a pistol, a knife, gloves, an agent, a
      music note, a spray can and a rosette. Icons
      are grey at rest, white on hover and light blue on the active entry. A blank
      space where an icon should be means the client's `customhud_icons.vcss_c`
      is stale
- [ ] the active entry follows you down: inside AK-47 finishes, Weapon Skins
      stays active
- [ ] a category opens with a tab strip over its guns: Rifles, Pistols, SMGs,
      Shotguns, Snipers, Heavy, Other. The open tab is light blue on a faint
      blue fill; Pistols holds the ten pistols, CZ75-Auto to USP-S, in two
      lines. Each gun you skinned shows that finish, named in its rarity's
      colour under the gun's name
- [ ] a tab opens its category in place. Knifes, Gloves and an item's
      properties show no strip, and the locker keeps its size on every page
- [ ] a gun's finishes drop the strip. Over the grid, a spotlight shows the
      finish you have on: its picture on a glow of its rarity, the gun's name,
      the finish's name, its grade in the grade's colour over a bar, then
      Exterior (`Field-Tested (0.18)`), Pattern, StatTrak™ (only on a StatTrak™
      item) and Name Tag (only when set), a `PROPERTIES` button under them,
      and an `EQUIPPED · CT` pill on the picture. On a gun with no finish it
      shows the gun as it ships, `Stock`, and Reset is the marked tile. The
      grid under it shows exactly two lines, and the bar reads `61 finishes`
- [ ] with all four facts showing, `PROPERTIES` still sits whole inside the
      spotlight. It opens the gun's inspect card; `<` there reads `AK-47` and
      returns to its finishes, the spotlight showing any change. A gun you
      never applied anything to has no button. On your knife's or gloves'
      finish page it opens Knife or Glove Properties; another knife type's
      page has none
- [ ] on your knife's and your gloves' finish pages the spotlight shows the
      whole render, from blade tip to pommel and fingertips to cuffs, clear of
      the stage's top and bottom edges; a gun's still fills the stage's width
- [ ] the agents page spotlights the agent on your side: its portrait standing
      at the right of the stage, clear of the `EQUIPPED · CT` pill, `CT AGENT`
      over its name, its grade (Distinguished, Exceptional, Superior or Master)
      in the grade's colour, `Patches 2 / 5` and `PROPERTIES`, which opens
      Agent Properties; `<` there returns to the agents page. With no agent
      applied there is no spotlight, and without the `Patches` permission no
      Patches row or button
- [ ] `<` from a gun's finishes returns to that gun's category, with its tab
      open and the gun focused
- [ ] the active entry is a light-blue label and icon with a 2px light-blue
      edge at the left and a faint light fading from it
- [ ] every item of a menu sits in one scrolling grid, five tiles per line,
      whatever `MaxVisibleLines` is (all 66 AK-47 tiles); every line but the
      last is full edge to edge, and each image tile shows the picture, then
      the name, then the caption in its rarity's colour over a 2px rarity line
- [ ] the grid scrolls with the mouse wheel and by dragging the scrollbar,
      which stays in place while the tiles move, with no empty column before
      it
- [ ] the bar at the top holds Back at the left, the title, the page's counts
      at the right and `×`; there is no footer, no `<<` / `>>` and no page
      counter
- [ ] Back is hidden on a top-level page (the categories mosaic, the knife
      grid). On a category it reads `WEAPON SKINS` and returns to the mosaic;
      on AK-47 finishes it reads `RIFLES`
- [ ] `×` closes the whole menu from any depth and releases the cursor; the
      menu's close key on a top-level page also closes it rather than
      reopening the same page
- [ ] a sidebar entry from a nested menu opens that category, and `<` there
      is hidden again
- [ ] Weapon Properties (the keyboard root, or the first locker's sidebar)
      lists only guns you applied a skin to on your current
      team (knives and gloves excluded), each card showing that finish; one
      opens its inspect card: the finish's picture, rarity and name, then
      Nametag, Wear with its banded bar, Seed, StatTrak™ count with its switch,
      Souvenir, Stickers and Charms. With nothing applied the page says so
- [ ] turning StatTrak™ or Souvenir on turns the other off, the count box dims
      while StatTrak™ is off, and the weapon in hand updates at once
- [ ] hovering the wear bar tints it from its left edge to the pointer; a
      wear clicked on the bar or typed exactly (`0.000123`) applies to the
      weapon in hand at once and the other control follows
- [ ] Seed refuses `1000`: core's invalid notice shows in its box for two
      seconds, then the old seed returns; `×` clears the name tag and is gone
      until a tag is set again
- [ ] `<` from an inspect card shows the grid again with no card, field text
      or fill left over
- [ ] the inspect card is the item on a stage at the left, its grade and name
      under it, and the fields at the right, the labels in one column and long
      names whole; a typed name tag, wear, seed or count shows in its box at
      once, never `Accepted`
- [ ] Knife Properties is the same card without Souvenir, Stickers and Charms;
      Glove Properties shows only Wear with its banded bar, under the rarity
      line reading Extraordinary
- [ ] a glove wear clicked on the bar or typed exactly redraws the gloves on
      your hands at once, with no `!wp` or respawn, and survives a respawn
- [ ] a sticker slot shows its sticker's name and picture once applied, and
      Sticker Properties appears in the slot only while one is set
- [ ] in the inspect card, Stickers and Charms are headings with a hairline,
      not buttons: hovering or clicking either name does nothing. Stickers
      shows six tiles under it and Charms one: each tile the picture of what
      is on that slot (hover names it), an empty one a plus. Clicking the
      third sticker tile opens slot 3's capsule list directly; picking a
      sticker lands on the slot list as usual, and `<` from there returns to
      the card, its third tile showing the new sticker. Clicking the charm
      tile opens the charm page. Without the `Stickers` / `Keychains`
      permission the tiles show what is on but do not react
- [ ] the sticker on a slot is marked in its capsule's list, and a weapon's
      charm in its collection
- [ ] picking a sticker, or Reset in a slot, lands straight back on the slot
      list with that slot showing the result; `<` on a capsule's sticker list
      still goes back one page
- [ ] stickers show on the weapon in hand, not just once it is dropped: hold
      an AK-47 with a finish, add a sticker, change its wear, add a second,
      then remove them all; every step shows in hand at once and matches the
      dropped weapon. Repeat on an M4A4 with 龍王 (Dragon King), a
      legacy-model finish, and once more after a map change. A layout new to
      that finish is drawn a few 0.001 steps of wear from the one picked, in
      the same exterior (the game caches a finish by its wear); the menu keeps
      the picked wear
- [ ] a slot's capsule list shows each capsule's own art (major capsules too),
      and a capsule's stickers each show their sticker
- [ ] Charms lists Reset, then the four charm collections (Dr Boom, Missing
      Link, Missing Link Community, Small Arms) with their collection icons and
      Cologne 2026 Highlight with its souvenir charm, then Sticker Slab, Austin
      2025 Highlight and Budapest 2025 Highlight; each collection opens its
      charms with their pictures, rarest first. Budapest 2025 is a plain card:
      the game ships no picture for it
- [ ] Graffiti lists Reset, the tournaments with their logos, then every
      graffiti with its picture; a colourable one opens its colours, each card
      showing the graffiti in that colour. A pick is named in chat, and the
      picked graffiti is marked, in the colour picked
- [ ] `!spray` aimed at a wall or the floor close by paints the picked
      graffiti in its colour; with a graffiti equipped in your Steam inventory,
      the game's own graffiti key paints the one picked in `!ws` instead
- [ ] Agent Properties lists Reset and Patch 1 to 5; a slot opens the patch
      packs with their pictures, then the patches no pack holds. A picked patch
      shows on its slot card, is marked on that slot's lists, and is on your
      agent at once and after a respawn; Stickers lists no patch pack
- [ ] Pins (WeaponSkins 0.9.0) lists Reset, then every pin with its picture
      and rarity; the picked pin is marked and shows beside
      your name on the scoreboard at once, and Reset clears both. A plain card
      with a `Pin` label means the client's `econ_thumbs.vcss_c` is stale
- [ ] unavailable categories are not exposed as active sidebar shortcuts;
      without the `Stickers` / `Keychains` permission a weapon's Stickers /
      Charms card is shown disabled
- [ ] coloured skin rows get the corresponding rarity's caption and 2px line;
      no invented VIP data appears, and the only mark is on the item you have
      on
- [ ] the rarity line along each tile's foot is a thin, crisp line with no halo
      around it
- [ ] paintkit, knife, glove, agent, music kit, charm, sticker, capsule, pin and
      charm collection cards show the game's thumbnail; the weapon and knife
      lists show your finish on each, else the weapon as it ships, the glove
      list your gloves' finish, else each type's first; a weapon's Reset shows
      it as it ships; a card with no image (a glove's Reset) shows none, and
      switching menus never leaves a previous list's image or type on a card.
      A type label without a thumbnail means the client's `econ_thumbs.vcss_c`
      (`econ_stickers.vcss_c` on sticker cards) is stale; no type label either
      means CustomHUD's WeaponSkins integration is not bound (the startup log
      names a WeaponSkins build whose API lacks the members it needs)
- [ ] with the first locker chosen (`Menus.WeaponSkins.Mouse` set to `"menu_skins_mouse"`): `!ws` opens
      straight on the Rifles tab in the orange-accented locker, a pick is
      outlined in orange, the footer holds `<` and `×`, the sidebar still
      lists the four properties pages, and the inspect card's
      Stickers and Charms are plain fields with no tiles
- [ ] Music Kits opens with every kit and its picture; the one kit the game
      ships without a name is listed under its code name, `valve_02`
- [ ] a picked music kit plays as your round music and MVP anthem, and is
      still yours after a reconnect or map change, with no `!wp`
- [ ] browsing several large sticker capsules in one sitting keeps working:
      after about ten full pages the menu redraws once as its HUD entity is
      replaced, at the moment a new list opens
- [ ] entering a knife after scrolling the knife list opens its finishes at
      the top
- [ ] a sticker capsule longer than 128 (an autograph capsule) ends its first
      page in a `»` card and opens the next with `«`; shorter lists show none
- [ ] double-clicking back during a submenu transition cannot activate a
      stale row; wait for the parent's frame before the next navigation
- [ ] switch to buttons/WASD: the list and key navigation return; the
      navigation buttons disappear, and the legend names the keys
- [ ] in that list a sticker slot, a capsule and a capsule's stickers show
      their pictures, as in the locker. An empty tile on a sticker row means
      the client's `menu_list_skins.vxml_c` or `econ_stickers.vcss_c` is stale
- [ ] repeat in numbers mode, then switch back to mouse without reopening
- [ ] repeat on ordinary mouse menus (the mouse list) and the tile grid
      (`"menu_mouse"` as a `Mouse` layout); a `MenuLayouts` layout on
      `menu.vxml_c`, named for both `Mouse` and `Keyboard`, hides the buttons
      outside mouse mode even though the same layout stays loaded
- [ ] root close releases the cursor

CustomHUD draws MapChooser's next-map vote as a live tally from
`IMapChooserApi` (MapChooser 1.6.0). `!mapsvote` (admin) starts one without
waiting for the end of the map.

- [ ] `!maps` opens the map menu on its first page; the sidebar lists Vote
      (while a vote runs and you may vote), Nominate, a divider and the admin
      pages your permissions allow, and the open page's entry is filled
- [ ] while a vote runs, the sidebar's Vote entry carries a red dot on every
      MapChooser page, and the dot goes when the vote ends
- [ ] the ballot opens as cards, three to a line; above it a red chip counts
      the vote down each second and a second chip reads `voted` with the
      turnout (`3 / 16`)
- [ ] each card shows its share as `45%`, its votes under the name and a meter;
      before anyone votes every share is `0%` and no meter is filled
- [ ] a vote raises its map's share and widens its meter; every other card
      moves to its new share on the same tick
- [ ] the leader's share and meter turn gold and it gets a `Leading` tag with
      a trophy
- [ ] before you vote, another player's vote updates your open menu within a
      tick, without reopening it or moving your cursor
- [ ] the Extend card is orange, with what an extend adds beside its votes
      (`1 vote · +15 min · 2 left`)
- [ ] after you vote, MapChooser closes the ballot and CustomHUD opens the
      tally in its place, still live, with your card outlined in light blue and
      tagged `Your vote`; the sidebar stays, it has no cursor and no key
      numbers, and walking with Shift or using E in game never changes your
      vote
- [ ] the tally closes with Tab (or R in wasd mode, `9` in numbers mode, `×`
      in mouse mode, whose footer says `Click × to close`), even with
      `DisableVoteMenuExit` on, and closes itself when the vote ends
- [ ] `!revote` reopens the vote with the current shares and your current vote
      in light blue; picking another map moves your vote, and the tally returns
- [ ] the ballot's mouse footer says `Click a map to vote`
- [ ] with `"PageViews": { "mapchooser.eof-vote*": "maps-rows" }` the ballot
      is one poll row per option, its share filling the row, and under the
      last row a turnout line: `11 of 16 players voted`, a pip per player and
      `ends in 0:18` in red
- [ ] a wasd or numbers player sees each option's share as `45%`
- [ ] in a wasd or numbers list, each map's kind is a boxed label on the line
      under its name, as readable on the cursor's lit row as on the
      others; on the pool pages the player limits go under the label, and on
      the cursor's row they turn a lighter blue
- [ ] `!nominate` shows a chip with the time left on the map (or the rounds
      left); your nomination is outlined and tagged `Your pick`, other
      players' nominations `Nominated`
- [ ] a map played recently, or outside its player limits, shows greyed out
      but stays in the list, with `Played recently` (a lock) or
      `Needs 10 players` on its picture in full strength; clicking it does
      nothing
- [ ] `!mapsvote` (Start a vote) shows the pool as map cards behind a
      `START VOTE` card reading `Select maps to include in a vote`, dimmed;
      the footer says `Click maps to add them to the vote`
- [ ] scroll to the last map with the mouse wheel, back up by dragging the
      scrollbar, then click a map below the first lines: it gets a light-blue
      glowing outline, the page stays where it was, and `START VOTE (1)`
      lights up in light blue; clicking it again clears it
- [ ] `START VOTE` closes the page and counts down five seconds in chat
      before the vote opens on the ticked maps
- [ ] during that countdown and while the vote runs, `!mapsvote` shows
      `START VOTE` dimmed with `A vote is about to start` or
      `A map vote is already running`, and clicking it starts nothing
- [ ] a map change during the countdown cancels it
- [ ] on a server without CustomHUD the page lists each map with ✔ or ✘ and
      no brackets
- [ ] with `maps.jsonc` naming maps for display (`"Name": "Dust II"`,
      `"Id": "de_dust2"`), the ballot, `!nominate`, `!votemap` and the admin
      change-map and set-next-map pages still show each stock map's screenshot
      and logo; every map shows its kind boxed under its name (`Stock map`,
      `Workshop map`), and the pool pages show each map's player limits
      (`Any players`, `8–20 players`)
- [ ] on a server without CustomHUD the vote menu shows map names only and
      closes once you vote, as before
- [ ] the map menu's Map cycle (`!cyclemenu`) lists the maps in order, a row
      each with its place, its shot, its kind and its player limits; nothing
      is cut off. The map on now is tagged `Now playing` in green, its place
      green too, and with `Cycle.Enabled` on, the next map `Up next` in blue
- [ ] each row ends in ▲, ▼ and a bin; the first map's ▲ and the last map's ▼
      are dimmed and clicking them does nothing. Hovering a bin turns it red
- [ ] click ▲ on the third map: it swaps with the second, the list stays open
      under the same sidebar, and `maps.jsonc` holds the new order. ▼ the same
- [ ] click a bin: `Remove <map> from cycle?` shows the map and a red
      `Yes, Remove`; `<` and Cancel return to the list, Yes removes the map,
      says so in chat and returns to the list
- [ ] clicking a map's row away from its buttons opens the map's own page
      (Move Up, Move Down, Remove, Back), whose `<` returns to the list
- [ ] in a wasd list the rows read `1. Dust 2` with the same tags and no
      buttons; E opens the map's page. With
      `"Menus": { "mapchooser.cycle": { "MoveUp": "Space" } }` in
      `menus.jsonc`, Space moves the focused map up and keeps the cursor on it

CustomHUD puts ShopCore's prices in the value column, reading the items from
ShopCore's API (ShopCore 1.1.9; the category tiles' prices and ownership
need 1.2.0). Open `!shop`, then Buy and a category.

- [ ] every item shows its name at the left and its price at the right, with
      no `[price]` after the name
- [ ] an item name too long for the row ends in `...` and still shows its price
- [ ] an item's page shows the name alone on its first line and the price at
      the right of the Buy row; an inventory item's Sell row shows `+` and its
      refund at the right
- [ ] in mouse mode the item list's footer says `Click an item for details`,
      and an item's page `Buy or preview`
- [ ] the balance shows as a gold chip beside the title on every shop page,
      and each price has a gold coin before it
- [ ] item cards: an agent model shows its render, a healthshot or decoy item
      its grenade's, and any other item its category's icon (Bhop, Parachute,
      a hit sound); each card says how long it lasts (`1 hour`, or
      `Permanent` in gold) and a team-only item carries `CT only` / `T only`
- [ ] an item you have on is outlined with `Equipped`, one you own reads
      `✓ Owned` with its price struck through, and one you cannot afford
      reads `need N` in red
- [ ] an item's page shows its art large on the left with the name under it;
      badges, the price with the balance it leaves, and the facts on the
      right; Preview and Buy at the foot
- [ ] Inventory lists each owned item with its art, `Equipped` on the one you
      have on, and the time a timed item has left over a draining meter
- [ ] with ShopCore 1.2.0, Buy → Visuals shows each subcategory as a tile:
      its name large at the foot with `N items · N owned` under it, the
      cheapest item left top-left with a coin (`From 500`, red when you can't
      afford it, `✓ All owned` once you have them all) and a green `Equipped`
      pill top-right while one of its items is on. Player Models shows the
      model you have on; the others their icon, large and faint
- [ ] a meter along each tile's foot fills to how much of it you own, green
      when complete; hovering lights the tile's edge and warms its icon
- [ ] two to six categories fill the page with taller tiles; a page of seven
      closes on two pairs, so no tile sits alone on a line
- [ ] Inventory → Visuals: the same tiles, each with the time left on what
      runs out first (red in its last 15%) or `Permanent`
- [ ] with ShopCore 1.1.9, the same pages still draw as tiles, with the item
      count alone
- [ ] on a server without CustomHUD the same rows read `name [price]`,
      `Buy [price]` and `Sell [+refund]`, as before

CustomHUD draws Admins' menus from its API (Admins 2.0.0). Join with a bot or
a second client so there is someone to act on, open `!admin` in mouse mode,
then repeat the first three in wasd.

- [ ] Players: a roster of avatars with each player's team, `You` on your
      own row, K/D at the row's end and no ping or ping bars (a bot's avatar
      reads `BOT`), and in red under the name what is on them (`Muted · 2 d`,
      `Frozen`); a player above your immunity reads `Immune (<their group>)`
      in grey. Two chips beside the title count the players and give your
      admin group; the team filter sits in the foot
- [ ] the pane beside it reads `No player selected`; back out of a player's
      page and it shows that player's sheet: avatar, name, SteamID, team,
      the K/D and group band (`—` for a non-admin), and their last four
      records with the kind and length in red while active
- [ ] in wasd, Players lists the same rows with no ping bars and every avatar
      (a bot's included) in one column; Bans and Comms put a global record's
      globe before the avatar, never over it, and keep every avatar in one
      column
- [ ] a player's page: Punish, Control player, History and Sessions
      as cards with what each holds, History counting the records; a group opens its
      actions as tiles three to a line, all dark with white icons, Kick, Ban and
      Slay included, and a switched-on one (frozen, noclip, beacon) outlined.
      Unmute appears in place of Mute while they are muted
- [ ] Sessions (needs `admins.commands.sessions`): `Playtime` as five tiles,
      today, this week and this month on one line, this year beside a total
      two tiles wide on the next, both lines as wide as the rows under them and
      no tile lighting up under the pointer; then `IP addresses`
      with each address's session count and when it was last used, then
      `Sessions` with each connection's length, start, the server when it
      was another, `Online` while open, and its IP. Clicking an address
      prints the accounts that used it in chat
- [ ] a bot's Punish group also offers Ban, Gag, Mute and Silence; each
      opens that punishment's player list under a `has no SteamID` chip
      naming the bot. Your own row offers them too
- [ ] Ban: the steps across the top, finished ones ticked with their pick,
      the current one numbered in light blue; kinds as chips with their glyph,
      reasons two to a line, lengths four to a line with Permanent across the
      row in red, and `Other reason` and `Other length` as chips with a chat
      glyph that go on to the next step. The rail on the right builds the ban
      as you pick, the step being made in gold and a part left for chat as
      `In chat`, with a dimmed `Confirm ban` (or `Print the command in chat`
      once a part is left for chat) until the last step
- [ ] with `Other reason`, `Other length` or `Someone else` picked, the last
      step offers `Print the command in chat` as a light-blue button and no
      summary. It closes the
      menu and prints the command filled in with every pick (the player's
      SteamID64, the length as one token such as `30h`, the reason), with
      `<reason>`, `<time>`, `<steamid64>` or `<ip>` only for what was left;
      `Someone else` prints a SteamID and an IP form
- [ ] from the length step on, a quote shows the message the player will
      read, for the length picked or, before one is, the first on offer
- [ ] Bans and Comms: a table of `#id`, player, reason and time left over a
      meter (red under a fifth left, `Permanent` in red, `Ended` dimmed, and
      `Unbanned`, `Ungagged` or `Unmuted` dimmed on a lifted one), a globe on a
      global record, chips with the active, expired and unbanned (lifted on
      Comms) counts, and `Give`, the filters and `Retry` in the foot; the pane
      shows the selected record's fields with the IP masked, a lifted one's
      end as `Unbanned` and the date
- [ ] a record's page lists its actions with glyphs, Unban, Ungag or Unmute in
      red, beside the same fields
- [ ] My tools: ESP, Hidden, Noclip and God mode with their state, an `On`
      one outlined; Server: the restart delays, the maps with the current
      one outlined and `Current`
- [ ] `!votekick <name>` with a second client: both see Yes and No as two big
      tiles, a chip counting down, who called it, the target's card with
      their K/D and team, and under them a pip per voter, yes in green and no
      in red, with the rule; `!votemap de_dust2 de_inferno` shows the maps as
      cards with their screenshots and shares; `!vote Lunch? Pizza Sushi
      Tacos Burgers` a numbered list with each option's share, and a poll of
      three or fewer options numbered tiles
- [ ] in wasd every page keeps its rows and glyphs in the list, and the
      ballot's title is the question, not `Vote`
- [ ] with Admins removed, CustomHUD still loads and logs that it skipped
      Admins; with an Admins build before 2.0.0 the integration is skipped
      the same way and its menus draw as plain rows

MixScrims (`MixScrims-SwiftlyS2`) has built-in layouts: in a keyboard mode its
menus draw in the list under a `MIX` eyebrow, and in mouse mode each opens in
its `mix-*` view ([Plugin Pages](PluginIntegration.md#plugin-pages-mouse)). From
MixScrims 1.13.0 CustomHUD fills the pages from its API (Provider
Integrations); an older MixScrims shows plain rows in each view's frame. Run a
mix through captains, the knife round and a few rounds of the match, once in
wasd and once in mouse mode, once with Ranks installed and once without.

- [ ] the captain's team pick (`mixscrims.team-pick`) and side pick
      (`mixscrims.knife-side-pick`) have no exit: R in wasd, `9` in numbers,
      `×` and `<` in mouse and Tab leave them open, and the legend offers no
      close key
- [ ] mouse: the team pick opens in `mix-draft`: the pick order strip over the
      page (picked cells with the player's initial in their team's tone, the
      current one `Now`, later ones `CT`/`T`), CT's roster in the column beside
      the pool and T's opposite (captain, `Pick N` rows, open picks with
      `Picking now` lit), the `Your pick` card with the clock and a bar that
      empties, chips `0:42 to pick` and `3 / 8 picked`; a click picks that
      player, and the next captain's page shows the pick in the strip and roster
- [ ] mouse, team pick with Ranks in Premier mode: pool cards show the CS
      Rating badge and `K/D x · HS y%`, rosters `Avg rating`; without Ranks the
      cards show this map's K/D alone and no average
- [ ] keyboard, team pick: the pool rows and two lines under them naming each
      team with `picked/size`
- [ ] mouse: `!captain <t/ct>` (admin) opens `mix-captains`: the CT and T slot
      rows as banner cards (the holder, or `Click a player below` with
      `Picking` lit on the side being filled; clicking a card switches side),
      a table head over the players, each with K/D, and with Ranks their
      rating badge and HS %; in warmup an unready player is faint with
      `Not ready`; the `1 / 2 captains` chip counts as captains are set
- [ ] mouse: the side pick opens in `mix-sides`, Stay and Switch as two big
      tiles in the sides' tones; voting, each shows its votes
- [ ] `!timeout`, `!surrender` and `!votekick <name>` open a Yes/No ballot for
      the caller's teammates: in a keyboard mode the list's `ballot` view,
      Yes and No as its two big rows; in mouse mode `mix-ballot`, a small
      panel docked at the left. A vote closes it, and so does the close key
- [ ] the map vote (`mixscrims.map-vote`) draws as MapChooser's: stock maps
      over their screenshot, each map's share and votes, `Your vote` on yours
- [ ] mouse: `!mix` (admin) opens `mix-control`: the status line as a chip,
      `Match flow` down the page (done phases ticked, the current one lit with
      its ready count, votes, pick or score, later ones numbered; no map vote
      steps with map voting skipped, no captain or pick steps without
      captains), and the rows as tiles under Flow, Players and Map with their
      glyphs; Hold lit when on and greyed out when a plugin holds; Reset and
      Kick spectators red; the page follows each phase change within a second
- [ ] keyboard: `!mix` shows the rows with their glyphs and no road
- [ ] mouse: `!mix` → Change map and Map pool open as `maps-pool` cards, four
      to a line: stock maps over their screenshot, others a prefix mark, each
      with its badge and its id under the name where they differ; a Change map
      card loads that map, and Map pool's cards are not greyed out
- [ ] mouse: those two pages have no sidebar, so their cards span the wider
      list: the rows fill it edge to edge with no stretched or squished card,
      no card cut at the right edge and no wide empty gap after the last
      column; check with a pool of 3, one that fills a line and one that wraps
- [ ] team pick, mouse: with CT, T, CT picked and T picking, CT's first pick
      disconnects: the banner still reads `Pick 4 of N`, the chip `3 / N
      picked`, `Now` stays on pick 4 and T's column lit `Picking now`; CT's
      remaining pick shows no `Pick N` line
- [ ] team pick, mouse, `ResetOnCaptainLeave: false`: after four picks a
      captain disconnects and the phase restarts with a new captain. Whether
      the new captain came from the players left to pick or from that team's
      own picks, the chip still reads `4 / N picked`, `Now` sits on pick 5,
      the banner and `!mix`'s team pick step read `Pick 5 of N`, and
      `Picking now` lights the column of the team whose turn it is. The
      leaver's team shows no avatars in its made cells, its new captain reads
      `Captain` wherever they sit in the column and none of its players has a
      `Pick N` row; the other team keeps its avatars and pick numbers. The
      count holds through the picks that follow
- [ ] team pick, mouse, default `ResetOnCaptainLeave`: a captain disconnects
      mid-pick: in the second and a half before the reset the chip does not
      gain a pick
- [ ] keyboard (wasd and numbers), `!captain <t/ct>`: no table head, and each
      page fills the list's rows (5 by default: the two slots and 3 players)
- [ ] no MixScrims menu shows a sidebar, and none leaves a `{s:…}`
      placeholder
- [ ] the warmup ready list and the vote-kick progress stay core's
      centre-HTML (§7)

## Design v1

The first, orange-accented designs are the layout names without `_v2`. Set them
in `Menus`: `"menu_locker"` (`"menu_skins_mouse"` for WeaponSkins, and
`"menu_list_mouse"` for `Other`) under `Mouse`, `"menu_list"` (`"menu_list_skins"`
for WeaponSkins) under `Keyboard`. It reloads live, so the next menu opened
shows it.

- [ ] the list, the page locker and the WeaponSkins locker switch together:
      a 3px orange edge down the list's left side, the locker's orange frame
      with its gold eyebrow and a footer of `<`, the hint and `×`, and the
      first WeaponSkins locker opening on the Rifles tab
- [ ] every check above holds with orange where it reads light blue, and the
      shop's equipped item outlined in orange rather than green
- [ ] back to the `_v2` names, the next menu is the store look again, with no
      part of the first design left on screen

## Quick reference

| Command | What it exercises |
|---|---|
| `!rank` | Ranks: title, header divider, toggle, its own pages |
| `!mapdecal` | MapDecals: the list, toggle, sliders, input, submenu, an alert |
| `!ws` | WeaponSkins: long lists, inputs, the WeaponSkins locker |
| `!shop` | ShopCore: prices, disabled rows, shop pages |
| `!maps` / `!mapsvote` / `!nominate` / `!revote` | MapChooser: the map menu, the live tally, map pages |
| `!vip` | VIPCore: disabled rows, VIP pages |
| `!timeout` / `!surrender` / `!votekick` | MixScrims: Yes/No ballots |
| `!captain <t/ct>` | MixScrims: the captains page (admin) |
| `!ch_test center` / `!ch_test alert` `[text]` | a centre message or alert as any plugin sends it, and what draws it |
| `!admin` | a `Core.MenusAPI` menu, which must stay centre-HTML |
| `!setnav <mode>` | nav mode / layout profile |
| `!hudscale <n>` | UI scale ladder |
| **F6** | Panorama debugger — panel tree, computed styles |
