# Authoring Panorama Layouts for CustomHUD

Everything here was verified against Valve's shipped CS2 files, the live CS2
convar/property dump, or Valve's own wiki. Where something is community-sourced
or unverified it says so.

## The short answer on browser previews

**A browser cannot render Panorama faithfully, and no amount of CSS mapping
will fix that.** Panorama is a different layout engine, not a CSS subset:

* **There is no `display` property at all.** `flow-children` is the only
  automatic-layout switch, and there is no inline/block distinction.
* **Panels default to `fit-children` on BOTH axes.** An unstyled `<Panel>`
  shrinks to its content horizontally; an unstyled `<div>` fills its parent.
  This one difference alone reshapes almost every layout.
* **There is no `justify-content` equivalent.** `horizontal-align` /
  `vertical-align` are *cross-axis only* — main-axis placement comes purely
  from child order and `fill-parent-flow` weights.

So treat any browser preview as a **hierarchy and colour sanity check**, never
as a layout check. For layout, use the debugger below.

## CS2 ships a Panorama debugger, on F6

This is the DevTools equivalent, and it inspects the *real* rendering.

Valve's own shipped `game/csgo/gameinfo.gi` says so in a comment:

```
// Panorama - use f6 to close and open debugger
// (Otherwise f6 just opens debugger, and a subsequent f6 does an inspect)
"panorama_toggledebugger_mode" "1"
```

Hover a panel and press **F6**. You get the panel hierarchy on the left and,
on the right, the full CSS cascade per property — which rule won, and the
computed value. A `:inspect` pseudo-class fires on the panel being inspected.

| Convar | Default | Effect |
|---|---|---|
| `panorama_toggledebugger_mode` | `0` | `0` = F6 opens, next F6 inspects. `1` = F6 opens/closes. |
| `panorama_debugger_theme` | `Light` | Debugger window theme. |

Neither is `developmentonly` — both are live in the retail client. Launch flags
should not be needed; if F6 does nothing, try `-dev -tools -insecure`.

