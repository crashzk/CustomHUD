# CustomHUD: guide for coding agents

CustomHUD draws other SwiftlyS2 plugins' menus with CS2 Panorama. Its costliest bugs are layouts
that compile cleanly and then draw wrong in game, so most of this guide is about catching those
before a player does. It indexes the docs: read the section it names, not the whole file. The docs
are long, so grep for the heading and read that range.

## Where things are

| What | Where |
|---|---|
| Directives into classes and `{s:var}` writes | `src/Rendering/FrameComposer*.cs`, `src/Integrations/Directive.cs`, `src/Integrations/PageDraw.cs` |
| One plugin's pages | `src/Integrations/<Plugin>/`, reading that plugin's API |
| Which layout, slot counts, class names | `src/Config/` (`LockerProfiles`, `ListProfiles`, `MenuPageProfile`) |
| Layouts | `resources/panorama/layout/custom_game/customhud/`; `menu_locker*`, `menu_list*` and `menu_skins_mouse_v2` are generated |
| Sheets | `resources/panorama/styles/custom_game/customhud/`; `v2/` is generated; `econ_*` and `locker_art` come from VPK listings |
| Tokens, icons | `customhud_tokens.vcss`; `customhud_icons.vcss`, where `glyph=x` draws `.ico-x` |
| Strings | `resources/translations/`: `en.jsonc` and `lt.jsonc` always, `pt-BR.jsonc` partial |

| Task | Read first |
|---|---|
| Any sheet or layout | `docs/DesignLanguage.md` "Stylesheet rules"; `docs/PanoramaAuthoring.md` "Panorama vs web CSS, concretely" |
| A new view or a restyle | `docs/DesignLanguage.md` "Principles", "Looks" and the layout's anatomy |
| Sizes, wrapping, scrolling, images | `docs/PanoramaAuthoring.md` "Page layouts", "Scrolling grids", "Images", "The trap with no web analogue" |
| A plugin's page or a directive | `docs/PluginIntegration.md` "What a Page Sends", "Provider Integrations" |
| Input, threads, the HUD entity | `docs/PublicPort.md` |
| Something seen in game | `docs/DesignLanguage.md` "Unverified in-game"; that plugin in `docs/TestingGuide.md` |

## The loop

1. Edit the source. Never edit `styles/.../v2/` or a generated layout: change the sheet or the
   generator.
2. Regenerate what you touched: `scripts/generate-v2-sheets.ps1` after any shared sheet, the
   matching `scripts/generate-*-layout.ps1` after a slot count or an anatomy change. Use PowerShell 7
   for every script here; 5.1 writes mojibake into the layouts.
3. Run `pwsh -File scripts/audit.ps1` until it prints `PASS`. It runs the static checks, publishes
   as CI does, runs SoftDependencyCheck and the wiki check, and compiles the changed Panorama files
   when CS2 is installed. `-Quick` runs the static half while you iterate.
4. Report, as below.

A pass proves the sheets parse and keep the rules. It does not prove that a layout looks right:
`resourcecompiler` passes selectors CS2 rejects, and nothing offline draws the way CS2 does.

`scripts/audit-hook.ps1` can enforce step 3 as a Copilot `agentStop` hook; its header shows the
setup. Where it is installed, a turn that ends with CustomHUD changed and failing is sent back, so
ask a decision with your question tool rather than by ending the turn.

## Rules that past bugs wrote

Each of these reached a player at least once. The audit fails the ones marked *(audit)*.

**Panorama**

- Use only the properties in `scripts/panorama-properties.txt`; CS2 drops any other silently.
  *(audit)*
- A rule lists at most 30 selectors; past that CS2 drops the whole sheet. *(audit)*
- One border colour per panel: CS2 draws every side in the top's colour. *(audit)*
- Never put a `box-shadow` on a bar 4px or thinner. Glow and shadow draw much stronger in game than
  in a browser. *(audit, thin bars)*
- Colours are tokens from `customhud_tokens.vcss`, and no define name is a prefix of another.
  *(audit)*
- Use `/* */` comments, `animation-*` and `transition-*` longhands, and no `fill-parent-flow` in a
  margin. *(audit)*
