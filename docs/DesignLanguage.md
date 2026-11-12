# Design language

What the starter layouts in `resources/panorama/` look like, why, and the exact
contract each one fulfils. Related docs:

- [PanoramaAuthoring.md](PanoramaAuthoring.md): authoring, compiling and hot-reloading.
- [TestingGuide.md](TestingGuide.md): driving every surface in-game.
- [PublicPort.md](PublicPort.md): the config model behind the ids.

> These are **starters**: compiled-clean but not yet checked in-game. See
> [Unverified in-game](#unverified-in-game) before treating any rule here as settled.

## Provenance

Three screenshots in `deisng-reference-images/` and a HUD kit mock-up from the
earlier design research set the direction. They are third-party and Valve UI,
the folder is gitignored, and nothing in it ships: no assets, no class names and
no colours copied verbatim. The decompiled `references*/` folders and the
private CustomHUD's addon styles are not reused either.

| Image | What it contributed | Used in |
|---|---|---|
| `image3.png`: loadout locker | Near-black shell under a thin accent top edge, a sidebar whose active entry is filled, tiles with a vivid rarity glow rising from the foot, gold eyebrow over a large heading, a red close | `menu_mouse`, `menu_skins_mouse` |
| `image.png`: challenges | Orange as a solid active fill, uppercase letter-spaced titles, monodigit number column, 3px accent bar on the active row | `menu`, the locker sidebar, `hud_*` |
| `image2.png`: VIP menu | Flat rows with a right-hand value column | `menu` |
| HUD kit mock-up | The palette: navy-black panels under bright cool text, and one job per hue (gold for captions and values, blue for counters, green and red for state), hairline rows, tinted action buttons | every layout |

## Principles

1. **One accent, a few signal hues.** Interaction (selection, hover edges, the
   active entry) comes from `chudAccent*`, so re-theming a server means editing
   the accent defines in `customhud_tokens.vcss`. Every other hue has one job:
   gold for captions and values, blue for counters, red and green for state,
   the rarity ladder for item tiers. Surfaces stay a cool near-black and text a
   bright cool white, so the hues carry the colour and the contrast holds over
   a bright map. **Fills stay dim**: anything carrying a label (a button, row,
   tile, card or sidebar entry) is filled only with a tint, `Soft` at rest and
   `Dim` when active or hovered, because white text on a full-strength hue is
   unreadable. The bright hue goes into edges, bars and text instead.
2. **The layout is a dumb surface.** It styles only what `FrameComposer`
   writes: the ids, `{s:var}` variables and classes its layout profile names
   (`src/Config/`). A UI element with no data behind it, such as an
   equipped-item preview, is left out rather than faked.
3. **Keyboard modes get a list; Mouse gets a grid.** List modes show selection
   through a class. In Mouse mode it is `:hover`, because the composer never
   sets `selected` there.
4. **Everything starts hidden.** The composer reveals panels; a layout never
   assumes a row exists.
5. **Motion is short.** Transitions cover only shell entry, height changes and
   row state, and none lasts longer than 0.2s. The 4s marquee is the one
   exception. Menus despawn when they close, so there is no exit animation to
   design.
6. **Simple and clean.** One idea per element, no ornament without data behind
   it, and the same few shapes everywhere: a new page reuses the locker's
   sidebar, rows, cards and chips before it invents a part.
7. **Every sidebar entry has an icon.** The game's own white UI and equipment
   icons, coloured by `wash-color`: muted at rest, white on hover, the accent
   when active. An entry without a mapped icon still shows the default one. A
   page that edits an item's settings shows that item's icon with a small gear.

## Files

| Source | Compiled include path | Config profile |
|---|---|---|
| `styles/custom_game/customhud/customhud_tokens.vcss` | `s2r://panorama/styles/custom_game/customhud/customhud_tokens.vcss_c` | (shared) |
| `styles/custom_game/customhud/customhud_icons.vcss` | `s2r://panorama/styles/custom_game/customhud/customhud_icons.vcss_c` | `Sidebar.IconClass` tokens (`ico-<token>`), pointing at base-game icons |
| `layout/custom_game/customhud/menu.vxml` + `styles/custom_game/customhud/menu.vcss` | `panorama/layout/custom_game/customhud/menu.vxml_c` | the classic list: `"menu"`, a `Keyboard` layout in `Menus` |
| `layout/custom_game/customhud/menu_mouse.vxml` + `styles/custom_game/customhud/menu_mouse.vcss` | `panorama/layout/custom_game/customhud/menu_mouse.vxml_c` | the tile grid: `MenuLayoutProfile.MouseGrid()`, `"menu_mouse"`, a `Mouse` layout in `Menus` (the sheet also holds the mouse layouts' shared chrome) |
| `layout/custom_game/customhud/menu_list.vxml` (generated) + `styles/custom_game/customhud/list.vcss` | `panorama/layout/custom_game/customhud/menu_list.vxml_c` | the keyboard list in the first design, `"menu_list"` (`ListProfiles.Keys()`, a plugin's flavour from `ListProfiles.Plugins()`) |
| `layout/custom_game/customhud/menu_list_mouse.vxml` (generated) + `list.vcss` | `panorama/layout/custom_game/customhud/menu_list_mouse.vxml_c` | the mouse list in the first design, `"menu_list_mouse"` (`ListProfiles.Mouse()`) |
| `layout/custom_game/customhud/menu_list_skins.vxml` (generated) + `list.vcss` | `panorama/layout/custom_game/customhud/menu_list_skins.vxml_c` | WeaponSkins' keyboard list in the first design, `"menu_list_skins"`: `menu_list` plus `econ_stickers.vcss` (`ListProfiles.Skins()`) |
| `layout/custom_game/customhud/menu_list_v2.vxml`, `menu_list_mouse_v2.vxml`, `menu_list_skins_v2.vxml` (generated) + `v2/list.vcss` + `styles/custom_game/customhud/menu_list_v2.vcss` | `panorama/layout/custom_game/customhud/menu_list_v2.vxml_c`, `menu_list_mouse_v2.vxml_c`, `menu_list_skins_v2.vxml_c` | the same three lists in the store look, the defaults in `Menus`: `"menu_list_v2"` for keyboard menus, `"menu_list_skins_v2"` for WeaponSkins', `"menu_list_mouse_v2"` for `Other`'s mouse menus |
| `layout/custom_game/customhud/hud_text.vxml` + `styles/custom_game/customhud/hud_text.vcss` | `panorama/layout/custom_game/customhud/hud_text.vxml_c` | `"hud_text"` in `Messages` |
| `layout/custom_game/customhud/hud_alert.vxml` + `styles/custom_game/customhud/hud_alert.vcss` | `panorama/layout/custom_game/customhud/hud_alert.vxml_c` | `"hud_alert"` in `Messages` |
| `layout/custom_game/customhud/hud_text_v2.vxml` + `v2/hud_text.vcss` + `styles/custom_game/customhud/hud_text_v2.vcss` | `panorama/layout/custom_game/customhud/hud_text_v2.vxml_c` | `"hud_text_v2"` in `Messages`: `hud_text` in the store look |
| `layout/custom_game/customhud/hud_alert_v2.vxml` + `v2/hud_alert.vcss` + `styles/custom_game/customhud/hud_alert_v2.vcss` | `panorama/layout/custom_game/customhud/hud_alert_v2.vxml_c` | `"hud_alert_v2"` in `Messages`: `hud_alert` in the store look |
| `layout/custom_game/customhud/menu_teampick.vxml` + `.vcss` | `panorama/layout/custom_game/customhud/menu_teampick.vxml_c` | `"menu_teampick"`, a `Keyboard` layout (`MenuOverrides` for one menu) |
| `layout/custom_game/customhud/menu_shop.vxml` + `.vcss` | `panorama/layout/custom_game/customhud/menu_shop.vxml_c` | `"menu_shop"`, a `Keyboard` layout |
| `layout/custom_game/customhud/menu_skins.vxml` + `.vcss` | `panorama/layout/custom_game/customhud/menu_skins.vxml_c` | `"menu_skins"`, a `Keyboard` layout |
| `layout/custom_game/customhud/menu_skins_mouse.vxml` + `.vcss` | `panorama/layout/custom_game/customhud/menu_skins_mouse.vxml_c` | the first WeaponSkins locker: `MenuLayoutProfile.WeaponSkinsLocker()`, WeaponSkins' `"menu_skins_mouse"` |
| `layout/custom_game/customhud/menu_skins_mouse_v2.vxml` (generated) + `styles/custom_game/customhud/menu_skins_mouse_v2.vcss` | `panorama/layout/custom_game/customhud/menu_skins_mouse_v2.vxml_c` | WeaponSkins' `"menu_skins_mouse_v2"`, the WeaponSkins locker in CS2's store look (`MenuLayoutProfile.WeaponSkinsLockerV2()`), the default |
| `layout/custom_game/customhud/menu_locker.vxml` (generated) + `locker.vcss` | `panorama/layout/custom_game/customhud/menu_locker.vxml_c` | the page locker in the first design: `"menu_locker"` for ShopCore, MapChooser, Ranks (with its `weaponstats.*`, `customcombos.*`, `prestige.*` modules), VIPCore, Admins and MixScrims (`LockerProfiles`, through `BuiltInLayouts`); the view per menu from `PageViews` |
| `layout/custom_game/customhud/menu_locker_v2.vxml` (generated) + `v2/locker.vcss` + `styles/custom_game/customhud/menu_locker_v2.vcss` | `panorama/layout/custom_game/customhud/menu_locker_v2.vxml_c` | the same plugins, the same page, in the store look: `"menu_locker_v2"`, the default |
| `styles/custom_game/customhud/v2/menu.vcss`, `menu_mouse.vcss`, `locker.vcss`, `locker_<plugin>.vcss`, `list.vcss`, `hud_text.vcss`, `hud_alert.vcss` (generated) | `s2r://panorama/styles/custom_game/customhud/v2/<sheet>.vcss_c` | the `v2` layouts' copies of the shared sheets, in the store palette (`scripts/generate-v2-sheets.ps1`) |
| `styles/custom_game/customhud/locker_shop.vcss`, `locker_maps.vcss`, `locker_ranks.vcss`, `locker_vip.vcss`, `locker_admins.vcss`, `locker_mix.vcss` | `s2r://panorama/styles/custom_game/customhud/locker_<plugin>.vcss_c` | one plugin's views, each scoped by `view-<name>` on the shell |
| `styles/custom_game/customhud/locker_art.vcss` (generated) | `s2r://panorama/styles/custom_game/customhud/locker_art.vcss_c` | map screenshots and logos, rank emblems, replacement rank icon sets and level badges as `econ-<token>` classes |
| `layout/custom_game/customhud/panel_versus.vxml` + `.vcss` | `panorama/layout/custom_game/customhud/panel_versus.vxml_c` | `"panel_versus"` in `Panels.Versus`: vars `team1 team2 score1 score2 status detail`, class `live` |
| `layout/custom_game/customhud/panel_ready.vxml` + `.vcss` | `panorama/layout/custom_game/customhud/panel_ready.vxml_c` | `"panel_ready"` in `Panels.Ready`: vars `title count state hint`, classes `is-ready` / `not-ready` |
| `layout/custom_game/customhud/panel_timeout.vxml` + `.vcss` | `panorama/layout/custom_game/customhud/panel_timeout.vxml_c` | `"panel_timeout"` in `Panels.Timeout`: vars `title team timer detail`, classes `team-ct` / `team-t` |
| `styles/custom_game/customhud/econ_thumbs.vcss` (generated) | `s2r://panorama/styles/custom_game/customhud/econ_thumbs.vcss_c` | `econ-<token>` classes (hashed keys) for `image=`, pointing at base-game textures |
| `styles/custom_game/customhud/econ_stickers.vcss` (generated) | `s2r://panorama/styles/custom_game/customhud/econ_stickers.vcss_c` | the same for sticker art; only the two WeaponSkins lockers, `menu_list_skins` and `menu_list_skins_v2` include it |
| `images/custom_game/customhud/faceit_lvl1..10.svg` | `panorama/images/custom_game/customhud/faceit_lvlN.vsvg_c` | badges for `icon=N` rows in `menu_teampick` |
| `images/custom_game/customhud/muted.svg` | `panorama/images/custom_game/customhud/muted.vsvg_c` | `ico-mute`: a white copy of the game's muted icon, which is painted red and so ignores `wash-color` |

The menu variants share `menu.vxml`'s variable vocabulary (`title`, `footer`,
`option_N`, `option_N_key`, `option_N_right`), so any menu can be pointed at
any of them. Panel shells use id `shell`, start `hidden`, and set
`hittest="false"`. A team-pick row puts a FACEIT level on its icon with a
`MenuRawNode(rendererId, "icon=N")` marker, and its ELO after a tab in the
label.

Images go in `images/custom_game/customhud/`. A PNG is referenced as
`url("s2r://panorama/images/custom_game/customhud/x.png")` and an SVG as `.vsvg`; see
[PanoramaAuthoring.md](PanoramaAuthoring.md#images).

### Stylesheet rules

`scripts/audit.ps1` fails a change that breaks any rule below it can check.

- **Tokens come first** in every layout's `<styles>`. A `@define` is visible
  only to stylesheets included after it, and Panorama has no `@import`.
- **csgostyles is not included**, so every Label sets its own `font-family`,
  `font-size` and `color`.
- **Keyframes stay in the sheet that uses them.** Whether a keyframe can be
  looked up across sheets is untested.
- **No define name may be a prefix of another.** Use `chudTileBase` /
  `chudTileHover`, never `chudTile` / `chudTileHover`.
- **No colour literal outside `customhud_tokens.vcss`.** A sheet names a token;
  a colour a design needs is a step on a hue's ladder (see [Tokens](#tokens)),
  so the whole look re-themes from that one sheet.
- **A view joins a look; it never restates one.** A surface more than one view
  draws is a single rule listing every selector that uses it (see
  [Looks](#looks)). A view adds its selector to that list and keeps only its
  own geometry, arrangement and type.
- **No rule lists more than 30 selectors**, or about 1600 characters of them.
  CS2 drops the whole sheet when one rule lists too many (48 failed in game;
  Valve's own sheets stop at 30), and `resourcecompiler` passes it. Write a
  ladder as a rule per line, and when a look's list outgrows the bound,
  continue it in a second rule with the same body.
- **Thin bars take no `box-shadow`.** In-game, a 6px blur round the locker's 2px
  rarity bar drew as a thick, bright halo, far stronger than a browser shows it.
  State and rarity bars stay crisp lines; the colour comes from gradients.
- **Side menus cast a light shadow.** A shadow is drawn behind the whole panel
  unless it is `hollow`, so it darkens through the translucent surface as well
  as below it. The list's `0 16 40` drop in `chudShadowDrop` read as a heavy
  smear in-game; the side shells (`.ls`, `.chud-menu`) take half their geometry
  in `chudShadowDim`, and the centred lockers keep their full drop.
- **One border colour per panel.** Panorama keeps each side's width but draws
  every side in one colour, the top's when the top has one. In-game the menu's
  orange left edge came out grey under a grey hairline, and the lockers' grey
  sides came out orange. A second colour needs a child panel.

## Tokens

Each hue is one ladder, so a step means the same thing in every sheet. A family
defines only the steps its sheets use, and a translucent step is always its
family's own colour at a lower alpha.

| Step | Alpha | Use |
|---|---|---|
| `Base` | opaque | the hue itself: edges, bars, text, glyphs |
| `Strong` | opaque | the hue lightened, for text on a dark ground |
| `Half` | 50% | a strong edge, a hover fill |
| `Dim` | 35% | a quiet edge (pills, borders, meter tracks), and a label's fill when active or hovered |
| `Soft` | about 12–22% | a fill at rest: pills, chosen rows, glows behind art |
| `Wash` | about 8% | a tinted area: a column, a stage, a quiet box |
| `Fade` | 0% | a gradient's end in the hue's own colour, so the gradient never passes through grey |

| Group | Defines |
|---|---|
| Fonts | `chudFontBody`, `chudFontMedium`, `chudFontLight`, `chudFontCondensed`, `chudFontMono`, `chudDigitsRegular`, `chudDigitsBold` |
| Accent | `chudAccentBase #ff8a1f`, `chudAccentStrong #ffb066`, then `Half`, `Dim`, `Soft`, `Wash`, `Fade`: selection, the active entry, hover edges, the player's own pick. Swapping the ladder's colour re-themes every layout (pink alternative: `#f8a8d8`, `Strong #ffc4e6`) |
| Gold | `chudGoldBase #f7bd45` with `Half`, `Dim`, `Soft`, `Wash`, `Fade`: captions, values, prices and the leader; `chudGoldDeep #d99a2b` for a table's column heads and a row's type caption |
| Info | `chudInfoBase #5aaeff`, `chudInfoStrong #8cc4ff`, `Half`, `Dim`, `Soft`, `Wash`, `Fade`: counters and share meters |
| Success | `chudSuccessBase #8fd14f`, `chudSuccessStrong #a9e07a`, `Half`, `Dim`, `Soft` |
| Danger | `chudDangerBase #ff4f3f`, `chudDangerStrong #ff8a7f`, `Dim`, `Soft`, `Wash`, `Fade` |
| Caution | `chudCautionBase #f2d43a`, between good and bad |
| Teams | `chudTeamCtBase #5aaeff` and `chudTeamTBase #f5973a`, each with `Strong`, `Dim`, `Soft`, `Fade` |
| Legend | `chudLegendBase #c08cff`, `chudLegendStrong #d2b0ff`, `Dim`, `Soft`: the top tier |
| Extend | `chudExtendBase #ffa500`, `chudExtendStrong #ffc266`, `Half`, `Dim`, `Soft`, `Wash`, `Fade`, and `chudExtendShade #2a1c0c` to `chudExtendDeep #120c06` for its art: MapChooser's Extend option, in the orange MapChooser draws it in |
| Maps | `chudMapAwpBase` / `Deep`, `chudMapAimBase` / `Deep`, `chudMapSurfBase` / `Deep`, `chudMapOtherBase` / `Deep`: a map without a picture, by its prefix, under the `stripes.svg` tile |
| Surfaces | `chudSurfaceBase #0b0e13f2` for HUD panels (95% opaque); `chudSurfaceRaised #141a24dc` and `chudSurfaceDeep #0b0e13d9`, every menu shell's gradient, top then foot (the lists and both lockers, about 86%); `chudSurfaceEdge #2a3443` for hairlines and borders, with `chudSurfaceFade` its transparent end; `chudSurfaceRow #161d27` for a row or control at rest; `chudSurfaceHover #222c3a` |
| Tiles | `chudTileBase #1c2431` and `chudTileDeep #0f141b`, a card's gradient, top then foot; `chudTileHover #283346`; `chudTileFade` |
| Shadow and glass | `chudShadowDrop #000000b8`, `chudShadowText #000000cc`, `chudShadowDim`, `Soft`, `Fade`: black glazes under art and text. `chudGlassDim #ffffff26`, `Soft`, `Wash`: white glazes over art and bars |
| Text | `chudTextMain #f5f7fa`, `chudTextMuted #a5afbd`, `chudTextFaint #6d7787`, `chudTextCream #ffd28a` (HUD panels), `chudTextInk #1a1203` (dark text on a solid pill) |
| Rarity | `chudRarity01`…`10` and `chudTint01`…`10`, where each tint is the rarity colour at alpha `59`; avatars take the tints too |
| Wear | `chudWearFnBase #3fdc5a`, `chudWearMwBase #a6dfa6`, `chudWearFtBase #f2d43a`, `chudWearWwBase #f5973a`, `chudWearBsBase #ff7a72`, each with a `Dim` at alpha `59` for the unfilled part of the bar |
| Ranks | `chudRating0Base`…`chudRating6Base` for CS Rating's bands and `chudLevel1Base`, `2`, `4`, `8`, `10` for the level bands, each with a `Glow` at alpha `5c` |
| Swatches | `chudSwatchRed`, `Blue`, `Green`, `Yellow`, `Purple`, `Orange`, `Pink`, `Cyan`, `White`, `Black`: the colours `swatch=<colour>` names |
| Graffiti | `chudSprayTint01`…`chudSprayTint19`: the game's graffiti tints by id, which `tint=spray-N` washes spray art in. Game data, not part of the palette |
| Store | CS2's own look, which every `v2` layout draws in. Copied from the game's sheets and never themed: `chudNavBase #82d8ff` (selectedNavColor), `chudNavStrong #b4e8ff` for its text, `Half`, `Dim`, `Soft`, `Wash`, `Fade`; `chudEquipBase #0f9d58` (positiveColor), `chudEquipStrong #46c786` for its text, `Half`, `Soft`, and `chudEquipRise` / `chudEquipFade`, the green a selected store tile rises in; `chudStoreRaised #1e2d3df2` to `chudStoreDeep #14202bf2` for the panel, `chudStoreBar #00000040` for the content bar, `chudStoreEdge` for hairlines, `chudStoreRule #25282b` for a tooltip's rules, `chudStoreText #cccccc` and `chudStoreFaint #7c7c7c` for text, `chudStoreFootBase` / `chudStoreFootFade` for a store tile's foot; `chudStoreRow`, `chudStoreHover`, `chudStoreFade` and `chudStoreTileBase` / `Deep` / `Hover` / `Fade`, white and black glazes standing in for the Surfaces and Tiles in the `v2` sheet copies (see [Design v2](#design-v2)) |

The rarity ladder follows the `lvl-N` icon classes: grey, light blue, blue,
purple, pink, red, gold, orange, yellow, green.

## Looks

A look is one rule whose selector list names every place it appears, so a
card, a button or a chosen row is drawn the same in every menu. A view joins a
look by adding its selector to the list. A look the WeaponSkins locker shares
lives in `menu_mouse.vcss`, which every mouse layout includes; the rest are in
`locker.vcss`, under Looks and Text.

| Look | What it draws | Sheet |
|---|---|---|
| Tile | a card in a grid: the tile gradient in a hairline. Hover lifts it with an accent edge and a soft glow; a chosen tile keeps a 2px accent edge. A static tile lists its `:hover` with its rest state, so it does not react | `menu_mouse.vcss` |
| Eyebrow | the gold uppercase caption over a title: the grid's heading, a hero's name, a form's title | `menu_mouse.vcss` |
| Locker chrome | the sidebar, its entries and dividers, the footer bar, the nav buttons and the red close button, in both lockers and the mouse list | `menu_mouse.vcss` |
| Chosen | the player's own pick in a list, and the open tab: a flat `Soft` accent fill in a 1px accent edge | `locker.vcss`; the WeaponSkins locker's tabs in `menu_skins_mouse.vcss`, labelled in the accent |
| Bare | text with no surface; a banner's, a foot's and a bar's text are bare by default | `locker.vcss` |
| Control | a button on `chudSurfaceRow` in a hairline, accent-edged on hover; a foot's and a bar's actions are controls by default | `locker.vcss` |
| Inset control | a control on a row or card, a step darker than its ground: an inline entry's button and a row's `act=` buttons, red on hover where `danger` | `locker.vcss` |
| Chip | a control sized to its text. Its text panel and label are `fit-children` too: a `width: 100%` label inside it draws nothing in game | `locker.vcss` |
| Solid pill | a pill on a picture or hanging off a card's edge: the hue itself, with `chudTextInk` on it | `locker.vcss` |
| Badge | a small boxed label under a name, on its own `chudSurfaceBase` ground so it reads on any row, the cursor's orange and a share's fill included; its tone colours the edge and the words (a map's kind) | `locker.vcss` |
| Danger | a destructive button: a `Soft` red fill in a red edge, `Dim` on hover, red label | `locker.vcss` |
| Primary | the page's one main action | `locker.vcss` |
| Featured | the leader, a podium's first place, the tier on offer | `locker.vcss` |
| Panel | a box holding part of a page: a hero, a price, a side pane | `locker.vcss` |
| Team | a slot that belongs to one side, a pick in the order or a roster's head: the team's `Soft` fading from the left in its `Dim` edge | `locker.vcss` |
| Side | a card for one team, its colour glowing behind the emblem on a solid ground | `locker.vcss` |
| Empty | a place still to fill, outlined on no ground | `locker.vcss` |
| Target | the player a vote is about, in a red edge | `locker.vcss` |
| Stage | the backdrop of a live vote | `locker.vcss` |
| Vote pips | one per voter, lit in the accent as the votes come in | `locker.vcss` |
| Ballot pips | a team's votes, the yes votes green and the no votes after them red (`nays=`) | `locker.vcss` |
| Well | where typed text goes | `locker.vcss` |
| Poll row | a share filling the row behind its text, with a bright leading edge: blue, gold for the leader, the accent for the player's pick | `locker.vcss` |

Text styles shared the same way are a plain value, an id line, a small caption
and a table pill. Two rules hold across every look:

- **Pills are soft**: a `Soft` fill, a `Dim` edge and the hue as text. Only a
  Solid pill is the hue itself.
- **A pick is filled in a list and outlined on a card.** A chosen row, chip or
  tab takes the Chosen fill; a chosen tile or poll row takes a 2px accent edge.

## Layout anatomy

### `menu` (classic: the keyboard modes, when `Menus` names `"menu"`)

Left-middle of the screen, 420px wide: a near-black panel whose only border is
a 3px accent edge on the left (gold in `menu_shop` and `menu_teampick`), and a
bold uppercase heading spanning the panel, cut with an
ellipsis when a title is too long. Rows are 34px with a hairline-lifted
background; values read in gold,
the selected row glows from its accent bar, a disabled row fades and turns its
value red.

```text
#shell.chud-menu              hk-scale-N · hidden
├─ #title                     {s:title}, full width
├─ #options                   hk-rows-N → height N × 38px
│  └─ Button#option_N.chud-row          N = 0…19
│     ├─ .chud-row__bar                 3px state bar
│     ├─ .chud-row__key                 {s:option_N_key}
│     ├─ .chud-row__clip
│     │  ├─ .chud-row__label            {s:option_N}
│     │  └─ .chud-meter                 has-bar → 3px meter at the clip's foot
│     │     └─ .chud-meter__fill        bar-0…100 → width N%
│     ├─ .chud-row__value               {s:option_N_right}
│     └─ #icon_N.chud-row__icon         lvl-1…10 → rarity dot
└─ .chud-bar.chud-bar--list
   ├─ Button#nav_back.chud-nav--back    "<" · hidden outside Mouse
   ├─ #footer.chud-menu__footer        {s:footer}
   ├─ #page.chud-bar__page              {s:page} "2 / 5" · hidden on one page
   ├─ Button#nav_previous.chud-nav--page "<<" · hidden outside Mouse
   └─ Button#nav_next.chud-nav--page    ">>" · hidden outside Mouse
```

The list shows one fixed page at a time ([Pages](Configuration.md#pages)): at most
`MaxVisibleLines` lines (from `menus.jsonc`, or `config.jsonc` under
`"MenuConfig": "customhud"`), the menu's `ItemsPerPage` and 20 rows, six in
numbers mode. `hk-rows-N` follows the page, so a short last page
is a short panel. A body `DividerComponent` takes no row here, nor a tile or
card field in the mouse layouts; the rows already stand apart.

### `menu_mouse` (the tile grid: Mouse, when `Menus` names `"menu_mouse"`)

Centred, 550px wide, in an accent frame (2px along the top, 1px elsewhere),
three tiles per line. Four lines show at once; the grid scrolls through all 60
tiles. A bold uppercase heading sits over an accent rule that fades out; each
156×96 tile, drawn by the [Tile](#looks) look, glows from its foot in its
rarity colour.

```text
#shell.chud-locker            hk-scale-N · hidden
├─ #title.chud-locker__header
│  └─ .chud-locker__heading             {s:title}
├─ .chud-locker__divider
├─ #grid.chud-locker__grid    hk-tiles-N → height ceil(N / 3) × 104px, max 416px · scrolls
│  └─ .chud-locker__items               492px, flows down; 16px scrollbar gutter beside it
│     └─ .chud-locker__line             three tiles, flows right · 20 lines
│        └─ Button#tile_N.chud-tile     N = 0…59
│           ├─ #tile_N_icon.chud-tile__rarity lvl-1…10 → gradient tint + bottom edge
│           ├─ .chud-tile__name         {s:tile_N}
│           ├─ .chud-tile__value        {s:tile_N_value}
│           └─ .chud-meter              has-bar · bar-0…100, under the value
└─ .chud-bar                            footer bar, see below
   ├─ Button#nav_back.chud-nav--back    "<"  · hidden
   ├─ #footer.chud-bar__hint            {s:footer}
   ├─ #page.chud-bar__page              {s:page} · hidden on one chunk
   ├─ Button#nav_previous.chud-nav--page "<<" · hidden
   └─ Button#nav_next.chud-nav--page    ">>" · hidden
```

The grid has no key column, because `Profiles.Mouse.Row.KeyVar` is null.

All menu layouts end in a `.chud-bar` footer: `nav_back` at the left, the footer
hint in the middle, then the page counter, `nav_previous` and `nav_next` at the
right (the WeaponSkins locker instead ends in `nav_close`; see below). Shared
styles in `menu.vcss` give Back a 64×34 hit area (a row's height), an orange
border, a dim orange fill that deepens on hover, and the counter a
small blue label that collapses on a single page. The renderer hides the three buttons
outside Mouse mode and hides the paging buttons on single-page menus. List
variants (`menu`, `menu_shop`, `menu_skins`, `menu_teampick`) use
`.chud-bar--list`: collapsed controls leave the original keyboard hint and
spacing, while a mouse player drawn in one (a `MenuLayouts` layout) still has a way back.

### `menu_skins_mouse` (WeaponSkins, Mouse only)

The first WeaponSkins locker, still shipped: WeaponSkins' `"menu_skins_mouse"` in
`Menus` (`MenuLayoutProfile.WeaponSkinsLocker()`). The default is
[`menu_skins_mouse_v2`](#menu_skins_mouse_v2-weaponskins-mouse-only-the-default).

Centred, 1140px wide: a near-black shell in an accent frame (2px along the top,
1px elsewhere), a gold
eyebrow and a bold uppercase heading. A 194px left rail holds twelve
`category_N` buttons mirroring the root menu: entries, with a root
`DividerComponent` drawn as a 1px rule (`divider`) and the entry the player is
under drawn active (`selected`): a dim orange fill under a bright 3px accent
edge, with a bold white label. There is
no root button: the root page is skipped and the menu opens on its first entry.
The right grid shows five columns of 168×184 `option_N` cards, three rows at a
time, and scrolls through all 128 (a 16px gutter holds the scrollbar). Each row
of five is its own `.ws-line` panel, so the fifth card can never wrap away and
leave a gap. The 576px
content height leaves room for every root category without shifting the
navigation as short submenus open. Labels and values are real menu data.

Over the grid, a strip of eight `tab_N` buttons holds the second level of the
pages `Tabs.Hosts` names, WeaponSkins' categories, while the player is on one:
30px buttons sized to their uppercase labels, the open one in the Chosen look
with its label in the accent. The content area places its panels by alignment
rather than flow, so the strip takes the top of the grid's column: with it the
shell carries `has-tabs` and the grid shrinks to a fixed 534px, leaving the
locker the same size on every page. A page below a tab (a weapon's finishes)
drops the strip.

```text
#tabs.ws-tabs                           shown on a tab's own page · #shell gets has-tabs
└─ Button#tab_N.ws-tab                  {s:tab}, {s:tab_count} · selected · has-count · disabled

Button#option_N.chud-tile.ws-card       has-thumb · has-type · page-tile · chosen · inside #options > .ws-grid__items > .ws-line
├─ #icon_N.chud-tile__rarity            lvl-1…10 → glow rising from the foot
│  └─ .ws-card__bar                     rarity bar under the image (has-thumb only)
├─ #thumb_N.ws-card__thumb              econ-<token> class, 152×104 · tint-spray-1…19 washes a graffiti in its colour
├─ .ws-card__type                       {s:option_N_type}
├─ .ws-card__name                       {s:option_N}
└─ .ws-card__value                      {s:option_N_right}

.chud-bar.ws-bar
├─ Button#nav_back.chud-nav--back       "<" · hidden where there is nothing to go back to
├─ #footer.chud-bar__hint               {s:footer}
└─ Button#nav_close.chud-nav--close     "×" · leaves every level · hidden outside Mouse
```

The thumbnail is the card's main content; the type and name sit below it, over
a rarity glow rising from the card's foot and a thin rarity bar.
A row without `image=` (Reset, a property toggle, an item the game ships no art
for) is a plain action card: no `has-thumb`, the name centred and the value (a
wear, `On`) under it in gold. A list past 128 cards opens and closes its chunk with a
`page-tile` card: a 44px `«` / `»` over the page it leads to. Footer buttons
are a nav item's 34px height: Back is tinted orange and Close tinted red, and
hovering deepens the tint rather than filling the button. A `chosen` card, the
item the player has on, keeps a 2px accent outline: twice the hover edge, so a
hovered card never reads as the one worn. There is no other equipped-loadout
model.

#### Inspect card

A page whose first row carries `hero` ([Card Pages](PluginIntegration.md#card-pages)) sets `card-mode`
on the shell, which collapses the grid and shows a 668px card centred in the
content area; the sidebar and footer stay. The hero is the item's picture over
its rarity glow, a crisp rarity line, the rarity name and the item name. Each
remaining row is a field: a muted uppercase label (a fixed 256px column where a
value box follows it, so the boxes line up), then what the row's kind asks for.
A typed value shows in its box as soon as it is accepted.

```text
#card.ws-cardview                       visible only under .ws-locker.card-mode
└─ .ws-inspect                          668px: 634 inner, 16px padding
   ├─ #hero.ws-hero                     lvl-1…10 · has-thumb · has-type · hidden
   │  ├─ .ws-hero__glow                 rarity glow
   │  ├─ #hero_thumb.ws-hero__thumb     econ-<token> class, 618×148
   │  ├─ .ws-hero__bar                  96×2 rarity line, no shadow
   │  ├─ .ws-hero__rarity               {s:hero_type}
   │  └─ .ws-hero__name                 {s:hero_name}
   └─ .ws-fields
      └─ #field_N.ws-field              N = 0…7 · kind-* · is-on · aside-* · aside-on · has-track · fill-0…100 · track-wear · selected (keyboard modes) · disabled · danger · hidden
         ├─ .ws-field__row
         │  ├─ Button#field_N_hit       the field's row
         │  │  ├─ .ws-field__label      {s:field_N}
         │  │  ├─ .ws-field__box        {s:field_N_value} · input, choice and slider
         │  │  ├─ .ws-switch            toggle; the knob slides on is-on
         │  │  └─ .ws-field__chevron    "›" · action
         │  └─ Button#field_N_aside     the inline row: a switch (aside-toggle) or "×" (aside-action)
         ├─ .ws-track                   has-track: an 8px bar, five wear bands under a fill-N clip, a 4px knob
         │  └─ .ws-track__hits          51 Buttons #field_N_seg50 … #field_N_seg0, (2s + 1)% wide, seg50 the whole bar
         └─ .ws-track__legend           FN MW FT WW BS · track-wear only
```

| Class on `field_N` | Draws |
|---|---|
| `kind-input`, `kind-choice`, `kind-slider` | the value in a box beside the label |
| `kind-toggle` | a switch at the right, accent while `is-on` |
| `kind-action` | a `›` chevron; the row opens or runs something |
| `aside-toggle` | a switch beside the box; without `aside-on` the box dims to 45%, so a StatTrak™ count reads as off |
| `aside-action` | a `×` button beside the box that turns red on hover |
| `has-track` + `fill-N` | the bar, lit from the left to `N`% |
| `track-wear` | the bar split into the five exteriors at 0.07 / 0.15 / 0.38 / 0.45, with their legend below |

The wear bands are bright green (Factory New), pale green (Minimal Wear),
yellow (Field-Tested), orange (Well-Worn) and light red (Battle-Scarred), dim
where the bar is unfilled. Hovering a segment tints the bar from its start to
that point, a preview of the value a click sets.

### `menu_skins_mouse_v2` (WeaponSkins, Mouse only; the default)

The WeaponSkins locker in CS2's own store look, built from the game's sheets
(the store's tiles, the content navbar, the inspect panel's rarity bar and
tooltip rows) rather than from this palette's accent: its colours are the
[Store tokens](#tokens). Generated by `scripts/generate-skins-layout.ps1`;
never edit the vxml by hand.

A fixed 1184×740 panel on CS2's navy gradient, in a 1px hairline:

- **The rail**, 230px: a `WEAPONSKINS` brand on the content bar's band, then
  twelve `category_N` entries mirroring the root menu, each an icon and an
  uppercase label. The open entry is drawn in selectedNavColor (`#82d8ff`)
  with a 2px edge on the left and the light fading from it, the way CS2's main
  menu marks its open tab. A root `DividerComponent` is a hairline. The four
  properties pages are left out (`Sidebar.Omit`): each opens from the
  spotlight of the item it sets up.
- **The content bar**, 48px on a quarter black: Back as the game's pan arrow
  over the parent page's title (`{s:crumb}`) and a rule, the page's title, the
  header's chips joined into one line at the right (`{s:info}`: "35 guns · 10
  skinned", "61 finishes"), and the game's plain × closing every level. There
  is no footer.
- **The body**, 920×662: the tab strip, the spotlight, then the grid or the
  inspect card. Every height is fixed per state, so no page resizes the panel.

```text
#shell.chud-locker.sk                    view-<name> · has-tabs · has-spot · card-mode
├─ .sk-side
│  └─ Button#category_N.sk-nav__item     {s:category} · selected · divider · disabled · icon in category_N_icon
└─ .sk-main
   ├─ .sk-bar
   │  ├─ Button#nav_back.sk-back         ico-back, {s:crumb}, a rule · hidden where Back has nowhere to go
   │  ├─ #title                          {s:title}
   │  ├─ #info.sk-info                   {s:info}
   │  └─ Button#nav_close.sk-close       ico-exit
   └─ .sk-body
      ├─ #tabs.sk-tabs                   Button#tab_N.sk-tab: {s:tab}, {s:tab_count} · selected
      ├─ #spot.sk-spot                   lvl-N · has-thumb · has-type · has-sub · has-note · chosen
      │  ├─ .sk-spot__stage              glow, #spot_thumb (econ-<token>), .sk-status ({s:spot_note})
      │  └─ .sk-spot__info               {s:spot_sub}, {s:spot_name}, {s:spot_type}, rarity bar,
      │                                  #spot_fact_N: {s:spot_fact_N}, {s:spot_fact_N_value},
      │                                  Button#spot_link.sk-link at the foot: ico-settings, {s:spot_link}
      ├─ #options.sk-grid                128 tiles in one right-wrap flow; option_0..2 in .sk-lead
      │  └─ Button#option_N.sk-tile      has-thumb · has-type · chosen · page-tile · danger · disabled
      │     ├─ #icon_N.sk-tile__art      lvl-N: glow, #thumb_N, 2px line, dot + {s:option_N_type}
      │     ├─ .sk-tile__name            {s:option_N}
      │     ├─ .sk-tile__value           {s:option_N_right}
      │     └─ .sk-tile__check           the green circle on a chosen tile
      └─ #card.sk-card                   the inspect card: #hero on a stage, #field_N as menu_skins_mouse
```

The grid's tiles are CS2 store tiles, 170×188, five to a line: a clear tile
shaded from the foot, the picture, the name in white, and under it the caption
(`type=`: the weapon a finish is for, an agent's side, an item's kind)
uppercase in the rarity's colour, with the rarity as a 2px line along the
bottom. A tile's rarity layer, `icon_N`, holds everything the rarity colours,
so it stays drawn without one: `hidden` there only leaves it uncoloured. The
item the player has on (`chosen`) takes the store's selected look, green
rising from the foot and a green check circle; hovering lightens a tile and
lifts its picture. The tiles share one `right-wrap` flow, kept 14px short of
the line, rather than line panels, so that the categories view can rearrange
the first three.

- **The categories view** (`view-ws-categories`, `Views` in the profile) draws
  Weapon Skins' page as a mosaic: the first category 2×2, the next two stacked
  beside it, the rest in a row of four, each its cover gun large on a rarity
  glow, its title in 30px (56px for the first) and its counts under it, the
  cover's name with a rarity dot at the top left. Unlike the first design the
  page is shown, not skipped (`Tabs.AutoOpen` is -1); its pages keep the tab
  strip.
- **The spotlight** (`MenuLayoutProfile.Spot`) is a header line marked `spot`:
  on a finish page, the finish the weapon wears now, its render on a stage
  over its rarity's glow, CS2's inspect text beside it (the weapon, the
  finish's name, its grade in the grade's colour and a 6px bar), up to four
  `fact=` rows as a tooltip's table, and the status in CS2's green on the
  stage. Under it the grid holds exactly two lines. Its `link=` is a
  PopupButton at the foot of the text, a gear and `PROPERTIES` in a hairline,
  which opens the item's properties; Back from there returns to the
  spotlight's page. The 24px fact rows leave it room under four facts. A gun's
  render is 420×315 on the 258px stage, a gun filling only the middle of its
  canvas; a knife's or gloves' fills the canvas, so their finish pages
  (`view-ws-knives`, `view-ws-gloves`) draw it whole at 312×234. On the
  agents page (`view-ws-agents`) the agent on the player's side is spotlit the
  same way, its bust standing on the stage's foot right of the status.
- **The inspect card** keeps `menu_skins_mouse`'s fields and their click
  targets, restyled: the item on a 360px stage at the left, its grade and name
  under it, the fields at the right as near-clear buttons in hairlines, values
  in dark wells, switches and the track's fill in selectedNavColor. A field
  whose row carries `slot=` entries (`Card.SlotTiles`, six) is a heading, not
  a button: its name in grey capitals before a hairline, over 84×64 tiles in
  dark wells that are its only click targets, each the picture of what is on
  its slot or the game's plus on an empty one. Hovering a tile edges it in
  selectedNavColor and names what is on it over its foot; a disabled field's
  tiles still show, and do not react. The first locker declares no tiles, so
  its Stickers and Charms stay plain fields.

```text
#field_N.sk-field                        has-slots while its row carries slot= entries
├─ .sk-field__head                       in place of .sk-field__row while has-slots: {s:field_N}, a hairline
└─ .sk-slots
   └─ Button#field_N_slotK.sk-slot       K = 0…5 · has-thumb · hidden · {s:field_N_slotK} (the name)
      ├─ .sk-slot__add                   ico-add, while the slot is empty
      └─ #field_N_slotK_thumb            econ-<token> class, 76×57
```

### `menu_locker` (plugin pages, Mouse only)

Generated by `scripts/generate-locker-layout.ps1` from the same slot table as
`LockerProfiles`; never edit the vxml by hand. A 1140px shell like the
WeaponSkins locker, with a 194px sidebar and an 896×576 page; the detail pane,
when a view has one, takes 332px of the page's right side.

```text
#shell.lk                                  view-<name> · has-main · has-<region> · has-chips · has-tabs · has-head · no-sidebar
├─ #title.lk-head
│  ├─ .lk-titles                           {s:eyebrow}, then {s:crumb} › {s:title} · has-crumb
│  └─ .lk-chips > #chip_0..2               glyph, thumb, rating, {s:chip_N}, {s:chip_N_label}
├─ .lk-divider
├─ .lk-content
│  ├─ .lk-side > #category_0..11           icon, {s:category}, a live dot, {s:category_count} · live
│  └─ .lk-main
│     ├─ #tabs > #tab_0..7                 {s:tab}, {s:tab_count}
│     ├─ .lk-strip > #strip_0..9           a row of small slots: a pick order
│     └─ .lk-body
│        ├─ .lk-aside > #aside_0..7        a column left of the list: a team's roster
│        ├─ .lk-col
│        │  ├─ .lk-banner > #banner_0..4
│        │  ├─ #thead > col_0..5           {s:col_N}
│        │  ├─ .lk-listbox > #options.lk-list > .lk-items (right-wrap) > #option_0..47
│        │  └─ .lk-foot > #foot_0..3
│        └─ .lk-detail
│           ├─ .lk-detail__box > .lk-detail__body > .lk-detail__items > #detail_0..27
│           └─ .lk-detail__bar > #detailbar_0..2
└─ .chud-bar                               nav_back, footer, page, nav_previous, nav_next, nav_close
```

Every slot of every region shares one anatomy: a Panel holding a `_hit`
Button (what a click reports) with the parts inside it, and an `_inline`
Button beside it for an inline entry's own action (ShopCore's Sell). Main-list
slots also carry three `_act` Buttons over the hit's right end, one per `act=`
(MapChooser's cycle moves). A region only declares the parts its designs use:

```text
#<p>N.lk-slot                              has-<part> · tone- sub- badge- tag- tag2- note- meter-<tone> · kind-<kind> · grp-<group> · section · lead · primary
├─ #<p>N_hit.lk-hit
│  ├─ #<p>N_icon.lk-rarity                 a full-size layer under everything; views draw boxes with it
│  ├─ #<p>N_thumb.lk-thumb, #<p>N_logo.lk-logo   econ-<token>: picture, and a map's logo
│  ├─ .lk-mark > {s:<p>N_mark}             avatar, prefix art, step number
│  ├─ .lk-shade                            a view's overlay above the art
│  ├─ .lk-swatch, #<p>N_glyph, .lk-rating
│  ├─ .lk-text > {s:<p>N_type} {s:<p>N} {s:<p>N_unit} {s:<p>N_badge} {s:<p>N_sub}
│  ├─ .lk-tags > #<p>N_tagglyph, {s:<p>N_tag} {s:<p>N_tag2}
│  ├─ .lk-note, .lk-end > (#<p>N_vglyph, {s:<p>N_value}, {s:<p>N_vunit})
│  └─ .lk-switch, .lk-chev, .lk-meter, .lk-meter2, .lk-pips > 20 pips
└─ #<p>N_inline.lk-inline                  {s:<p>N_inline} {s:<p>N_inline_value}
└─ .lk-acts > #<p>N_act0..2.lk-act         #<p>N_actK_glyph: ico-<icon> · act-danger · act-off · hidden
```

Every part starts collapsed and is shown by the class written with it, so an
absent part never shows its `{s:…}` placeholder. Foot slots have no `_icon`
layer, so a view that needs a box there draws it on `.lk-shade`; detailbar
slots are a glyph and a value only. A view is a block of rules scoped by
`.lk.view-<name>` in its plugin's sheet; `locker.vcss` draws the frame, the
plain list every view starts from and most of the [looks](#looks) views join.
Fixed-column card lines keep slack: see
[PanoramaAuthoring.md](PanoramaAuthoring.md#page-layouts).

### `menu_list` and `menu_list_mouse` (the list)

Generated by `scripts/generate-list-layout.ps1` from the counts in
`ListProfiles`; never edit the vxml by hand. A 460px panel docked at the left,
its only border a 3px accent edge (the `menu` list's shape), with the path and
chips on one line over a 20px title. That line collapses when it holds neither,
as on a general plugin's root menu, so the title starts at the panel's top. The
rows reuse the locker's part classes, so `locker.vcss` styles what is in them
and `list.vcss` lays each one out as a single line: the art, the text taking
whatever is left, then the right side. WeaponSkins' keyboard list is
`menu_list_skins`, the same file with `econ_stickers.vcss` for its sticker art.

```text
#shell.ls.ls-keys | .ls-mouse              mode-<mode> · view-<name> · has-<region> · has-chips · hk-scale-N · hidden
├─ #title.ls-head                          has-eyebrow · has-crumb
│  ├─ .ls-top > .ls-path                   {s:eyebrow} › {s:crumb}; collapsed without an eyebrow, crumb or chip
│  │         > .lk-chips > #chip_0..2      the locker's chips at the list's size
│  └─ .ls-title                            {s:title}
├─ .ls-divider
├─ #options.ls-list > .ls-items > #option_0..19       (mouse: 0..23, scrolling past ten)
├─ .ls-foot > #foot_0..2                   a ballot's turnout (pips) and its rule
└─ .ls-bar                                 keys: #legend > #legend_0..3 (ls-cap, ls-cap2, ls-verb) + #footer,
                                                 #page (ls-page__num, ls-dot-1..8)
                                           mouse: nav_back, footer, page, nav_previous, nav_next, nav_close
```

```text
#option_N.lk-slot.ls-row                   kind-<kind> · has-<part> · selected · chosen · lead · danger · disabled · section · has-key
├─ .chud-meter.ls-fill                     has-bar: the share behind the row
├─ #option_N_hit.lk-hit.ls-hit             (flow right)
│  ├─ .ls-edge                             the cursor's accent edge
│  ├─ .ls-key                              {s:option_N_key}: faint in Wasd/Buttons, a keycap in Numbers (keyboard only)
│  ├─ .ls-stripe                           the compact view's rarity stripe
│  ├─ .ls-art > icon (rarity glow), thumb, mark, glyph, rating
│  ├─ .lk-text > type, name, unit, badge, sub, .ls-meter (a progress bar)
│  ├─ .lk-rating.ls-rating-end, .lk-tags
│  ├─ .ls-track                            a slider's track (keyboard only)
│  ├─ .ls-end > ‹ .ls-val (.lk-end value, .lk-note under it) pen › cycle
│  └─ .lk-switch, .lk-chev
└─ .ls-ctl                                 mouse only: track + 21 #option_N_segK, #option_N_dec, value, #option_N_inc
```

Rows are sized by what they hold: 36px, 52px with art or a second line, 62px
with a badge and a sub (three lines), 44px with a meter or as a ballot's
answer, 24px a heading. On the cursor's row the second line brightens with the
name, so it reads on the orange. In Mouse mode the row under the pointer is
drawn as the cursor's row, on a row a click acts on. A fact (a row the cursor
skips, or one marked `info` that it only passes through, as a plain
`TextComponent` is) and a meter sit on the panel with no ground, no key and no
hover; a rank on Ranks' ladder (`grp-road`) is
the exception, a card on the row's ground like a MapChooser map. The right side
is the component's control: `kind-toggle` a switch, `kind-selector` `‹ value ›`,
`kind-choice` the value and a cycle mark, `kind-slider` the track (`bar-N`
sizes it, `track-wear` bands it), `kind-input` a field with a pen,
`kind-submenu` a `›`. A row with art or an avatar reads as the thing itself:
its value is gold and it draws no `›`. A picture sits on a dark tile, as the
inventory frames a render; a rank emblem (a row with a tier tone) and a chip's
picture keep their own outline and sit on the row. A row with both an avatar
and a glyph (a global record's globe) draws the glyph before the avatar: the
page then carries `has-glyph-column`, and every avatar row on it keeps that
column so the avatars line up.

### Design `v2` (the default)

Every built-in layout in the WeaponSkins locker v2's look: CS2's own store,
inventory and main-menu sheets rather than the orange accent. The first designs
are the same names without `_v2` (`menu_locker`, `menu_list`, `menu_skins_mouse`
…), picked per plugin in `Menus` and per kind in `Messages`: the same profiles pointing at the
files before their `_v2`. Each `v2` file draws the same panels, ids and
variables as its first-design file, so one profile, one composer and every
plugin view serve both; only the frame and the palette differ.

- **One palette, mapped.** A `v2` layout reads copies of the shared sheets
  (`menu`, `menu_mouse`, `locker`, the six `locker_<plugin>`, `list`, `hud_text`,
  `hud_alert`) under
  `styles/.../customhud/v2/`, written by `scripts/generate-v2-sheets.ps1`
  with the first design's palette renamed to the [Store tokens](#tokens):
  Accent to Nav, Surface to the navy panel and white hairlines, Tile to white
  and black glazes, muted and faint text to CS2's greys. Gold, the rarity
  ladder, teams, state and the rest are shared unchanged. A `@define` is text
  substitution and a sheet is parsed once, so the copies are what let one
  rule draw in two palettes. Edit the source sheets and regenerate; never the
  copies.
- **The frame** is the WeaponSkins locker v2's: a hairline round CS2's navy
  gradient, the rail with selectedNavColor's edge and light on the open
  entry, a 48px content bar on a quarter black with Back as the pan arrow over
  the parent's title, the page's title, its chips and the plain ×. There is
  no orange anywhere; the open tab is selectedNavColor over a tenth of it.
- **Words**: a section heading, a row's type caption and a table's column
  heads are CS2's greys; gold stays for values, prices and tiers.
- **What the player has on** is CS2's green in the shop as in WeaponSkins: an
  equipped item's card rises in `chudEquipRise`, its note and its inventory
  tag are `chudEquipStrong`. Any other pick (a vote, a chosen option) is
  selectedNavColor.
- `menu_locker_v2.vcss`, `menu_list_v2.vcss`, `hud_text_v2.vcss` and
  `hud_alert_v2.vcss` come last and hold only what the store look draws
  differently: the frames, the words and the green.

#### `menu_locker_v2`

A 1160×682 shell: the 230px rail beside a 928px column holding the content
bar, the first design's 896×576 page (12px and 16px of padding round it),
and a 32px band for the page's hint and pager. A menu that is its own root
(`no-sidebar`, MixScrims) drops the rail and names the plugin at the head of
the bar instead; a docked ballot (`view-mix-ballot`) is 440 wide and sized to
what it holds, its question under the bar.

```text
#shell.lk.lk2                              the first design's classes: view-<name> · has-<region> · no-sidebar ...
└─ #title.lk2-frame                        has-eyebrow · has-crumb (every title variable resolves under it)
   ├─ .lk2-side
   │  ├─ .lk2-brand                        {s:eyebrow}: the plugin's name
   │  └─ .lk2-nav > Button#category_N.lk2-nav__item   edge, category_N_icon, {s:category}, a live dot, {s:category_count}
   └─ .lk2-col
      ├─ .lk2-bar                          {s:eyebrow} (no-sidebar only), Button#nav_back (ico-back, {s:crumb}, a rule),
      │                                    {s:title}, .lk-chips > #chip_0..2, Button#nav_close (ico-exit)
      ├─ .lk2-heading                      {s:title} again: a docked ballot's question
      ├─ .lk-content > .lk-main            the first design's page, unchanged
      └─ .lk2-foot                         #footer (the hint), Button#nav_previous, #page, Button#nav_next
```

#### `menu_list_v2`, `menu_list_mouse_v2`, `menu_list_skins_v2`

The first design's list, docked at the left and 460 wide, in a hairline round
the navy. A 34px band on a quarter black names where the menu sits (the
plugin's name, a rule, the parent's title) with the chips at its right; in
Mouse mode Back leads it as the pan arrow, and the band shows even on a root
so Close has its line. The title follows, then the rows (the cursor's row and
the pointer's lit in selectedNavColor's light rising from a 2px edge), then a
band for the keys and the page in the keyboard modes, or the hint and the
pager in Mouse mode.

```text
#shell.ls.ls2.ls-keys | .ls-mouse          the first design's classes
├─ #title.ls2-head                         has-eyebrow · has-crumb
│  ├─ .ls2-band                            Button#nav_back (mouse), .ls2-path ({s:eyebrow}, {s:crumb}), .lk-chips
│  └─ .ls2-line                            {s:title}, Button#nav_close (mouse)
├─ #options.ls-list > .ls-items > #option_N      the first design's rows
├─ .ls-foot > #foot_0..2
└─ .ls-bar.ls2-bar (keys) | .ls2-bar (mouse)     keys: #legend, #page · mouse: #footer, ‹ #page ›
```

### `hud_text` and `hud_alert` (captured native text)

```text
#shell.chud-toast | .chud-alert         kind-<destination> · hidden
└─ #text                                {s:text}
```

`hud_text` is a top-centre toast with a 3px left accent. `hud_alert` sits higher
and is larger: uppercase, centred, with accent edges top and bottom. Both style
every kind: `notify`, `center`, `chat`, `chatendofround`, `console` and `alert`.

`hud_text_v2` and `hud_alert_v2` draw them in the store look, at the same place
and size: a hairline round CS2's navy, and the first design's edges in
selectedNavColor (the toast's 2px), or in the grey, gold or red a kind gives
them. The shell's border is the hairline, so the edges are a body panel's,
inside it.

```text
#shell.chud-toast.chud-toast2 | .chud-alert.chud-alert2   kind-<destination> · hidden
└─ .chud-toast2__body | .chud-alert2__body                 the kind's edge, the padding
   └─ #text                                                {s:text}
```

Rows and tiles must be `<Button>` elements with **exactly** the row id, because
`MenuInputBridge` parses the clicked panel's id back into a row index. Every
child sets `hittest="false"` so the Button receives the click.

## States

| Class | Set when | `menu` | `menu_mouse` |
|---|---|---|---|
| `hidden` | the slot is empty | collapsed | collapsed |
| `selected` | focused, in Wasd or Buttons mode only | accent bar, accent glow across the row, accent key, bold label | never set |
| `:hover` | the pointer is over the row (input is captured in Mouse mode) | brighter row | lighter tile, accent edge, soft accent glow |
| `disabled` | the item cannot be chosen | 40% opacity, red value | 40% opacity |
| `danger` | the item is destructive | red bar, red label | red name |
| `marquee` | the item asked to scroll | the label slides inside its clip | no effect |
| `lvl-N` on the icon panel | icon token 1–10 | rarity dot | rarity tint |
| `has-bar` + `bar-N` | the row carries `bar=N` | a blue meter under the label, filled N%, and a right-aligned value column so every meter ends at the same x | a blue meter under the value |
| `chosen` | the row carries `chosen`: the player's own pick (WeaponSkins: the item they have on) | accent bold label, accent meter | 2px accent edge, accent name and meter |

An empty slot receives `hidden` and nothing else; the diff drops every class
its last occupant had (`selected`, `disabled`, `danger`, `marquee`, `has-*`,
`lvl-*`, `econ-*`, `tint-*`, `bar-*`, `chosen`).

## Nav modes

| Mode | Profile | Focus shown by | Key column |
|---|---|---|---|
| Wasd | the plugin's `Keyboard` layout in `Menus` (a `MenuOverrides` entry first): the list by default | `selected` | absolute item number; none on an `info` fact |
| Buttons | the same `Keyboard` layout | `selected` | absolute item number; none on an `info` fact |
| Numbers | the same `Keyboard` layout | nothing, because the key names the target | the key that picks the row on this page; none on a disabled row or an `info` fact |
| Mouse | the plugin's `Mouse` layout in `Menus` (a `MenuOverrides` entry first): a supported plugin's locker, else the mouse list | `:hover` | none |

In the three keyboard modes the cursor steps past an `info` fact (a label) the way it was moving,
on any page that also holds something to act on; a page of facts alone keeps it, or could not be reached.

## Payload directives

A plugin can target CustomHUD directly through
`MenuRawNode("customhud.panorama", "…")`. The directives are listed in the
[Plugin Integration guide](PluginIntegration.md#richer-rows-optional-no-dependency).

| Directive | Effect in these layouts |
|---|---|
| `icon=N` | rarity dot or tile tint `lvl-N` |
| `image=key` | econ thumbnail on `thumb_N` in the WeaponSkins lockers; ignored elsewhere |
| `tint=token` | `tint-<token>` beside the thumbnail; the WeaponSkins lockers and the list wash `spray-1`…`19` in `chudSprayTint01`…`19` |
| `type=label` | caption beside the name plus `has-type` in the WeaponSkins lockers; ignored elsewhere |
| `danger` | red bar and label, or red tile name |
| `disabled` | 40% opacity |
| `marquee` | scrolling label in `menu` |
| `text=` | text only CustomHUD shows, added where the raw node sits; `text=\t7` fills the value column |
| `bar=N` | a share filled to N%: behind the row in the list, a meter in the classic lists and the mouse grid; ignored by the WeaponSkins lockers |
| `chosen` | the player's own pick, drawn in the accent; a 2px accent outline on the tile or card in the mouse layouts, the store's green in `menu_skins_mouse_v2` |
| `hero` | on the first row: the inspect card in the WeaponSkins lockers; ignored elsewhere |
| `inline` | joins the field above in the inspect card; ignored elsewhere |
| `track=wear` | the five exterior bands on an inline slider's bar |
| `slot=pick\|image\|name` | a tile under the card field in `menu_skins_mouse_v2`, one per entry; ignored elsewhere |
| `spot` | on a header line: the spotlight in `menu_skins_mouse_v2`, with `image=`, `icon=`, `type=`, `sub=` (the line over the name), `note=` (the status), `chosen`, up to four `fact=Label\|Value` rows and a `link=entry\|pick\|label` button; left out of the title everywhere else |
| `chip` | on a header line: joins `{s:info}` in `menu_skins_mouse_v2`, a chip in `menu_locker` and the list, and is kept out of every other layout's title |

Without a raw node, the text `"Label\tValue\tIcon"` splits into the same three
slots. `menu_locker` and the list add their page directives (`at=`, `section`,
`chip`, `view=`, the tones, `glyph=` / `vglyph=`, `verb=`, `nays=`, `info`, `fold`, `hint` and the
rest); see [What a Page Sends](PluginIntegration.md#what-a-page-sends).

## Changing slot counts

The height ladders are written out in the stylesheet because Panorama has no
`calc()`. If you change `RowSlots` in config, regenerate the matching rules:

- `menu`: `.chud-menu__options.hk-rows-N { height: N × 38px; }` (a 34px row
  plus its 4px margin).
- `menu_mouse`: `.chud-locker__grid.hk-tiles-N { height: ceil(N / 3) × 104px; }`
  (a 96px tile plus 2 × 4px margin), up to `N = 12`. Past four lines the
  default 416px holds and the grid scrolls, so larger counts need no rule.

Then add or remove the matching `option_N` / `icon_N` or `tile_N` /
`tile_N_icon` elements, each row with its `.chud-meter`, so the layout declares
exactly `RowSlots` rows. Ids are
0-based. In the mouse layouts they sit in line panels in id order — five per
`.ws-line`, three per `.chud-locker__line` — with the remainder in the last.
The meter's width ladder (`.bar-N .chud-meter__fill { width: N%; }`, N =
0…100) is in `menu.vcss` and does not depend on the slot count.

The inspect card declares exactly `Card.Slots` fields and `Card.Segments`
segment buttons per field. Its two ladders are written out too: `.ws-seg-S
{ width: (2S + 1)%; }` for S = 0…49 and `100%` for S = 50, so segment S reaches
just past the point it sets (S / 50 of the bar) at any card width, and
`.ws-field.fill-N .ws-track__fill { width: N%; }` for N = 0…100. Only
`.ws-track__lit` is in pixels: it sits inside the fill, so it carries the
bar's own width, the card's 634px inner width.

`menu_skins_mouse_v2` is generated: change a count at the top of
`scripts/generate-skins-layout.ps1` together with the profile, rerun it, then
rerun `watch-panorama.ps1`. Its ladders are the same two under the `sk-`
prefix, and its `.sk-track__lit` is the card's 540px field column.

## Known gaps

These come from the data a frame carries. Styling cannot close them:

- **No switches in the classic layouts.** Every toggle reaches the layout with
  `kind=toggle` and its on/off state; the list, the lockers and the inspect
  card draw a switch from them, but the classic `menu` list and the tile grid
  show the component's on/off symbol (`✔` / `✘` unless the plugin sets text,
  as WeaponSkins does).
- **Value components are split for you; other text is not.** A toggle, input,
  slider or choice lands in the value slot, but a plain line reading
  `"Label: value"` stays in the label slot unless it carries a tab.
- **No eyebrow data.** The WeaponSkins lockers' `WEAPONSKINS` eyebrow and brand
  are static and untranslated; the generic grid has none. `menu_locker` and the lists show
  the root menu's title, or, when the plugin opens without a root, its
  `customhud.eyebrow.<plugin>`, a built-in page's name, or
  `customhud.eyebrow.default`, in that order.
- **Captured native text can contain `#localization` tokens**, which are
  rendered verbatim.
- **Multi-line native stacks flatten to one line.** `MaxVisibleLines` defaults
  to 5.

## Unverified in-game

Check these with the F6 debugger first. Each one is a plausible reading of
Valve's sources, not an observed result:

- whether the marquee animates, and whether `width: fit-children` works on a
  Label;
- whether `:hover` fires while Mouse mode captures input;
- whether `max-width` makes a Label wrap inside a fit-children shell;
- which font is used when csgostyles is not included;
- which point `ui-scale` scales from;
- whether the mouse wheel scrolls a grid while Mouse mode captures input
  (dragging the scrollbar does not depend on it);
- whether shortening a scroll panel's content to 1px with `overflow: squish noclip`
  (`chud-rewind`) clamps a scrolled grid back to the top while its rows still
  draw, which is what the renderer's scroll rewind relies on;
- whether `font-weight: bold` on the `Stratum2` family is honoured outside
  csgostyles (a production CS2 HUD relies on it), and whether a gradient to
  `chudAccentFade` stays orange rather than greying at its end;
- whether overlapping sibling Buttons give the click and `:hover` to the later
  (narrower) one, which the inspect card's wear segments rely on (the
  Cybershoke colour picker does the same);
- whether `{s:field_N}` resolves on a Label two levels below `field_N`, inside
  its `_hit` Button;
- whether the `›` and `×` glyphs exist in the Stratum2 family, and whether the
  switch knob's `translateX` transition animates;
- whether chat input reaches a text field while Mouse mode captures input;
- whether the `bar=` meter's fill animates its percentage `width` over 0.2s,
  the way the list's height does;
- whether `menu_locker`'s ~3 200 panels spawn and diff without a visible
  hitch: each page writes every variable its slots declare;
- whether `right-wrap` breaks the locker's card lines where the sheets expect,
  given the slack they keep below an exactly full line, and whether the list's
  100%-wide name puts a `badge=` row's badge on a line of its own;
- whether `wash-color` tints a map logo and the glyphs as it does elsewhere,
  and whether a radial `gradient()` draws the rank badge's tier glow;
- whether a click on a slot's `_inline` Button reaches it rather than the
  `_hit` Button it sits beside;
- whether `menu_skins_mouse_v2`'s one `right-wrap` grid breaks into lines of
  five, and the categories view into its mosaic, with the `.sk-lead` panels
  holding the first three tiles; whether `brightness` brightens a hovered
  tile's picture, as it does in CS2's own store tiles; and whether the
  spotlight's stage clips the render it centres past its edges;
- whether a card's slot tile, which selects the field's row and then the slot
  on the page that opens, both in one click, lands on that slot's page, as the
  tab and sidebar shortcuts land on theirs; and whether the spotlight's
  Properties button, which reopens the root, selects the properties entry and
  then the item on its list, lands on the item's properties with Back
  returning to the spotlight's page, its `Parent` reassigned.
- whether the list's `.ls-ctl`, laid over the row's `_hit` Button, takes the
  clicks on its steppers and track segments, and whether `max-height` stops
  the mouse list at ten rows so it scrolls;
- whether `min-width` keeps a one-character keycap (`W`, `9`) square in the
  legend, and whether `overflow: noclip` lets `mix-control`'s step connector
  reach into the next step's slot.
