# Plugin Integration

For plugin authors. A plugin that builds its menus with `Core.Menu` already
renders through CustomHUD, with no reference to it and no code changes. This
guide covers going further: designed pages, renderer directives, provider APIs
and the Panels API.

## How It Works

**Overview:**
CustomHUD registers a menu renderer with SwiftlyS2's `Core.Menu`. Core still
owns every menu - its components, focus, keymap and submenu stack - and hands
the renderer a composed frame, which CustomHUD draws into a Panorama layout
only the menu's owner receives. Only *presentation* varies per player: a
renderer cannot change what a plugin put in the frame.

**Phase 1 - Load:**
CustomHUD registers `customhud.panorama`, reads `MaxVisibleLines` from
`menus.jsonc`, publishes `CustomHUD.Panels.v1`, and binds the Cookies plugin and
any [provider APIs](#provider-integrations) it finds.

**Phase 2 - Compose:**
When a menu opens or changes, CustomHUD resolves the player's navigation mode
and the [layout profile](Configuration.md#layout-resolution) for that menu id,
then turns the frame into a snapshot: title, rows, values, the page and the
footer legend.

**Phase 3 - Draw:**
Only what changed since the last frame is written to the player's own
`CCSCustomHudLayout` entity, as dialog variables and CSS classes. The entity is
transmitted to its owner alone; switching layout respawns it.

**Phase 4 - Input:**
`buttons` stays core's own keyboard dispatch. `wasd`, `numbers` and mouse clicks
are CustomHUD's: they move core's focus and send core's own actions, so a plugin
sees an ordinary menu interaction.

```text
Open → Resolve profile → Compose → Diff → Apply to HUD entity → Input / Invalidate → Compose … → Close → Despawn
```

**Key Mechanics:**

- **Per-player presentation:** a profile is chosen per player, per navigation
  mode, with per-menu overrides, so mouse and keyboard players can see the same
  menu in different layouts.
- **Preferences:** `!setnav` and `!hudscale` choices persist through Cookies
  under `customhud.navmode` and `customhud.scale`.

## The List

Every menu without a [page of its own](#plugin-pages-mouse) draws as **the
list**: `menu_list` in the keyboard modes and `menu_list_mouse` in Mouse mode,
docked at the left edge. Each row is drawn from the component behind it:

| Component | Drawn as |
|-----------|----------|
| `ToggleComponent` | A switch |
| `SelectorComponent<T>` | `‹ value ›`; A and D step it |
| `ChoiceComponent<T>` | Its value and a cycle mark; E steps it |
| `SliderComponent` | Its track and value; `track=wear` bands the track with the five exteriors |
| `InputComponent` | Its text in a field, with a pen |
| `SubmenuComponent` | `›` at the right |
| `ProgressBarComponent` | Its label, a meter under it and the share at the right |
| A component that cannot be focused | A fact on the panel: no key, no row ground |

- **Title path:** the menus above it (`MapDecals Menu › Choose a decal to
  edit`). ShopCore, MapChooser, Ranks, VIPCore, Admins and MixScrims root menus
  show their plugin's name instead, and MapChooser's and MixScrims' map votes
  draw each stock map's screenshot.
- **Legend:** the footer shows the keys that work on the focused row, in the
  menu's own keymap: `W S move · E switch · R back` on a toggle, `A D change` on
  a slider or selector, `1-6 pick · 7 8 page · 9 close` in numbers mode, which
  also draws each row's key. The verb follows the component (`switch`, `edit`,
  `change`, `open`, `select`) unless the plugin names one with `verb=`. The close
  key says `back` where it returns to a parent, and is left out where the plugin
  unbound Close.
- **Mouse:** no keys and no cursor; the row under the pointer is the bright one.
  A switch flips where it is clicked, a slider's track sets it where it is
  clicked, and a long list scrolls, paging past 24 rows.
- **Views:** `compact` (a rarity stripe for the art, one line a row) and
  `ballot` (Yes and No as the two big rows, used by MixScrims' mid-round votes)
  change the whole list; pick one per menu id in `PageViews`. A third, `ranks`,
  is the default for Ranks' menus.
- **Directives:** the [page directives](#what-a-page-sends) a plugin sends for
  its mouse page work in the list too.

The classic layouts still ship: `menu.vxml` (`"Default": {}`), its variants
`menu_teampick`, `menu_shop` and `menu_skins`, and the tile grid
`menu_mouse.vxml`.

## What CustomHUD Changes

A renderer receives an already-composed frame. CustomHUD changes only its
presentation, with these deliberate exceptions:

| Element | Change |
|---------|--------|
| Title | Header dividers and runs of rule characters (`---`, `───`, `===`) are stripped; the layout frames the title itself |
| Dividers | A body `DividerComponent` takes no row; only a sidebar draws one, as a rule |
| Footer | Core's footer becomes a short per-mode hint or key legend in the player's language |
| Pages | Fixed pages replace core's scrolling window |
| Value slots | A toggle, input, slider or choice puts its value in the layout's value slot instead of after `": "` |
| Accepted input | The new value shows at once instead of core's two-second `Accepted`; a rejected value keeps core's notice |
| Sidebar | Static, visible root `SubmenuComponent` entries become shortcuts to the same actions |
| Text width | Text is not cut to core's `MaxWidth`; the layout ends it with an ellipsis at the row's real edge, or a marquee for scrolling text |

- CenterHTML and chat draw a divider as a row of `─`. A divider meant only for
  the sidebar should override `IsVisible` to return `true` only when
  `session.Menu.Renderer.Id` is `customhud.panorama`, as WeaponSkins' does.
- Sidebar entries with player-dependent text, enabled or visibility providers
  stay in the normal body only.
- A component that turned fitting off (`Overflow = None`, or a `MaxWidth` below
  1) is passed through untouched.

## Plugin Pages (Mouse)

In Mouse mode, ShopCore, MapChooser, Ranks, VIPCore, Admins and MixScrims open
in one page layout, `menu_locker`: the plugin's sections in a sidebar, the open
section's submenus as tabs, chips beside the title, and the page itself. Each
menu id draws as a **view**, a design for that kind of page:

| Plugin | Menu ids | Views |
|--------|----------|-------|
| ShopCore | `shopcore.*` | `shop-grid` (storefront cards), `shop-list` (a price list), `shop-product`, `shop-inventory` |
| MapChooser | `mapchooser.*` | `maps-board` (vote cards), `maps-rows` (poll rows), `maps-pool`, `maps-rtv` |
| Ranks and its modules | `ranks.*`, `weaponstats.*`, `customcombos.*`, `prestige.*` | `ranks-rank`, `ranks-stats`, `ranks-weapons`, `ranks-board`, `ranks-settings` |
| VIPCore | `vipcore.*` | `vip-perks`, `vip-options`, `vip-guns`, `vip-users`, `vip-grant`, `vip-tiers` (no VIPCore menu opens it yet) |
| Admins | `admins.*` | `admins-players`, `admins-player` (one player's actions), `admins-ban` (a punishment's steps), `admins-bans` (bans and comms), `admins-record`, `admins-votes`, `admins-staff`; the ballot (`admins.vote`) as `mix-ballot`, MapChooser's `maps-board` for a map vote, or the list |
| MixScrims | `mixscrims.*` | `mix-captains`, `mix-draft` (the team pick), `mix-sides` (the knife round's side), `mix-ballot` (timeout, surrender and kick votes, docked at the side), `mix-control` |

- An id no rule names gets `list`, the plain list. `PageViews` picks another by
  exact id or trailing-`*` prefix, winning over the built-in choice and the
  plugin's own `view=`:

  ```jsonc
  "PageViews": { "mapchooser.eof-vote*": "maps-rows", "shopcore.buy.items": "shop-list" }
  ```

- Admins' ballot opens with no root, so it has no sidebar; the integration
  picks its view from the vote (see Provider Integrations).
- MixScrims' menus are all their own root, so none has a sidebar; its map vote
  is MapChooser's `maps-board`. Its team pick and side choice unbind Close, so
  CustomHUD hides `×` and `<`.
- Ranks' modules open as tabs under its Statistics but keep ids of their own:
  `weaponstats.weapons` draws as `ranks-weapons`, and the modules' other pages
  and another player's statistics (`ranks.top.player`) as `ranks-stats`. The
  playtime board (`ranks.top.playtime`, Ranks 1.3) draws as `ranks-board`.
- In the keyboard modes the same Ranks pages draw in the list's `ranks` view,
  which drops the captions of meters only the mouse page draws.

### The WeaponSkins Locker

The built-in `weaponskins.*` rule selects
[`menu_skins_mouse.vxml`](../resources/panorama/layout/custom_game/customhud/menu_skins_mouse.vxml):
a wide, orange-accented locker with a category sidebar and a five-column
scrolling grid of cards. WeaponSkins keeps using ordinary `Core.Menu`.

- Each card shows the item's econ thumbnail, its type and name, and a rarity
  glow from its `MenuTextStyle.Color`. An item the game ships no art for is a
  plain card.
- The item the player has on (a finish, a knife or glove type, an agent, a music
  kit, a graffiti, a patch, a pin) gets a 2px orange outline, read from
  WeaponSkins' API.
- Graffiti show in their colour: the chosen one in the colour picked, and each
  card on a graffiti's colour page in that colour. Agent Properties shows the
  patch on each slot. Both need a WeaponSkins build with graffiti and patches in
  its API; with an older one those pages stay plain and the rest is unchanged.
  Pins, from WeaponSkins 0.9.0, follow the same rule.
- With `Sidebar.AutoOpen`, opening the menu lands on its first entry (Weapon
  Skins) and backing out of that level closes it. The root's buttons, such as
  Ranks' `Help`, join the sidebar.
- The weapon, knife and glove properties pages draw as an inspect card: the
  item's picture, rarity and name, then one field per entry (name tag, wear,
  seed, StatTrak™, Souvenir, Stickers, Charms; gloves take only the wear). A
  click on the wear bar sets wear in 0.02 steps. Keyboard modes keep their list.
- In the keyboard modes WeaponSkins' menus draw in `menu_list_skins`: the list
  with the same art, sticker art included.

## Provider Integrations

A plugin with a public shared-interface API can have its menus drawn as
designed pages without depending on CustomHUD or knowing it exists. Its menus
stay ordinary `Core.Menu` components; CustomHUD finds them by menu id, reads the
figures from the plugin's API, and adds the page directives to the rows the
plugin built. Any other custom renderer can read the same API.

| Plugin | Shared interface | Needs | Menus | Draws |
|--------|------------------|-------|-------|-------|
| Ranks | `Ranks.Api.v1` | 1.0.5 | `ranks.*` | The rank as a hero card with its road through the ladder, counters as tiles, a podium with the player's row pinned (the XP board and, from Ranks 1.3, the playtime board), the settings as a form, and from Ranks 1.2 the Leaderboard's season line as a chip |
| Ranks_WeaponStats | `Ranks.WeaponStats.Api.v1` | any | `weaponstats.*` | Weapon cards with the game's renders and their meters, hit groups as tiles |
| Ranks_CustomCombos | `Ranks.CustomCombos.Api.v1` | 1.0.2 | `customcombos.*` | Each landed combo as a tile |
| Ranks_Prestige | `Ranks.Prestige.Api.v1` | 1.0.1 | `prestige.*` | The level and the next-level meter as tiles |
| WeaponSkins | `WeaponSkins.API` | A build whose API has `Agents`, `MusicKits`, `GetImageKey` | `weaponskins.*` | Item art, captions and the equipped outline |
| ShopCore | `ShopCore.API.v2` | 1.1.9 | `shopcore.*` | Prices in a value column, item art, and owned, equipped or unaffordable state |
| MapChooser | `MapChooser.Api.v1` | 1.5.0 (contract 1.1.0) | `mapchooser.*` | The vote as cards or poll rows with each option's share, the vote's clock and turnout, the map menu as the sidebar, and the pool with player limits, nominations and why a map can't be nominated |
| VIPCore | `VIPCore.Api.v1` | 1.2.0 (contract 1.1.0) | `vipcore.*` | The membership over its perk cards, choices as cards, admin pages as a table beside the selected player's grants |
| Admins | `Admins.Menu.V2`, with `Admins.Admins.V2`, `Admins.Bans.V2`, `Admins.Comms.V2` | 2.0.0 | `admins.*` | The players as a roster beside the selected player's sheet, a player's actions as cards and tiles, a punishment's steps over chips with the punishment as it stands and the message the player will read, bans and comms as a table beside the selected record, and the vote ballot |

- **MapChooser:** the ballot gets each option's share as `text=\t45;vunit=%` and
  `bar=`, its votes as `note=`, and `chosen` with a `Your vote` tag on the
  player's pick; the vote's clock and turnout are chips, plus a turnout line in
  the `maps-rows` view. Once the player votes, a read-only tally under the same
  menu id stays open until the vote ends. The pool pages show each map's player
  limits, its nominations (`Nominated`, `Your pick`) and why it can't be
  nominated now (`Played recently`, `Needs 10 players`), under a chip with the
  map's time left. Every map carries its kind as a badge under its name
  (`Stock map`, or `Workshop map` in blue), on every page and in the list. A
  stock map shows its screenshot and logo, another map its prefix over stripes
  in that prefix's tone.
  Start a vote shows the pool the same way: a ticked map is outlined and
  glows, and the start button is an action card whose line is its hint, which
  MapChooser words (what it does, or why no vote can start now).
  MapChooser's map menu (`mapchooser.hub`, `!maps`) is the sidebar, its Vote
  entry marked with a red dot while a vote runs.
- **ShopCore:** prices go out as `text=\t500`, so other renderers still read
  `Neon Tracer [500]` and a long name never truncates its price. An item's
  picture is its `ShopItemDefinition.ImageKey`; any other item shows its
  category's icon from `Sidebar.Icons`.
- **Admins:** every row's `Tag` is an `Admins.Contract` record (a player, a
  record, an action, a step's choice) and each page's `IMenu.Tag` its subject:
  the player, the record, the record list, the punishment draft, or the
  running `IAdminVote`. What changes while a page is open (a player's team,
  K/D and ping, what is on them, the tools' state, the vote's tally) is read
  live from the API; a player's history is read once and kept for 15 seconds.
  A poll of up to three options draws as `mix-ballot`, a longer one as the
  list with each option's share, and a map vote as `maps-board`.

What a page needs in order to be recognised is renderer-agnostic:

- **A row's `Tag` is the item it stands for:** a leaderboard entry, a weapon's
  definition index, a finish's paint kit, a shop item, a map's name, a ballot
  option, a VIP perk, choice, user or grant.
- **An item's own page carries that item as `IMenu.Tag`.** A page opened from a
  row without one is matched to the row it was opened from.
- **The API exposes what a page shows** as read-only members: the ladder and a
  player's snapshot, a sell price, an item's art and whether the player owns it,
  a map's pool entry, the live vote counts.

A plugin that is missing, older than the table says, or publishes another
contract under the same key is skipped with one log line, and its menus draw as
plain rows. A draw that throws falls back to the plugin's own rows for that page
and logs once.

CustomHUD compiles against each plugin's contract DLL in `lib/`
(`Private=false`, never shipped); `scripts/update-contracts.ps1` rebuilds and
copies them. Update a plugin and CustomHUD together when a contract changes.

Every provider stays optional only while each type in `CustomHUD.dll` loads
without its contract: SwiftlyS2 finds the plugin class with
`Assembly.GetTypes()`, so a single type that needs a missing assembly stops
the whole plugin from loading. A type may hold a provider's interfaces and
classes in fields, but must not implement or derive from a contract type, or
hold a struct built over one. An `async` method that awaits a provider's task
compiles to exactly such a struct, so integrations hand the provider's task to
`TryLoad` or continue it with `ContinueWith`. CI runs
`tools/SoftDependencyCheck`, which loads the published plugin with no provider
installed and names any type that would not load.

To ask for another plugin, open a
[plugin menu support request](https://github.com/shmitzas/CustomHUD/issues/new?template=plugin-menu-support.yml).

## What a Page Sends

A page is built from the same components as any other menu. These directives, in
a `MenuRawNode("customhud.panorama", "…")` on the entry, place it and say what
it is; other renderers ignore them. Directives are separated by `;`, so a text
value runs to the next one. A value that becomes part of a class name (`at=`,
`view=`, `group=`, the tones, `glyph=`, `tagglyph=`, `swatch=`) must be a plain token: up to
24 lowercase letters, digits and `-`.

**Placement:**

| Directive | Effect |
|-----------|--------|
| `at=<region>` | Where the entry goes: `banner` (above the list, 5 slots), `detail` (the side pane, 28), `detailbar` (the pane's buttons, 3), `foot` (under the list, 4), `aside` (a column left of the list, 8), `strip` (a row of small slots over the page, 10). Absent, unknown or full: the list (48) |
| `chip[=tone]` | A chip beside the title instead of a row, up to 3. The text's first segment is its value, the one after a tab its caption |
| `head` | The first entry carrying it is not drawn: its tab-separated text becomes the column heads, up to 6 |
| `section` | A heading across the page that also starts a new line of cards; the text after a tab shows beside it (`Perks\t8 of 10 on`) |
| `group=<token>` | Adds `grp-<token>` to the entry. On a `section`, every entry after it carries the group up to the next section, and the section itself does not |
| `view=<token>` | On any entry, the first one wins: draw the page as that view |
| `inline` | Joins the entry straight before it as a second button on its slot, such as a Sell beside an owned item; in the list, `detail` and `foot` |
| `fold` | The entry takes no slot on a mouse page, because another entry draws what it says (a hero card showing the lines it sums up). Keyboard pages still draw it, and the cursor still counts it |

**Content and style:**

| Directive | Effect |
|-----------|--------|
| `sub=`, `unit=`, `tag=`, `tag2=`, `note=`, `mark=` | More text: a line under the name, a small word with it (`kills`), two pills, a note at the right, and the text in the mark square |
| `badge=` | A small boxed label under the name, on its own dark ground so it reads on any row, the list's cursor row included (a map's kind). In the list it opens the second line and a `sub=` goes under it |
| `tone=`, `subtone=`, `badgetone=`, `tagtone=`, `tag2tone=`, `notetone=`, `metertone=` | Colours, from the tone table below |
| `glyph=<icon>` | An icon from `customhud_icons.vcss` (`ico-<icon>`) |
| `vglyph=<icon>` | The value's own icon, such as a price's coin, so `glyph=` can be the entry's. Without it the value shows `glyph=` |
| `tagglyph=<icon>` | An icon inside the `tag=` pill, before its words (a lock on `Played recently`) |
| `vunit=<text>` | The value's unit after it, smaller and muted (`45` then `%`). A layout with no place for it appends it to the value |
| `swatch=<colour>` | A colour dot: `red`, `blue`, `green`, `yellow`, `purple`, `orange`, `pink`, `cyan`, `white`, `black`, `team` or `random` |
| `avatar[=text]` | The mark square with the name's initial in a colour fixed per name, or the given text |
| `rating=N` | A CS Rating badge in its band colour |
| `pips=K/N` | N pips, K of them lit; N up to 20 |
| `nays=M` | With `pips=`, M more lit after the K as no votes, in red |
| `bar2=N` | A second meter, 0-100, in the list |
| `lead` | The leading entry: a gold `Leading` tag unless it has a tag of its own. On MapChooser's pages the single highest `bar=` leads without it, and on MapChooser's and MixScrims' pages the tag carries a trophy |
| `map` | Reads a text entry's label as a map, for its art |
| `hint` | On a header entry: its text is the page's footer line for a mouse player (`Click a map to vote`), in place of the mode's hint. Keyboard pages keep their keys |
| `live=<menu id>` | On a header entry with no text: the sidebar entry whose `SubmenuComponent.Id` is that id gets a red dot, for a page with something running on it (MapChooser's vote) |

**Behaviour:**

| Directive | Effect |
|-----------|--------|
| `verb=<word>` | The list's legend word for the entry: `select`, `pick`, `open`, `switch`, `edit`, `change`, `vote` or `apply` |
| `primary` | The page's main action, for views that draw one |
| `info` | The entry is a fact, not an action: drawn as text, with no key and nothing to click, even though core lets the cursor stop on it (a `TextComponent` is focusable so a long page can scroll past it). A plain `TextComponent`, or a subclass that does nothing when activated, gets it without asking |
| `submenu` | A `›` at the right, in views that draw one. Added for you on a `SubmenuComponent`; a button that opens a page can send it |

**Tones** (defined in `locker.vcss`):

| Part | Directive | Tones |
|------|-----------|-------|
| The value | `tone=` | `good` `bad` `gold` `info` `faint` `main` `ct` `t`; `warn` on a chip |
| The line under the name | `subtone=` | `good` `bad` `gold` `info` `faint` |
| The badge | `badgetone=` | `good` `bad` `gold` `info` `accent` |
| The pills | `tagtone=`, `tag2tone=` | `good` `bad` `gold` `info` `accent` `ct` `t` `dark` `legend` |
| The note | `notetone=` | `good` `bad` `gold` `info` `faint` `accent` |
| The meter | `metertone=` | `good` `bad` `gold` `accent` |

Views add tones of their own and use `tone=` for a whole card: MapChooser's
`extend`, `aim`, `awp` and `surf`, `ready` for its Start a vote button when it
can start, and the rank tiers below.

On MapChooser's pages a label is read as a map on an entry to click, a `bar=`
share or a `map` entry. A stock map (`de_mirage`) shows its screenshot and logo;
any other `prefix_name` label (`awp_lego_2`) shows its prefix, `AWP`, in the
mark square.

**Ranks:** any page can draw a rank the way Ranks' `RankMode` does:

| Rank mode | Directive | Tier tones |
|-----------|-----------|------------|
| Premier | `rating=N` | `csr0`-`csr6`, Premier's bands |
| Competitive | `image=rank/skillgroupN` (0-18) | `r1`-`r7`, Silver to Global Elite |
| Wingman | `image=rank/wingmanN` (0-18) | `r1`-`r7` |
| Custom ladder | `image=rank/levelN` (1-10) | `l1`, `l2`, `l4`, `l8`, `l10` for levels 1, 2-3, 4-7, 8-9 and 10 |

Send the tier tone with the image. It is how a chip and the side list know the
picture is an emblem: they draw it on the row rather than on the dark tile a
render sits on, and rasterize it at the size shown rather than at the 32×13 its
file declares.

Ranks' own `Custom` mode writes an icon number from `Scoreboard.CustomRank*`, and
the scoreboard loads `skillgroup<number>` from the player's icon addon. CustomHUD
loads the same file, but it can show an image only through a class its Workshop
addon defines, since the HUD entity carries text and classes, never an image
path. It has one for `0`-`18` and for the set Ranks' wiki uses as its example,
`skillgroup26283401`-`26283500` (Workshop `3340561607`, 100 ranks). A number
outside them shows the rank's colour and no icon; another set is a line in
`$CustomIconSets` in `scripts/generate-locker-art.ps1` and in
`RankArt.CustomIconSets`, then a republished addon.

A ladder longer than one page draws as the rank road page by page, its heading
saying which ranks are shown (`100 ranks · 48–94 shown`). In the keyboard list a
Ranks page is no longer than any other list's page (core's `MaxVisibleLines`):
My rank adds the road only where three ranks of it fit, leaving the rest to
`Show all ranks`, whose ranks are cards like MapChooser's maps.

## Richer Rows (Optional, No Dependency)

A component can address this renderer directly and still degrade cleanly
everywhere else:

```csharp
new MenuRawNode("customhud.panorama", "icon=7;danger")
```

| Directive | Effect |
|-----------|--------|
| `text=` | Text only CustomHUD shows, at the raw node's place in the row. A component drawing `de_mirage` that appends `text=\t7` gets `7` in the value column here, while every other renderer still shows `de_mirage` |
| `icon=N` | A rarity dot or tile tint, `lvl-N` |
| `image=<key>` | A CS2 econ image, see below |
| `tint=<token>` | With `image=`: adds `tint-<token>` to the image's panel, for art the game colours at runtime. The WeaponSkins layouts wash `spray-1` to `spray-19` in the graffiti tints |
| `type=<label>` | A short category label (`type=Rifle`) written to the profile's `Row.TypeVar`, adding `has-type`; layouts without a `TypeVar` ignore it |
| `bar=N` | A share, 0-100: the list fills the row behind its text, the classic lists and the mouse grid draw a thin meter, the WeaponSkins locker none |
| `chosen` | The player's own pick, such as their vote, in the accent. The mouse layouts also outline a chosen tile or card in a 2px accent edge |
| `danger` | A red row |
| `disabled` | A dimmed row |
| `marquee` | A scrolling label |
| `hero`, `inline`, `track=` | [Card pages](#card-pages) |

The [page directives](#what-a-page-sends) work here too. Unknown directives are
ignored, so a payload aimed at a newer CustomHUD never breaks an older one.
Without any raw node, a row's text still splits on tabs:
`"AK-47\t500 XP\t7"` → label, right-hand value, icon badge.

`image=` takes a CS2 econ image key: the game's own
`panorama/images/econ/<key>_png.vtex_c` path without prefix and suffix, e.g.
`image=default_generated/weapon_ak47_cu_ak47_asiimov_light`. It shows only in
layouts that declare `Row.ThumbPanel` and include `econ_thumbs.vcss`, plus
`econ_stickers.vcss` for `stickers/` keys. The image bytes stay in the player's
game install; the addon ships one CSS class per key, regenerated after a CS2
update with `scripts/generate-econ-thumbs.ps1`.

## Card Pages

A profile with a `Card` (the built-in WeaponSkins locker has one) draws a page
whose first row carries `hero` as an inspect card instead of a grid. CustomHUD
adds these to WeaponSkins' properties pages itself; another plugin can send
them:

| Directive | Put it on | Effect |
|-----------|-----------|--------|
| `hero` | The first body row | The page becomes a card: the row's text is the name, `type=` the rarity line, `image=` the picture, `icon=N` or the text colour (through `ColorIcons`) the rarity colour |
| `inline` | Any later row | Joins the field above it: a slider becomes that field's bar, a toggle a switch at its right, a button or submenu a `×` at its right. The locker draws no inline input or choice |
| `track=<preset>` | An inline slider | Adds `track-<preset>`; the locker styles `wear` with the five exterior bands |

Every other row becomes a field, up to `Card.Slots` (8 in the locker). Value
components need nothing more: CustomHUD marks each one with its kind, on/off
state, slider position and formatted number (`kind=`, `on=`, `pct=`, `num=`),
which a custom component may also send itself. A click on a field activates its
row; the bar under a field is 51 targets, each setting `n / 50` of the slider's
range snapped to its `Step`. Other layouts ignore all of this and list the rows
as usual.

Append the raw node into the component's own line instead of wrapping it,
because CustomHUD splits the value off the line's first text node:

```csharp
public override MenuNode Render(IMenuComponentRenderContext context)
{
    var node = base.Render(context);
    var raw = new MenuRawNode("customhud.panorama", "inline;track=wear");
    return node is MenuLineNode line
        ? line with { Children = [.. line.Children, raw] }
        : MenuLineNode.Of(node, raw);
}
```

## Panels API

Other plugins draw the [configured panels](Configuration.md#panels) through the
`CustomHUD.Panels.v1` shared interface. Reference
`resources/exports/CustomHUD.Contract.dll` with `Private=false`: CustomHUD
exports it, and a shipped copy would be a second type identity that never
resolves.

| Member | Description |
|--------|-------------|
| `Profiles` | Profile names from the server's config (`versus`, `ready`, `timeout`) |
| `Show(playerId, profile, variables, classes?, durationSeconds?)` | Draws or updates a panel on the next tick. Variables missing from the call keep their value; non-null `classes` replace the previous set. `durationSeconds`: `null` = the profile's default, `<= 0` = until hidden. Returns `false` only for an unknown profile |
| `Hide(playerId, profile)` | Hides one panel for one player |
| `HideAll(playerId)` | Hides every panel the player has open |
| `HideEverywhere(profile)` | Hides one profile for every player |

Calls are safe from any thread.

## Known Limitations

- **Mouse and numbers input is silent.** `MenuSounds` is played by core's input
  dispatcher, and no public API exposes the configured sound names.
- **Text colour is not drawn as such.** A `MenuTextStyle` colour reaches a
  layout only as a class, through a profile's `ColorIcons`. Use `danger` or an
  icon badge to mark a row.
- **`SendCenterHTML` cannot be captured**, and `Core.MenusAPI` menus never reach
  the renderer.
- **Out of scope:** toasts, countdowns and other HUD elements.