- Keep a fixed-column `right-wrap` line at least 6px short of its container. Write the budget in
  the rule's comment, `3 x 146 + 2 x 6 = 450 of the list's 452`, and the audit checks it.
- `width: 100%` resolves to zero inside a `fit-children` parent and overruns a padded `clip` or
  `scroll` parent. Only a panel with a pixel height is known to scroll.
- An SVG rasterises once at its declared size, so give a larger draw a `background-texture-size`.
- A layout holds only `Panel`, `Label`, `Image` and `Button` with their allowed attributes, and a
  picture is a class in a sheet. *(audit)*
- Every `{s:var}` a layout shows must be written or blanked by the composer, or it shows literally.

**Design and scope**

- The layout is a dumb surface: an element with no data behind it is left out, never faked.
- Name the source of every element, the API member or menu row, before you build it. Views built
  from invented fixture data have shipped empty against real menus.
- The keyboard list is first-class. Every page must also work as the list `menus.jsonc` caps, 5
  rows by default.
- Change only what was asked. Add no accents, decorations, renames or features nobody requested,
  and keep a mouse-only request out of the keyboard layouts.
- Providers stay renderer-agnostic: CustomHUD reads their APIs and never asks them to emit
  CustomHUD-specific data. Ask before you edit a provider's repository.
- Every player-facing string goes through a key in `en.jsonc` and `lt.jsonc`. The audit checks that
  the keys the code names exist and that both languages match; it cannot see a hard-coded string.

## From mock to Panorama

1. **Options first.** Before a restyle or a new view, show 3 to 5 options as rendered images and
   wait for a pick. Draw them from what Panorama can do (tokens, one border colour, no web-only
   effects) and from CS2's own look.
2. **Map, then build.** List each element of the picked option with its data source and its slot
   part. List every state it has: mouse v2, the keyboard list and v1; default, hover, selected and
   disabled; a label longer than core's cut of about 26 characters; English and Lithuanian.
3. **Preview honestly.** A browser render checks hierarchy and colour, never layout. It cannot
   show border colour, glow strength, squish (it overlaps instead), scrolling, wrap rounding, SVG
   raster size or the game's font. List each of those your change touches as "check in game".
4. **Measure, not by eye.** Write the width budgets, and compare against the approved images by
   measurement.

## A bug seen in game

- Before you diagnose something you cannot reproduce, confirm what is running: the plugin build,
  whether a downloaded Workshop VPK shadows newer loose files, and any `config.jsonc` keys on the
  server that change what draws (`Menus`, `MenuOverrides`, `MenuLayouts`, `PageViews`).
- For client-only behaviour (scrolling, hover, the wheel, input capture), change one thing, ask for
  one in-game check, and copy an unconfirmed fix to no other view.
- Diagnose before you blame the user's config.
- When the user confirms an item from "Unverified in-game", move it into the rules where it
  belongs, and add an audit check if one fits.

## Reporting

- What changed, by file.
- The audit's last line, `PASS (full): ...`, or what it could not run and why.
- What the user must check in game: the menu, the mode and the state for each item, never just
  "test it".
- What to deploy: `build/CustomHUD.zip`, and whether the Workshop addon must be republished, which
  any change under `resources/panorama/` needs.
- The docs you updated, or why none needed it.

"Compiled N of N" is never evidence that a layout loads or looks right.

## Docs to keep current

| Change | Update |
|---|---|
| A sheet rule learned in game | `docs/DesignLanguage.md` "Stylesheet rules", and a check in `scripts/audit.ps1` if one fits |
| An engine fact | `docs/PanoramaAuthoring.md` |
| A view, directive, region or slot count | the layout's anatomy in `docs/DesignLanguage.md`; `docs/PluginIntegration.md` |
| What to check in game | `docs/TestingGuide.md` |
| A setting | `docs/Configuration.md` and the wiki's Configuration page; `docs/verify-wiki.ps1` checks defaults |
| A layout name, or the steps a plugin's integration takes | the wiki's Layouts, Layout-Variations and Integrating-A-Plugin pages |

Comment only what the code cannot say: a constraint that lives elsewhere, a deliberate omission or
a chosen value. Default to one line, and never restate a doc.