> The debugger is resource-intensive (Valve's own note). Close it before
> judging frame timing.

## Get the property list from the game, not from a decompile

```
dump_panorama_css_properties   // every valid property *and its documentation*
dump_panorama_events           // every panel event
```

Both are flagged `release`, so they work in a normal client. This is how
Valve's own wiki page is kept current, and it is strictly better than a
name-only dump pulled out of `libpanorama.so` — it carries the accepted values
and the descriptions.

`panorama_generate_layout_xsd` emits an XSD for layout XML, which makes XML
validation authoritative rather than a hand-maintained whitelist. It is
`developmentonly`, so it may not run on a retail client.

## What a HUD layout may contain

The client checks a custom HUD layout against an allowlist, disassembled from
`client.dll`; every layout here keeps to it:

| Element | Attributes |
|---|---|
| `Panel` | `id`, `class`, `hittest` |
| `Label` | `id`, `class`, `hittest`, `text` |
| `Image` | `id`, `class`, `hittest`, `src`, `languages` |
| `Button` | `id`, `class` |

plus `root`, `styles` and `include`. The only resource a layout may reference
is a `.vcss`, so an `Image` cannot point its `src` at a texture and a panel
takes no inline `style`. A picture is a `background-image` in a sheet, switched
on by a class (see [Images](#images)). A `Button` takes no `hittest`.

## There is no layout hot-reload command

Searched the live CS2 convar surface: `panorama_reload`, `panorama_reload_css`,
`panorama_reloadjs` and the whole `cl_panorama*` prefix **do not exist**. The
only `reload` hit in the `panorama` module is `panorama_reload_animations`,
which is `developmentonly` (compiled out of the public client).

Sibling Panorama engines (Strata Source) bind `ReloadPanorama` to F7 and
`ForceReloadPanorama` to F8, but grepping CS2's own binary strings finds
`ToggleDebugger` present and those two **absent** — Valve compiled them out.

So the loop is: compile → re-trigger the layout. What you *can* remove is the
VPK packaging step.

## The authoring loop

Layouts ship in the dedicated `customhud` Workshop addon, so you author against
that addon's two trees, plus a local copy for your own client. `<CS2>` is
`C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive`.

| Tree | Path | Holds |
|---|---|---|
| content | `<CS2>\content\csgo_addons\customhud\panorama\` | raw sources — what `resourcecompiler` reads |
| game | `<CS2>\game\csgo_addons\customhud\panorama\` | compiled `_c` files — what `-addon customhud` mounts and a publish packs |
| client | `<CS2>\game\csgo\panorama\` | a copy of each compiled `_c` for local testing — never published |

The source of truth is `resources/panorama/` in this repo. The content and
client trees are build outputs that the script overwrites, so don't edit files
there.

Every file sits under `custom_game\customhud\` inside `layout`, `styles` and
`images`. CS2 refuses an addon layout anywhere else (`Addons can only add
layouts in the panorama/layout/custom_game/ subdirectory`), and styles and
images follow the same Valve convention.

1. **Create the addon once.** Open CS2 Workshop Tools and create an addon named
   `customhud`. That creates both trees. The script refuses to run until the
   content folder exists.
2. **Run the watcher.** `.\scripts\watch-panorama.ps1` copies each source into
   the content tree and compiles it with `game\bin\win64\resourcecompiler.exe`.
   The compiler writes the `_c` into the game tree. After each successful
   compile the script copies that `_c` into the client tree. The script compiles
   everything once, then recompiles each file you save. `-Once` builds and
   exits with a non-zero code on any failure, so use it as the pre-publish
   check. `-Once -Changed` compiles only the files that changed since their
   last compile. `-SkipClient` leaves the client tree alone, and `-ClientRoot <path>`
   points the copy somewhere else.
3. **Launch with the addon mounted.**
   `cs2.exe -addon customhud -tools -insecure +sv_lan 1 +map de_dust2`.
   Workshop Tools' own Launch button runs `-addon customhud -tools`. The client
   tree is on the base `Game csgo` search path in `gameinfo.gi`, so a local
   client also finds the layouts without `-addon`. Which copy wins when both
   are present, and whether a remote `sv_pure` server accepts the loose client
   copy, is untested. The script keeps both copies byte-identical, so the
   order only matters when a stale file is left behind.
4. **Re-trigger instead of restarting.** Close and reopen the menu, or respawn
   the `custom_hud_layout` entity. Don't rejoin. An addon directory is not a
   sealed archive, so a replaced `_c` file can be picked up mid-session.

> Step 4 is community-sourced (field notes against CS2 build 1.41.7.7), not
> Valve-documented. If a change doesn't show, restart before debugging the
> layout.

CS2 **never** loads loose `.vxml` / `.vcss` files, only compiled `_c` files.
No convar changes that.

### Mouse menu controls

Mouse menu controls are ordinary Buttons: `nav_back`, `nav_previous` and
`nav_next` by default (`Panels.Back` / `Previous` / `Next` in the profile).
Put them in a footer bar and start them with `hidden`; the renderer shows them
only in Mouse mode, and shows the paging pair only on multi-page menus. Their
styles are shared by all menu layouts (see `.chud-bar` / `.chud-nav` in
`menu.vcss`, included after the tokens). Back has a 64×34 hit area, a list
row's height.
List variants use `.chud-bar--list` so hidden controls leave only the original
keyboard hint, without an empty button-height strip. Child labels use
`hittest="false"`. A custom layout without controls must set the three panel
names to empty strings.
`Panels.Close` (empty by default) names a button that leaves every submenu
level at once; the locker's is `nav_close`, `×`, at the footer's right. With a
Close button the renderer hides Back where it has nowhere to go: on a root, and
on the first level under a sidebar root it skips. The locker overrides
`.chud-nav` inside `.ws-locker` to a nav item's 34px height.
`menu_skins_mouse_v2` has no footer: its `nav_back` and `nav_close` sit in a
content bar at the top, Back holding `{s:crumb}` (`Variables.CrumbPanel` /
`CrumbVar`, the title of the page it returns to) and the bar `{s:info}`
(`InfoPanel` / `InfoVar`, the header's chips on one line). It still declares a
collapsed `footer` Label, for the hint every row layout writes.
The page counter is a Label, `page` by default (`Variables.PagePanel` /
`PageVar`), with `text="{s:page}"` and starting `hidden`. The renderer writes
`Variables.PageFormat` (`"{0} / {1}"`) and reveals it only while a menu has more
than one page, in every mode. A layout without one needs nothing: the write
lands on no panel. Set `PageVar` to `""` to stop the write altogether.
The optional sidebar uses `Sidebar.RootButton` (empty for none), `ButtonPrefix`
and `LabelVar`; the shipped locker declares twelve `category_N` buttons with
`{s:category}` and no root button. A slot holding a root `DividerComponent`
gets `Sidebar.DividerClass` (`divider`) and an empty label, so style it as a
rule; the entry the player is under gets `Classes.Selected`.
Each button also holds an icon panel, `Sidebar.IconPanel` (`category_{0}_icon`),
before its label: `<Panel class="chud-ico">` with one `chud-ico__badge` child,
both `hittest="false"`. Include `customhud_icons.vcss_c` and leave the art to it:
`.chud-ico` draws the default icon, and the composer adds `Sidebar.IconClass`
(`ico-{0}`) for a mapped entry, whose `.chud-ico.ico-<token>` rule swaps the
image. Those rules tie a layout's `.x .chud-ico` on specificity, so a layout sets
only the panel's size and `wash-color` (the icons are white, and the wash colours
them per state), never `background-*`.

### Images

Put images in `resources/panorama/images/custom_game/customhud/`.

| Source | Compiled | Reference it as |
|---|---|---|
| `x.png` | `x_png.vtex_c` | `url("s2r://panorama/images/custom_game/customhud/x.png")` |
| `x.svg` | `x.vsvg_c` | `url("s2r://panorama/images/custom_game/customhud/x.vsvg")`, with `background-size: 100% 100%` |

* **Raster images.** The script writes a generated `x_png.vtex` wrapper into
  the content tree and compiles that. Only PNG is proven in game. TGA, JPG and
  WebP are accepted but untested.
* **SVG.** The compiler reports OK on any SVG. A bad one fails only in the
  client, logging `[Panorama] Unable to parse SVG`. So the script lints each
  SVG and warns about:
  * a missing `<?xml ?>` prolog;
  * a `<circle>` (use `<ellipse>`);
  * `fill="none"` on the root `<svg>`;
  * a root with no `width` / `height`;
  * a file minified onto one line.

  The lint does not check one thing: arc flags glued to the next coordinate.
  Write `a5 5 0 0 1 10 0`, not `a5 5 0 0110 0`.

  Give a colour its alpha with `fill-opacity` or `opacity`, as the game's own
  SVGs do; none of its 1,169 writes an 8-digit `#rrggbbaa`. A tile such as
  `stripes.svg` repeats with `background-size: 28px 28px` and
  `background-repeat: repeat`, and draws over the panel's `background-color`,
  which can stay a gradient.

  An SVG is rasterized once, at the `width` / `height` it declares, and that
  bitmap is stretched to the panel. The game's rank emblems declare 32×13, so
  the 270px hero card drew one magnified eight times. A panel that draws an SVG
  larger than it declares sets `background-texture-size` to the size drawn, in
  the image's own proportions, as Valve's Dota 2 and Deadlock sheets do (`px`
  scales like any other length; raster images ignore it). A browser preview
  redraws vectors at any size, so it never shows this blur.

#### Game econ thumbnails

Item artwork is never copied into the addon. Two generated stylesheets hold one
class per image under `panorama/images/econ/` in `pak01_dir.vpk`, each
pointing at `s2r://panorama/images/econ/<key>_png.vtex`, which every client
already has:

| Sheet | Families |
|---|---|
| `econ_thumbs.vcss` | weapon paintkits, base weapons, agents, music kits, charms, patches, cases and capsules (`weapon_cases`, `tournaments`), collection icons (`set_icons`), collectible pins (`status_icons/collectible_pin_*`) |
| `econ_stickers.vcss` | stickers: about 80% of all classes, so a layout that draws none leaves it out |

Regenerate both after a CS2 update:

```powershell
.\scripts\generate-econ-thumbs.ps1 -Source2Viewer C:\tools\Source2Viewer-CLI.exe  # lists the installed VPK
.\scripts\generate-econ-thumbs.ps1 -Listing x.txt                                  # or a saved --vpk_list output
```

Class names are `econ-` plus the first 12 hex digits of the SHA-256 of the
lower-cased key (`FrameComposer.ThumbToken` must stay in step), unique across
both sheets. Every distinct class name is networked once per HUD entity, so a
short token is cheaper than the 70-character key. That table only grows, so
after 2,048 distinct names (`FrameSnapshotExtensions.ClassNameBudget`) the
renderer respawns the entity at the next change of list. Paintkits use the
`_light` render, closest to Factory New; alternate renders (`_large`, `_small`,
`_square`, the sticker `_1355_37`) are skipped.
A layout opts in by including the sheets it needs and naming its panel in
`Row.ThumbPanel` (e.g. `thumb_{0}`).

### Scrolling grids

A profile with `ShowAllRows` receives every row, so its layout must scroll
rather than grow. The shipped mouse grids scroll the way a production CS2
custom HUD does, but lay their lines out explicitly:

```xml
<Panel id="options" class="ws-grid chud-scroll">   <!-- fixed height, overflow: clip scroll -->
	<Panel class="ws-grid__items">                  <!-- fixed width, flow-children: down -->
		<Panel class="ws-line">                       <!-- one per grid line, flow-children: right -->
			<Button id="option_0" class="... hidden">…</Button>
```

* Give the scroll panel a fixed height and `overflow: clip scroll`. Inside it,
  one fixed-width container flows down through a panel per grid line, each
  flowing right with that line's rows.
* Don't build a fixed column count with `right-wrap`. It moved a line's last
  child to the next line when the line was exactly full, which is what a
  column grid is: the WeaponSkins locker drew four cards and a gap instead of
  five. A line panel never wraps, and its default `overflow: squish` absorbs
  the rounding. `menu_locker` and `menu_skins_mouse_v2` are the exceptions,
  with one rule: see [Page layouts](#page-layouts). The second keeps its five
  tiles 14px short of the line, because its categories view rearranges the
  first three, which fixed line panels could not do.
* Keep line panels bare — no margin, padding or height. A bare line whose rows
  are all hidden measures zero, which the height ladders and the empty-grid
  rewind below rely on.
* Leave a gutter beside the rows and style `.chud-scroll VerticalScrollBar`
  with `layout-position: fixed`, which keeps the bar in place while the content
  scrolls (see `menu_mouse.vcss`).
* Declare exactly `RowSlots` rows, each starting `hidden`, in id order across
  the lines. The composer fills slots from 0 with no gaps, so every line but
  the last visible one is full. It only writes the classes a row actually has;
  a free row receives `hidden` and nothing else, so its text is never written
  and its placeholder never shown.
* Panorama keeps a scrolled panel's offset when its content changes. When a
  different menu or chunk replaces the list, the renderer writes the new rows
  with the profile's `Classes.Rewind` class on the shell for three ticks, then
  takes it off in a one-write apply. Style it to shorten the scroll panel's
  content box while its rows still draw, so the offset clamps to the top with
  nothing visibly missing: the built-in layouts give the items container
  `height: 1px; overflow: squish noclip` under `chud-rewind`, or the panel's
  own `max-height` where the panel fits its content (`.ls-mouse`, `maps-rows`),
  so the panel keeps its height. With `Rewind` empty the renderer sends one
  empty grid for a tick instead, from the same build.
* A layout with no footer paging sets `PageTiles`: past `RowSlots` rows, the
  renderer keeps the first and last slot of each chunk for a `«` / `»` card
  (label = the arrow, value = the target page, class `Classes.PageTile`,
  `page-tile`) that turns the page when clicked.

`menu_locker`'s regions around its list come and go per view, so its list box
has a pixel height only while the list owns its column:

* `#options.lk-list` is `height: 100%` of `.lk-listbox`, which is 576px (the
  page) or 534px under the tabs (`has-tabs`). A band sharing the column
  (`has-strip`, `has-banner`, `has-head`, `has-foot`) or a view that pads it
  (`maps-rtv`, `admins-votes`, `mix-control`) puts the box back on
  `fill-parent-flow`. A view that sizes the list itself sizes the box as
  well: `fit-children` (`mix-ballot`, `maps-rows`), or the list's own size in a
  column with no flow (`shop-product`).
* The detail pane is still `height: 100%` of a box that takes the leftover
  height (`.lk-detail__body` in `.lk-detail__box`), or `fit-children` where a
  view sizes it (`maps-rtv`, `vip-grant`). `vip-grant` caps both at
  `max-height: 488px`, the page less its 88px bar, so the bar under the form
  always fits: a form that outgrew the page squished Grant to a strip. Past
  nine groups, where the form always outgrows it, its `variant-scroll` makes
  the cap a pixel height.
* Both are `overflow: squish scroll`. Their items are `width: 100%`, which
  under `clip` keeps the padding and runs over the scrollbar in the gutter.
* The boxes and the items containers are `hittest="false"`, so nothing but
  the scrollbar sits under the cursor in the gutter.

A list whose height came from `fill-parent-flow` showed a scrollbar that
neither the wheel nor its thumb moved in game, both as `fill-parent-flow`
itself and as `height: 100%` of a filling box (Start a vote, Shop). Every
panel seen scrolling in game has a pixel height: the WeaponSkins grids and
the tile grid. A view that keeps a filling box keeps that risk; give it a
pixel height before it has to scroll. A `max-height` cap (`maps-rows`,
`vip-grant`) has not been seen scrolling either: it reliably keeps what
follows on the page, but keep the content under the cap where you can.

### Row meters

A row carrying `bar=N` gets `Classes.HasBar` (`has-bar`) and `Classes.Bar` with
`{0}` = N (`bar-42`). `menu.vcss` styles a `.chud-meter` holding one
`.chud-meter__fill`, and the 101-rule width ladder, so a layout that includes
it only has to place the meter in each row, with `hittest="false"` on both.
Inside a row that flows right, put it in a panel with `flow-children: none`
(the shipped lists use `.chud-row__clip`), or it takes a column of its own
instead of sitting under the label. A row carrying `chosen` (the player's own
pick) gets `Classes.Chosen`, `chosen`; the shipped sheets colour its name and
meter fill with the accent, and the mouse layouts outline its tile or card in
a 2px accent edge (the Tile look in `menu_mouse.vcss`). Give a chosen card a
different edge from its `:hover` one, and put the chosen rule after the hover
rule: the two selectors weigh the same, so the later one wins on a hovered
chosen card.

### Card layouts

A profile with a `Card` draws a page whose first row is marked `hero` as a
form instead of rows ([Card Pages](PluginIntegration.md#card-pages)). The layout declares, with the
default names from `MenuCardProfile`:

* a `hero` panel with `{s:hero_name}` and `{s:hero_type}` Labels and a
  `hero_thumb` panel for the `econ-*` class, starting `hidden`;
* exactly `Card.Slots` field panels `field_N`, each starting `hidden`, with
  `{s:field_N}` and `{s:field_N_value}` Labels;
* inside each field the click targets: Button `field_N_hit` for the field's
  row, Button `field_N_aside` for its inline row, and `Card.Segments` Buttons
  `field_N_seg{S}` for a slider bar. The field panel itself is never clicked.

The composer sets `card-mode` on the shell, so hide the grid and show the card
with `.card-mode` selectors. Every frame writes the `hidden` state of the hero
and of every field, and hides every grid row while the card is shown, so
neither side leaves stale content behind.

A slider bar is click targets, not a drag: segment S sets `S / (Segments - 1)`
of the slider's range, snapped to its `Step`. Make each segment a left-anchored
Button `(2S + 1)%` of the bar wide, the last one stopping at the bar's end, and
declare them widest first:

```xml
<Panel class="ws-track__hits">
	<Button id="field_0_seg50" class="ws-seg ws-seg-50" />   <!-- 100%, the whole bar, under the rest -->
	…
	<Button id="field_0_seg0" class="ws-seg ws-seg-0" />     <!-- 1%, on top -->
</Panel>
```

* A later sibling sits on top, so under the pointer the narrowest segment that
  still reaches it takes the click: segment S owns `[2S - 1, 2S + 1)` percent
  of the bar, centred on the point it sets. Percentages keep that true at any
  card width.
* Its `:hover` paints from the bar's start to its own edge, which previews the
  value; no script is involved.
* The fill is `fill-N` (0…100) on the field. Panorama has no `calc()`, so write
  the ladder out, one `width: N%` rule per step, and set `overflow: noclip` on
  the fill so the knob at its edge still shows at 0%.

Give a value row's label a fixed width and its box `fill-parent-flow`, or the
boxes start wherever each label ends.

`menu_skins_mouse_v2.vxml` declares the same card under `sk-` classes and is
generated by `scripts/generate-skins-layout.ps1`, whose counts must match
`MenuLayoutProfile.WeaponSkinsLockerV2()`: rerun it after changing one, then
`watch-panorama.ps1`. Each of its fields also declares `Card.SlotTiles` slot
tiles: Button `field_N_slotK` (`Card.SlotSuffix`), holding a picture panel
`field_N_slotK_thumb` (`Card.SlotThumbSuffix`) for the `econ-*` class and a
Label reading `{s:field_N_slotK}`, the slot's name. The composer sets
`Card.SlotsClass` (`has-slots`) on a field whose row has slots, and `hidden` and
`has-thumb` on each tile; keep the tiles outside the field's `_hit` Button, as
the track's segments are, so a click lands on the tile. While `has-slots` is
set the v2 sheet collapses the field's row, its `_hit` and `_aside` Buttons
included, and shows a heading Label reading `{s:field_N}` instead, so the tiles
are the field's only click targets.

### Page layouts

`menu_locker.vxml` and `menu_locker_v2.vxml` are generated.
`scripts/generate-locker-layout.ps1` writes both from a region table that must
match `LockerProfiles.Regions`, `MainSlots`, `MainActs` and `MainParts`: a slot the layout
draws but the profile doesn't name never fills, and one the profile names but
the layout lacks shows its `{s:…}` placeholder. The two files share the page
(the `Page` function) and differ only in the frame round it, so a change to
the anatomy reaches both. After changing a count or a part, rerun the
generator, then `watch-panorama.ps1`. `locker_art.vcss` comes from
`scripts/generate-locker-art.ps1` (`-Source2Viewer <cli>` or `-Listing <file>`,
as for econ thumbnails): map screenshots and logos, skill-group and Wingman
emblems from the game, the replacement rank icon sets in its `$CustomIconSets`
table, and the addon's level badges. Rerun it when a CS2
update adds a map, and keep `LockerProfiles.StockMaps` equal to the map list in
its header comment: a listed map without art draws a blank card, and art for a
map not listed is never used. Keep `$CustomIconSets` equal to
`RankArt.CustomIconSets` the same way: the plugin only sends a number the
stylesheet has a class for.

A view is a block of rules in its plugin's `locker_<plugin>.vcss`:

* Scope every rule by `.lk.view-<name>`, and add the name to the Views table on
  the wiki's Supported Plugins page so operators can find it.
* Draw surfaces with the shared [looks](DesignLanguage.md#looks), never a
  colour of your own: add the view's selector to the look's list in
  `locker.vcss` (or `menu_mouse.vcss` for the tile, the eyebrow and the
  chrome), and name a token for anything else. A plugin tile's chosen selector
  includes `.lk-list`, to outweigh the list's Chosen fill in `locker.vcss`,
  which loads later; a static tile lists its `:hover` beside its rest state.
* Keep a view's type and geometry in its own sheet. Rules of equal weight
  inside a view's block rely on their order (an answer's name after the list's
  name rule), so hoisting one into a shared rule earlier in the cascade lets
  its sibling win.
* Specificity crosses sheets. The plugin sheets load after `locker.vcss`, so a
  view rule wins a tie but not a heavier base rule:
  `.lk .lk-detail__bar .lk-slot .lk-text` (four classes) beats
  `.lk.view-x .lk-text` (three). A view that hides a part must weigh at least
  the three-class `.lk-slot.has-<part>` rule that shows it. Add the region
  (`.lk-list`, `.lk-detail`) or the group (`.grp-x`) to a view's selector
  rather than reordering sheets.
* Sections are styled by `.lk .lk-content .lk-slot.section …`, which outweighs
  a view's ordinary-row rules. A view rule that must not reach a section
  qualifies by kind (`.kind-toggle`, `.kind-action`) or group.
* The `_icon` panel (`.lk-rarity`) is a full-size layer under every other part:
  draw a card's background, box or glow on it. Foot slots have none; use
  `.lk-shade` there.
* A main-list row's `act=` buttons sit in `.lk-acts`, a `hittest="false"` strip
  over the right end of its hit, so a click between them still selects the
  row. A view that puts anything at the row's right end leaves room for them:
  `maps-cycle` keeps its tags 140px from the edge.
* A pip carries the lit colour, so a view or `metertone=` recolours lit pips
  with one rule; `pips-N` collapses the pips past N, and `lit-K` greys the
  unlit ones at a weight no tone rule reaches.
* Declare every border side in the same colour, a zero-width side included:
  Panorama draws all four in one (see
  [DesignLanguage.md](DesignLanguage.md#stylesheet-rules)).
* Card lines use `right-wrap`, because one list serves every view and cannot
  hold line panels. Keep a fixed-column line at least 6px short of the list's
  inner width, ui-scale rounding included: two 50% cards make an exactly full
  line, and the second one wraps.

### List layouts

`menu_list.vxml` (Wasd, Buttons, Numbers) and `menu_list_mouse.vxml` (Mouse)
are generated too, with `menu_list_skins.vxml`: the keyboard file plus
`econ_stickers.vcss`, for WeaponSkins. `scripts/generate-list-layout.ps1`
writes them, and their `_v2` files, from `ListProfiles.KeySlots`, `MouseSlots`
and `Segments` and the row anatomy that `ListProfiles.Row` names part by part.
As with the locker, a part one side draws and the other doesn't name either
never fills or shows its placeholder, so rerun the generator after changing
either.

* The rows reuse the locker's part classes and load `locker.vcss` before
  `list.vcss`, so the list only places what the locker already styles. Those
  part rules are unscoped and some weigh three classes
  (`.lk-slot.has-glyph .lk-text`), so a list rule must weigh as much:
  `.ls .lk-slot .lk-text`, not `.ls .lk-text`. `.lk-slot.disabled` fades a
  whole slot; the list sets the slot back to full opacity and dims only
  `.ls-art` and `.lk-text`, which keeps a pill saying why readable.
* A row's first child is its poll fill, `ls-fill`, under the row's Button, so
  a `bar=` share lies between the slot's ground and its text. All three meters
  (`ls-fill`, the progress `ls-meter` under the name, the slider `ls-track`)
  take `menu.vcss`'s `bar-N` ladder. `list.vcss` collapses each one unless the
  row's kind asks for it, and the rule showing the fill comes after the rule
  hiding it, since the two weigh the same.
* The keyboard layout's plain hint (`#footer`) sits inside `#legend`, and
  `.ls-legend.has-legend .ls-hint` collapses it while the composer writes
  keycaps (`Page.Legend`). A layout without a legend leaves `Page.Legend` null
  and keeps the hint.
* The Numbers keycap, `ls-key`, stays transparent until its row gets
  `has-key`. A disabled row gets none, so the keys a page shows stay
  consecutive.
* The mouse layout has no keys and no cursor; the row under the pointer is
  the bright one. Its body caps at `max-height: 390px` and scrolls
  (`overflow: squish scroll`), because a mouse list shows every row rather
  than paging. A slider's or selector's control, `.ls-ctl`, lies over the
  row's end: Buttons `option_N_dec` and `option_N_inc` either side of the
  value, and for a slider `Segments` equal Buttons `option_N_seg{S}` across
  its track, segment S setting `S / (Segments - 1)` of the range. Reserve
  room for it with the Button's `padding-right` (214px for a slider, 104px
  for a selector), or the name runs under the control.

### The v2 sheet copies

The `v2` layouts (the `_v2` names, the defaults) read copies of the shared sheets
in `styles/custom_game/customhud/v2/`, which `scripts/generate-v2-sheets.ps1`
writes with the first design's palette renamed to the store tokens. A
`@define` is replaced as text when a sheet compiles, so the same rule cannot
draw in two palettes from one file.

* Edit `menu.vcss`, `menu_mouse.vcss`, `locker.vcss`, `locker_<plugin>.vcss`,
  `list.vcss`, `hud_text.vcss` and `hud_alert.vcss`, then rerun
  `generate-v2-sheets.ps1`. Never edit a copy.
* A token the map renames needs its store twin in `customhud_tokens.vcss`;
  the script fails, naming the sheet, when a copy uses a token that is not
  defined.
* Draw what the store look draws differently (a frame, a word's colour, the
  green of an equipped item) in `menu_locker_v2.vcss`, `menu_list_v2.vcss`,
  `hud_text_v2.vcss` or `hud_alert_v2.vcss`, which come last, scoped by `.lk2`,
  `.ls2`, `.chud-toast2` or `.chud-alert2`.

### Checking a change

`pwsh -File scripts/audit.ps1` checks what neither CS2 nor `resourcecompiler`
reports. It then publishes the plugin as CI does and compiles the changed
Panorama files. `-Quick` stops after the static checks, a few seconds. A pass
writes `build/audit/receipt.json`.

* **Sheets:** every property against `scripts/panorama-properties.txt`;
  selectors, pseudo-classes, `calc()`, `var()`, `hsl()` and at-rules; the
  [stylesheet rules](DesignLanguage.md#stylesheet-rules); and every width
  budget written in a comment as `3 x 146 + 2 x 6 = 450 of the list's 452`.
* **Layouts:** the [allowlist](#what-a-hud-layout-may-contain), includes,
  tokens first, unique ids and image sources.
* **Generated files:** the `v2/` copies and the generated layouts are
  regenerated into a scratch folder and compared byte for byte.
* **C# and strings:** every `glyph=` against an `ico-` class, every literal
  translation key against `en.jsonc`, English–Lithuanian parity, and `{N}`
  placeholders.

Run it, and every script in `scripts/`, with PowerShell 7. Windows PowerShell
5.1 reads these BOM-less scripts as ANSI, so the list generator writes `â€º`
where the layout needs `›`. After a CS2 update, refresh the property list from
`dump_panorama_css_properties`.

### Before publishing

* **Orphans.** After each build the script lists any addon file that has no
  source in `resources/panorama`, typically from a renamed or deleted file.
  Delete those files from **both** trees, or they ship with the next publish.
  It also lists files in the client tree's `custom_game\customhud` folders that
  no source produces. Those never ship. But a layout that still references one
  works on your machine and breaks for players, so delete them too. Anything
  else that writes into `game\csgo\panorama\*\custom_game\customhud` collides
  with this script.
* **Stale VPK.** Once the addon is published and you are subscribed, Steam's
  downloaded VPK under `steamapps\workshop\content\730\<id>\` can shadow the
  loose files, the client copy included. If your edits seem ignored, pass
  `-PublishedId <id>`. The script then warns when compiled files are newer than
  that VPK.

## Panorama vs web CSS, concretely

The list that matters when writing a layout, or when deciding whether a
browser preview is lying to you.

| Area | Panorama | Web CSS |
|---|---|---|
| Layout switch | `flow-children: none \| down \| right \| down-wrap \| right-wrap` (plus undocumented `down-wrap-left`). Default `none` — children stack at the parent's top-left. | `display`, flex, grid |
| Default sizing | `fit-children` on **both** axes | block fills width, fits height |
| Grow | `width: fill-parent-flow(weight)` — remaining space split by weight among siblings that use it | `flex-grow` with basis/shrink |
| Cross-axis align | `horizontal-align` / `vertical-align`, cross-axis only | `align-items` / `align-self` |
| Main-axis align | **nothing** — order and weights only | `justify-content` |
| Out of flow | `ignore-parent-flow: true` | `position: absolute` |
| Box model | border-box, and there is **no `box-sizing`** to change it | content-box by default |
| Borders | per-side widths, but **one colour** for every side (observed in-game: the top's). A second colour needs a child panel; a browser preview shows per-side colours and will not catch it. | per-side colours |
| Units | `px` is auto-scaled: author for 1920×1080, engine multiplies by `verticalRes / 1080`. Plus `%`, and `width-percentage()` / `height-percentage()` for aspect lock. | real px, `em`, `rem`, `vw`, `vh` |
| Per-panel zoom | `ui-scale`, `ui-scale-x/y/z` — applies at layout level, not bitmap | `transform: scale()` (bitmap) |
| Colours | `#rrggbb`, `#rrggbbaa`, `rgb()`, `rgba()`; `gradient( linear \| radial, … )` wherever a colour goes | adds `hsl()`, named colours |
| Variables | `@define NAME: value;` (textual substitution — usable inside `gradient()` too) | `var()`, `calc()` |
| Responsive | aspect-ratio class auto-applied to the root (`.AspectRatio4x3`) | `@media` |
| Pseudo-classes | `:hover :focus :active :disabled :enabled :inspect :selected :descendantfocus :parentdisabled :layoutloading :layoutfailed :activationdisabled`, plus `:not()` (takes a class or pseudo-class, e.g. `:not(:hover)`) and, rarely, `:first-child` / `:last-child` | the CSS set |
| **Not supported** | `::before`, `::after`, `:nth-*`, attribute selectors, `display`, `calc()`, `var()`, `@media`, `box-sizing`, `hsl()` | — |

The colour and selector rows are checked against Valve's own shipped CS2
stylesheets (GameTracking-CS2, `pak01_dir/panorama/styles`): `rgba()` appears on
882 lines, `:not(` in 200 rules, and none of the "not supported" entries appear
anywhere. An earlier revision of this table called `rgb()` and `:not()`
unsupported; both were wrong.

For the visual rules these primitives are used to build, see
[DesignLanguage.md](DesignLanguage.md).

Unresolved: Valve does not state whether margins collapse. There is no
block-formatting-context concept and `flow-children` behaves like a single-axis
flex container, so **probably not** — verify in the debugger before relying on
it.

### The trap with no web analogue

`width: 100%` on a child of a `fit-children` parent resolves to **zero**. The
parent is sizing to its children while the child is sizing to its parent, and
Panorama resolves that circular dependency as nothing. Give one of the two a
concrete size.

`width: 100%` is a share of the parent's **whole** width, padding included. A
parent left at the default `overflow: squish` shrinks the child back inside its
padding; a `clip` or `scroll` parent lets it keep that width, so the child
starts after the left padding and ends both paddings past the padded edge
(measured in game: the Ranks road's count sat 40px past a list padded 20px each
side). Give a padded scroll panel `overflow: squish scroll`, or put the padding
on a panel inside it.

### Why Panorama silently eats mistakes

An unrecognised property is **dropped with no error and no warning** — the rule
simply does nothing. Every web reflex in the "Not supported" row above fails
exactly this way. `scripts/audit.ps1` therefore checks every property against
`scripts/panorama-properties.txt`, a list to refresh from
`dump_panorama_css_properties`.

A selector the parser cannot read is the opposite: it loses the **whole
sheet**. The game shows an "Error parsing layout and style files" dialog naming
the file and line (`Failed to parse selector`), then a second one from the
layout that included it (`Failed to add include`), and every rule in that file
is gone. `resourcecompiler` does not parse selectors, so a clean build proves
nothing here.

One cause is easy to hit with generated ladders: **a rule listing too many
selectors**. Lists of 48 and 190 selectors failed in game. Valve's own sheets
stop at 30 selectors and about 1650 characters, and anything within both of
those loads, so write a ladder as a rule per line (`.pips-3 .lk-pip-3, … {
visibility: collapse; }`) rather than one long list. The precise limit is
unknown: it lies between 30 and 48 selectors, or between 1650 and 2600
characters.

## If a faithful offline renderer is ever needed

[`CitroenGames/PanoramaEngine`](https://github.com/CitroenGames/PanoramaEngine)
is the only project found that reimplements Panorama's real primitives —
`fit-children`, `fill-parent-flow(ratio)`, `flow-children`, border-box,
`@define`, the selector cascade — rather than mapping onto flexbox. It reads a
loose `panorama/` source tree directly, so no resourcecompiler step.

Caveats: it is a C++ library rather than a tool, very young (created 2026-06),
essentially unproven, and its handling of compiled Source 2 `_c` binaries is
undocumented. Only worth it for something like CI screenshot tests.
