# CustomHUD — Port to the Core.Menu Renderer Model

How the private Rampage CustomHUD becomes a public, general-purpose SwiftlyS2
plugin. Read this before changing anything under `src/Rendering` or
`src/Input` — most of it records constraints that are invisible from the code.

## 1. What changes, and why

The private plugin owns the whole menu stack: consumers call
`ICustomHudMenuApi`, CustomHUD builds the model, paginates, tracks the cursor,
dispatches input and draws Panorama. Every consumer therefore has to take a
compile-time dependency on `CustomHUD.Contract.dll` and be rewritten against it.

SwiftlyS2 1.5.0 shipped `Core.Menu`, a component-based menu framework where
renderers are pluggable. Components emit a tree of `MenuNode` values rather
than markup, so **a renderer draws every component — including components from
plugins it has never seen**. A server owner switches renderer in
`configs/menus/menus.jsonc`:

```jsonc
{ "Menus": { "Renderer": "customhud.panorama" } }
```

So the public plugin inverts the dependency. CustomHUD becomes a **renderer**,
not a menu API. Consumers depend on nothing; they keep using `Core.Menu` and
get Panorama output for free.

| | Private (today) | Public (this port) |
|---|---|---|
| Consumer dependency | `CustomHUD.Contract.dll` | none |
| Consumer code change | full rewrite | none |
| Who owns the model | CustomHUD | `Core.Menu` |
| Who owns pixels | CustomHUD | CustomHUD |
| Opt-in | per call site | one config line |

### Requires SwiftlyS2 ≥ 1.5.0

`IMenuRenderer` / `IMenuService` / `MenuActionContext` do not exist in 1.4.x.
The published docs still describe the menu system as "beta branch only" — that
is **stale**; the API is in the 1.5.0 stable package.

### Scope

Menus go through `Core.Menu` and need no dependency. Standalone panels
(versus banner, ready tally, timeout) have no core abstraction, so they ship
through a small contract DLL instead: `CustomHUD.Contract.dll` in
`resources/exports`, shared interface key `CustomHUD.Panels.v1`
(`ICustomHudPanels`). A consumer resolves it in `UseSharedInterface`, calls
`Show(playerId, profile, vars, classes?, durationSeconds?)` / `Hide` /
`HideAll` / `HideEverywhere`, and degrades to chat or CenterHTML when the key
is absent. Profiles live in config `Panels`, so each panel's layout is
swappable like a menu's. Toasts, countdowns and floaters are not ported.

Intercepting core's own `SendCenter` / `SendCenterHTML` / `SendAlert` /
`SendNotify` to take them over for free — the menu trick, applied to HUD — does
not work, and the reason is not visible from the managed API. In
`src/server/players/player.cpp`, `CPlayer::SendMsg` wraps its
`PostEventAbstract` call in `bypassPostEventAbstractHook = true`, so
`HookServerMessage<CUserMessageTextMsg>` never sees it; `CenterHTML` emits no
user message at all, storing `centerMessageText` + `centerMessageEndTime` and
re-firing a game event on a timer. A renderer hook keyed on `MessageType` would
have to come from upstream — `CPlayer::SendMsg` is a single choke point, and
`CenterHTML` is the best first candidate because it is already modelled as
state rather than a one-shot.

## 2. Architecture

```
Core.Menu  ──Render(ctx)──▶  PanoramaMenuRenderer      [background thread]
                                   │
                                   ├─ FrameComposer     pure: MenuFrame → FrameSnapshot
                                   │                    (more than one page: deferred, see below)
                                   ├─ diff vs shadow    no delta ⇒ return
                                   └─ Scheduler.NextTick(Action)
                                               │
                                               ▼
                                        MenuEntityHost  [game thread]
                                        PageFrame + FrameComposer (deferred frames)
                                        writes CCSCustomHudLayout

OnCustomHudClicked ──▶ MenuClickBridge ──▶ session.SetFocus(i)
                                        └▶ component.HandleActionAsync(ctx)
```

### Thread contract

`IMenuRenderer.Render` runs on a **dedicated background thread**; `Clear` runs
on the **game thread**. Every `CCSCustomHudLayout` write is thread-unsafe.

Therefore:

* `FrameComposer` touches **no** engine state. It reads `IMenuRenderContext`
  and produces an immutable `FrameSnapshot`. It must stay a pure function —
  the moment it reads a pawn, the renderer is a crash waiting for load.
* **`IPlayer.SteamID` is a native call** (`NativePlayer.GetSteamID`, guarded by
  `ThrowIfDisposed`), so it is *not* safe here either. `SteamIdCache` keeps a
  slot→SteamID map refreshed on the game thread and the renderer reads that.
  `IPlayer.PlayerID` is a plain managed field and stays safe.
* All engine writes happen inside one `Core.Scheduler.NextTick(Action)`.
* Use the `Action` overload. Every `Func<Task>` overload of `NextTick` is
  documented upstream as *"Never use this! … breaks the synchronization safety
  this function gives."*

`PlayerRenderState` documents per-field thread ownership. The short version:
`Shadow`, `Entity` and `ScrollKey` are game-thread-only, `LastComposed` is
render-thread-only, `Pending` (a snapshot, or a deferred compose for a menu
that has to be paged) and `Rows` are published render→game, `Queued` is
interlocked. Nothing may be added outside those rules without re-reasoning the
coalescer.

### Why paged menus compose on the game thread

Core hands `Render` one window of at most `MaxVisibleLines` (≤ 32) lines, and
that window **slides**: `MenuFrameComposer.ResolvePage` moves it one row at a
time as focus passes its edge and backfills the last page, so its pages overlap
and never turn. CustomHUD instead cuts fixed pages from the top
(`PageBudget.Measure` is core's `Measure` plus a six-choice limit in numbers
mode) and draws the page holding focus. A mouse grid with `ShowAllRows` cuts
`RowSlots`-sized chunks the same way, without the line cap; with `PageTiles`,
a list that overflows gives up two slots per chunk to the page cards.

Rows outside core's window can only be rendered from the menu's components,
and that runs consumer code — `Render`, `IMenuRowSource.GetRows`, `IsVisible`,
`LineCount` — which core itself only ever runs on the game thread. So such a
frame publishes the render context instead of a snapshot, and the apply builds
the page with `PageFrame`, mirroring core's composer: row sources expanded,
focus resolved by index on that expansion, component renderers consulted
first, selection markers added. Only the page's rows are rendered — 128 of a
1,404-sticker list, not 1,404. A row that throws is drawn blank and logged once
per component type. Core's translator is captured per player by
`PanoramaComponentRenderer`, the one place it is visible, because
`GlobalLocalization` is internal. Every activation CustomHUD synthesizes passes
that translator, as core's router does — components read core's keys through it
(`menu.input.chat_hint`) — and a new one re-resolves the player's CustomHUD
strings, since core keeps one per language.

Most menus never take this path. When core's frame starts at row 0 and already
holds every choice within the page budget (`PageFrame.FitsOnePage`), it *is*
the only page, and it is composed on the render thread as before. A Chat-input
menu always is: core resolves `!N` against its own window
(`session.PageSelectables`), so a different page would make the numbers lie.

The render-thread `SameContent` check does not apply on the deferred path; the
game-thread diff is what keeps an unchanged frame free. The cost is one page
render per invalidation, roughly what core spends on its own window.

The page size comes from `MenuLayout.MaxVisibleLines`, which core keeps in an
internal class. `CoreMenuSettings` reads it from the same
`configs/menus/menus.jsonc` with the same parsing (clamped `1..32`, default
`5`), located from `Core.Configuration.BasePath`, and reloads it live.

Panorama keeps a scroll panel's offset when its content changes, so a new list
would open scrolled. In a `ShowAllRows` layout, when the (menu, first row) pair
changes without a respawn, the apply sends one frame with no rows and
invalidates; the content collapses, the offset clamps to the top, and the real
list follows a tick or two later.

### Why one hop instead of the Async setters

`SetDialogVariableStringForPlayerAsync` and friends each schedule onto the game
thread **individually**. A 20-row menu would queue 20+ continuations per frame
per player. Batching into one `NextTick` makes it one.

### Why coalescing is mandatory

`TextProvider` is evaluated every frame, `NeedsRedraw` is polled once per tick
per visible component, and `ProgressBarComponent` self-invalidates. Without a
gate, a fast invalidation loop queues several applies for the same tick. An
`Interlocked` flag plus a latest-wins `Pending` field bounds it to **one queued
action per player per tick**, regardless of invalidation rate.

The gate is sound because the apply clears `Queued` **before** it reads
`Pending`, while the render writes `Pending` **before** it sets `Queued`. A
render that observes `Queued == 1` therefore knows the pending apply has not
read yet and will pick up the newer snapshot; otherwise it queues a fresh one.

`ForceRepublish` closes the other half. The render-thread early-out would
otherwise suppress a frame forever once it had been composed, even though the
apply that was supposed to draw it failed — a failed spawn, or a throw
mid-write. The game thread sets the flag **and invalidates the session** on
either failure: the flag alone is not enough, because a static menu may never
be invalidated again and so would never call `Render` to consume it. `Shadow`
only advances after a clean apply, so it always describes what is really on
screen.

### Entity cleanup must be recoverable, not just logged

A failed transmit release leaks per-viewer blocks against an index the engine
recycles; a failed despawn leaves a menu on a player's screen, unrestricted and
therefore visible to everyone. Neither is acceptable to log and forget, so
`MenuEntityHost` keeps failed handles on a retry list drained on every
subsequent despawn, on shutdown, **and on a 5-second timer** — without the
timer, a release-then-failed-despawn would wait for an unrelated despawn that
might never come.

`Spawn` takes the entity handle before anything that can fail, so every failure
after creation is retryable. Forgetting an entity is the only outcome with no
recovery path.

### Why the render thread must not diff against the shadow

The render-thread early-out compares against `LastComposed`, which that thread
owns — **never** against `Shadow`, which the game thread owns and which lags.

Diffing against the shadow loses frames. Concretely: shadow is `A`, the menu
changes to `B` (published, apply queued), then back to `A` before the apply
runs. `Shadow.DiffTo(A)` is empty, so the render returns without publishing —
and the queued apply draws `B`. The display is then permanently `B` while the
menu says `A`. The applied-state diff belongs on the game thread only.

### Why diffing is mandatory

We cannot stop third-party components invalidating sessions. We can make an
invalidation that changes nothing cost one node walk and zero engine calls.
`FrameSnapshot.DiffTo` returns only changed `(panel, var, value)` and
`(panel, class, on)` entries; an empty delta writes nothing.

With a fixed row ladder, the most common interaction — moving the selection —
diffs to exactly **two class toggles**.

### What forces a respawn rather than an update

`StrLayout` is fixed once an entity has spawned, and `DiffTo` only ever visits
writes the *new* frame produces — so anything that changes the **set** of
writes, rather than their values, has to replace the entity. `RequiresRespawn`
covers all three cases:

| Trigger | Why |
|---|---|
| layout path changed | player switched nav mode; `StrLayout` is immutable |
| profile fingerprint changed | a live config edit shrank `RowSlots` or renamed panels, so rows the new frame never mentions would keep their last content |
| SteamID changed | slots are recycled; a new occupant must not inherit the previous player's shadow |

## 3. Entity model (carried over unchanged)

A client binds its HUD panel to a player slot when the panel is **created**,
using whatever its HUD follows at that moment — the observed player while
spectating — and never re-binds. A spectator watching the owner at spawn time
therefore renders the owner's per-player state, and no amount of per-slot
hiding reaches them.

Consequences, all load-bearing:

1. **One entity per viewer**, spawned on session open, despawned in `Clear`.
2. **Transmit-restricted to the owner** (`HudTransmit.RestrictTo`). This is the
   only bystander guard. A per-slot `hidden` stamp cannot be layered on top: a
   dead or spectating owner binds the panel to the *observed* player's slot, so
   the stamp would hide the menu from the player it belongs to — input still
   routes, so it reads as "clicks work, nothing drawn".
3. **Global writes only** (`SetDialogVariableString` / `SetHasClass`, not the
   `...ForPlayer` variants). Global state carries no slot, so it renders
   whichever slot the single remaining viewer bound to.
4. **There is no persistent global transmit deny.** `SetTransmitState(false)`
   loops over currently-registered slots; a reconnecting client gets a fresh
   bit vector. Every restricted entity must be re-denied for each joining
   client.
5. **Blocks are held against an entity index and the engine recycles indices.**
   Always release before despawn, or a leaked block eventually hides an
   unrelated entity.
6. The VXML `hidden` default is not enough. A client with no entry of its own
   resolves the *global* state — and a class the server never wrote has no
   global entry either, so such a client renders **another player's** entry.
   Write the hidden default explicitly at spawn.
7. **Core does not call `Clear` when a player disconnects mid-menu** — it drops
   the session instead. The plugin therefore handles `OnClientDisconnected`
   itself. Without it the entity survives with nothing left to despawn it, and
   because `DenyForJoiningClient` skips the entity's own slot, the next player
   to take that slot starts receiving it.
8. **Unload must quiesce the renderer before disposing its registration.**
   Consumer sessions hold a direct reference to the renderer instance, so
   dropping the registry handle revokes neither those nor an in-flight
   background render. `Shutdown()` sets a flag checked on both threads, so a
   late render cannot respawn an entity after cleanup.

## 4. Mapping `MenuNode` onto a Panorama row ladder

`Frame.Body` arrives focus-resolved, with invisible components already removed,
holding core's sliding window. Unless that window is already the menu's only
page or the menu takes Chat input, it is replaced by the fixed page holding
focus (§2); core still owns focus itself. A frame wider than `RowSlots` (core's
window on a Chat menu) is walked with a focus-following viewport of
`RowSlots` rows.

Each body entry is flattened to one row:

| From | To |
|---|---|
| `MenuSelectionNode.Focused` | `selected` class on the row panel |
| `MenuSelectionNode.Number` | key-hint variable (`!n` under Chat input) |
| `MenuTextNode` runs | row label text |
| first `MenuTextNode.Style.Color` | optional `ColorIcons` lookup for the row's rarity class; explicit icon tokens win |
| text after a `\t` | right-hand value variable |
| text after a second `\t` | icon badge token |
| `MenuBlankNode` | row rendered empty, not hidden |
| `MenuRawNode` for our id (the configured `RendererId`, or `customhud.panorama`) | structured payload, overrides the above |
| `MenuRawNode` for another id | ignored |
| anything unknown | `ReportUnsupported`, never throw |

Rows past the body length get the `hidden` class and nothing else. Every other
class — the row-count step, `lvl-N`, `econ-*`, `has-*`, `selected` — is written
only while on, and the diff clears whichever one the next frame no longer
names; only `hidden` is ever written as off. A free slot therefore costs one
write, and the entity's networked class-name table, which sends each distinct
name once, stays small. Thumbnail classes are a 12-hex SHA-256 token of the
image key for the same reason.

The tab convention is carried over from the private plugin so existing
shop-style labels keep working, and it costs a consumer nothing to adopt.

A profile with a `Card` flattens one kind of page differently. When the first
body entry carries a `hero` raw node, the composer hides every row slot, sets
`card-mode` on the shell and maps the entries onto the card instead: the hero's
name, rarity line, picture and `lvl-N`, then one field per entry, where an
entry marked `inline` joins the field before it (a slider as that field's bar,
anything else as its aside control). Value components need no raw node of
their own: `PanoramaComponentRenderer` appends `kind=`, `on=`, `pct=` and
`num=` to every one, which is what lets a field draw a switch or fill a bar.
An `inline` entry with nothing to join (no field before it, or that field
already holding one of its kind) starts a field of its own instead of
vanishing, a slider that starts a field gets its own bar, and entries past
`Card.Slots` fields are not drawn.

A `DividerComponent` reaches the composer as nothing but a `divider` marker
from `PanoramaComponentRenderer`, and the composer drops it before laying out
rows, tiles or card fields. Since a divider is never focusable, dropping it
leaves the selectable-row counts the input bridge relies on unchanged. The
sidebar reads root dividers from the components themselves.

### Capability limits that are not ours to fix

* `MenuLayout.MaxVisibleLines` in `menus.jsonc` caps core's window. It is
  clamped 1–32 and **defaults to 5**, and core exposes it to no plugin.
  CustomHUD reads the same file (§2) and pages at that size; a `ShowAllRows`
  profile renders its rows itself and ignores it.
* Menu-level `WithAction` handlers are **not reachable from a click**.
  `MenuActionDescriptor` exposes `Id` / `DefaultKey` / `Label` / `Order` /
  `ShowInFooter` and no handler. Footer action chips are therefore decorative
  in mouse mode; component rows are fully clickable. Upstream feature request.
* An unregistered renderer id makes a menu **fail in `Build()`**. A server-wide
  `"Renderer"` pointing at a CustomHUD that failed to load breaks every menu on
  the server. That is an operator configuration concern, not something the
  renderer can defend against.

Mouse-only profiles resolve before ordinary `Overrides`: operator
`MouseLayouts` first, then the built-in rules, each by exact id, then longest
trailing-`*` prefix. The built-in `weaponskins.*` profile
uses `menu_skins_mouse.vxml_c` without changing WeaponSkins' menu construction.
It is code rather than a `config.jsonc` default because SwiftlyS2 never
back-fills into a dictionary: the `MouseOverrides` key it replaced held a copy
generated with whatever row count shipped then, and a server kept paging at
that count after the layout grew.
Card thumbnails come from an `image=<econ key>` directive mapped onto classes in
the generated `econ_thumbs.vcss` and `econ_stickers.vcss`, which reference the
game's own VPK textures.
Its optional sidebar reads only static root `SubmenuComponent` labels and
visibility flags plus root `DividerComponent`s, never `TextProvider`,
`EnabledWhen` or `VisibleWhen`. Activation goes through the component's public
action pipeline using a root-menu session, so a lazily built child adopts the
correct parent. The active entry is the one whose component id equals the id
of the root's child the player is under (WeaponSkins sets them equal), else
whose label equals that menu's title: neither menus nor sessions record which
component opened them.

`Sidebar.AutoOpen` skips the root page in mouse mode. `ApplyOnGameThread`
composes the root's frame, sees `RowMap.AutoOpenItem`, and dispatches that
entry instead of drawing; a synchronous factory has opened the child before
the call returns, so the root never reaches the screen. Each root session is
opened past once (`PlayerRenderState.RootHandled`), and the input bridge marks
a root it opens to reach another entry so the two do not race. Core's
`Close()` reopens the parent, which for a top-level page is the skipped root:
`Drop` sees that page closing (core clears the renderer before reopening the
parent) and the root's next frame closes the root rather than reopening the
page the player just left. The footer Close button walks `Close()` up the
stack, one level per parent, bounded at 32.

## 5. Input

Keyboard navigation stays **entirely core's**. Core resolves keys from
`menus.jsonc` and dispatches to components; reimplementing it here would
produce two competing cursors.
CustomHUD adds only what core has no concept of:

* **Mouse** — `OnCustomHudClicked` gives a `ButtonId`. Parse the row index out
  of it, move the cursor onto that row, then synthesize:

  ```csharp
  var ctx = new MenuActionContext {
      Action = MenuActions.Select, Key = MenuKey.Mouse1,
      Session = session, Translate = translate };
  await session.FocusedComponent.HandleActionAsync(ctx);
  ```

  `MenuActionContext` is a public `readonly record struct` with `required` init
  properties; `IMenuComponent.HandleActionAsync` is on the public interface.
  This is a supported seam, not a trick.

  `OnCustomHudClicked` only fires while the layout entity has **input capture**
  enabled (`CCSCustomHudLayout.SetInputCaptureEnabledForPlayer`), which is also what
  puts a cursor on screen. Capture is part of `FrameSnapshot` (`CaptureInput`,
  true only in mouse mode) and diffed like any other frame field, so it is
  switched on the game thread when the mode changes rather than at spawn: a
  mode switch with no Mouse profile keeps the same entity, and a spawn-time flag
  would never be set. A respawn resets the shadow to `Empty`, which re-applies
  it. Only `<Button>` panels report clicks, so clickable rows must be buttons in
  **both** profiles — Mouse falls back to the Default layout when it has no
  profile of its own.

  For a Mouse or Numbers player on a Buttons-input menu,
  `MenuInputBridge.OnTick` masks Tab in
  `player.Pawn.MovementServices.Buttons.ButtonPressed`. SW2 1.5's native
  `OnGameFramePlayerHook` invokes this after simulation but before
  `CPlayer::Think` samples those buttons. No Tab event reaches the menu router,
  so the original session, submenu stack and pending input survive without
  close callbacks or sounds. The current `Pawn`, not `PlayerPawn`, also covers
  spectators. Other buttons and client user commands are left untouched.
  Checked against SW2's source from v1.5.0 to v1.5.1-beta.22: the hook calls
  the original `GameFrame`, then the managed tick, then `Think`, which reads
  `m_pButtonStates[0]` — the very field `ButtonPressed` writes. `OnKey` logs a
  warning when a Tab press still arrives for a slot masked that tick, so a
  broken assumption shows up in the server log, not only as a closed menu.

* **Numbers** — `sw_1`..`sw_9`, plus `css_N` / `ms_N` raw aliases so a config
  migrated from CounterStrikeSharp or MatchZy keeps working. 1–6 select, 7/8
  page, 9 closes, so a numbers page holds at most six choices. Core's own
  `Chat` input method (`!1`, `!0`) is chosen per *menu*, so it cannot serve a
  per-*player* preference.
* **Page keys and buttons** — 7/8, the mouse `<<` / `>>` and a `PageTiles`
  `«` / `»` card call `SetFocus` on
  the first choice of the neighbouring page, which the composer records in the
  row map (`PreviousPageFocus` / `NextPageFocus`, wrapping at both ends). Core
  has no page API; the renderer simply draws whichever page now holds focus.
  Stepping by the page's choice count instead lands mid-page on a short last
  page and, through `MoveFocus`'s wrap, on the wrong page from the first one.

Both paths bail when the session belongs to a different renderer, or a click
lands on a CenterHTML menu.

### Row index is not focus index

A click reports a **physical row**; `SetFocus` indexes **visible focusable
components**. Body rows include non-focusable entries (spacers, plain
labels), so the two numbers differ as soon as a menu contains one — clicking a
button could activate a different button, and on page two row 0 could activate
the first item in the menu.

The composer therefore records a `RowMap`: per-row focusability, derived from
whether that body entry carried a `MenuSelectionNode` (only focusable entries
do), plus which row holds the cursor, the session's `FocusedIndex` at that
moment, and the row-panel prefix of the profile used. The input bridge counts
selectable rows between the cursor and the target and calls
`MoveFocus(signedSteps)` — relative movement, which is unambiguous, rather than
an absolute index whose space is not pinned down. Key-hint numbering counts
selectable rows for the same reason.

Three details there are load-bearing:

* **The prefix comes off the `RowMap`, not a fresh config lookup.** The bridge
  cannot know which nav-mode profile or per-menu override the renderer
  resolved; guessing meant mouse clicks were matched against `option_` while
  the mouse profile had rendered `tile_`, so every click was dropped.
* **The step count is corrected by `session.FocusedIndex - rows.FocusedIndex`.**
  The map describes the last *applied* frame, which can be a tick or two
  behind — a keyboard press between compose and click moves core's cursor
  without the map knowing, and an uncorrected delta then lands that far off.
* **The map carries the owning session.** Row, back, page and sidebar clicks
  are rejected after a session transition until its own frame applies.
  Control ids and sidebar targets also come from this applied map, not from
  freshly resolved config. The footer back button calls `session.Close()`
  (parent return or root close); the close button closes every level; the root
  shortcut calls `root.Open(player)` only outside that root.

A card page maps entries, not row slots: `Focusable` and `FocusedRow` cover
every body entry, and `RowMap.Card` lists, for each field, the entry behind its
body, its aside control and its bar. `field_N_hit` and `field_N_aside` resolve
to an entry and take the same focus-then-select path as a row click.
`field_N_seg{S}` focuses the bar's slider, checks it is still visible and
enabled, and calls `SetValue` with `S / (Segments - 1)` of its range snapped to
`Step`; `SetValue` runs `OnChanged` and invalidates the session exactly as a
key press does.

The map is published from the **game thread, at apply time**, not at compose
time. A map published early would resolve clicks against rows the player cannot
see yet, because the frame describing them has not been drawn.

> **Known limitation.** `RowMap.FocusedRow` comes from the composed
> `MenuFrame`, while `FocusedIndex` is read from the live session during the
> same compose. A keyboard input landing between those two reads pairs an old
> row with a newer index, and the correction is then wrong by that much. The
> window is sub-tick and needs simultaneous keyboard and mouse input; the worst
> outcome is activating a neighbouring row. Closing it properly needs a focus
> generation counter on the frame, which core does not expose.

### Known gap: sounds

`MenuSounds` is played by core's input dispatcher. Synthesized activations
bypass it, and no public API exposes the configured sound names. Mouse and
numbers input is silent for now. Deferred deliberately.

## 6. Capturing native HUD text

Menus are not the only thing CS2 draws. `IPlayer.SendCenter`, `SendAlert`,
`SendChat` and friends all go through native `CPlayer::SendMsg`, which emits a
**`CUserMessageTextMsg`** whose `dest` is the `MessageType` value. One hook
therefore covers every one of them:

| `MessageType` | `dest` | API |
|---|---|---|
| `Notify` | 1 | `SendMessage(MessageType.Notify, …)` |
| `Console` | 2 | `SendMessage(MessageType.Console, …)` |
| `Chat` | 3 | `SendChat` |
| `Center` | 4 | `SendCenter` |
| `ChatEOT` | 5 | `SendMessage(MessageType.ChatEOT, …)` |
| `Alert` | 6 | `SendAlert` |

### The hook choice is load-bearing

`CPlayer::SendMsg` wraps its send in `bypassPostEventAbstractHook = true`, and
`PostEventAbstractHook` — the hook behind `[ServerNetMessageHandler]` /
`HookServerMessage<T>` — begins with `if (!bypassPostEventAbstractHook && …)`.

**So the ordinary server hook sees none of SwiftlyS2's own HUD text**, which is
precisely the traffic worth capturing. It would catch a `TextMsg` from the game
itself or from a plugin sending raw net messages, and nothing else.

`HookServerMessageInternal<T>` hooks `CServerSideClient::SendNetMessage`
instead — further down the stack, where no bypass exists — and hands over the
target `playerId`, which `PostEventAbstract` only carries as a recipient mask.
That is the hook `NativeTextCapture` uses, and swapping it for the
non-internal one silently disables the whole feature.

### `SendCenterHTML` is not a TextMsg at all

`MessageType.CenterHTML` (100) never reaches `PostTextMsg`. `SendMsg`
short-circuits, buffers the string with an expiry, and `CPlayer::UpdateCenterMessage`
re-sends it **every tick** as the `show_survival_respawn_status` game event
(`loc_token`), serialised into `CMsgSource1LegacyGameEvent` — also behind the
bypass flag.

Capturing it would mean hooking `CMsgSource1LegacyGameEvent` on the internal
pipeline and filtering by event id, then tolerating a per-tick re-send. It is
deliberately **out of scope**: the same buffer also carries the legacy
`Core.MenusAPI` centre-HTML menu text, so a naive filter would eat those menus
too — and plugins on that API are the ones this port is asking to migrate
anyway.

### Behaviour

Everything is off by default (`NativeText.Enabled`, plus a per-kind `Capture`).
Swallowing another plugin's messages is never a safe default, and a profile
pointing at a layout the player's addon lacks would make them vanish with
nothing drawn in their place.

* `Suppress: true` returns `HookResult.Stop` — the native HUD never sees it.
  `false` draws in CustomHUD *and* lets it through, which is useful while
  authoring a layout.
* Any exception in the handler passes the message through untouched. A capture
  bug must not cost a player their text.
* The hook is registered **programmatically and only when enabled**, because an
  installed hook sits on the path of every outbound message for every player.
* Text is copied out of the message wrapper before anything is deferred — the
  wrapper is temporary and must not outlive the callback.
* Rendering goes to `HudSurface`, deliberately **not** the menu renderer: a menu
  is a composed frame with focus and paging, this is a string with an expiry,
  and sharing one entity would have them fight.

## 7. Per-player nav modes

**Renderer and input method are per-menu, not per-player** — `IMenu.Renderer`
exists, `IMenuSession.Renderer` does not. Per-player variation therefore lives
*inside* the renderer: `Render` receives `context.Player`, resolves that
player's nav mode, and picks a layout profile for it.

So one registered renderer id serves every mode, and a mode change is a
despawn + respawn of that player's entity with a different `StrLayout`.

Mouse-mode layouts are free to be a completely different shape (grid of tiles
rather than a list) because only *presentation* is per-player. A renderer
cannot substitute different *content* per mode — the frame is composed before
it is handed over, deliberately, so a renderer cannot break a plugin's logic.

## 8. Configuration

Two files, different owners:

* `configs/menus/menus.jsonc` — **core's**. Renderer selection, key binds,
  `MaxVisibleLines`, sounds, and `InputMethod`, the nav mode a player starts in
  until they choose one.
* `configs/plugins/CustomHUD/config.jsonc` — **ours**. Layout profiles.

Every layout reference is a config value. A server owner swaps in their own
Panorama layout — different art, same function — by pointing a profile at a
different file and naming its panels, variables and classes. Nothing about
panel naming is hardcoded, which is what makes "hot-swap the look" real rather
than a slogan. See `src/Config/CustomHudConfig.cs`; defaults reproduce the
layouts this plugin ships.

Player preferences use the optional `Cookies.Player.v1` shared interface when
the public SwiftlyS2 Cookies plugin is loaded. CustomHUD stores navigation mode
under `customhud.navmode` and scale under `customhud.scale`. Without Cookies,
preferences remain in memory only. If Cookies is added after server startup,
restart the server because plugin exports are discovered at startup.

## 9. Asset delivery

Panorama layouts are **client-side** and ship through a dedicated CustomHUD
Steam Workshop addon, subscribed via AddonsManager — not through the plugin
zip, and not through any server's own addon.

Second-order consequence: configurable layout references only work for layouts
the player's addon actually contains. A profile pointing at a file the addon
does not ship renders **nothing**, and the server cannot detect it. Ship a
generous versioned catalog in one addon, and document the failure mode.

## 10. Migration for consumers

1. Port from `Core.MenusAPI` to `Core.Menu`. The old API is a separate pipeline
   that never reaches `IMenuRenderer`, so a plugin left on it is unaffected by
   the renderer — and the two do not share the "one menu per player" rule.
2. Drop any `CustomHUD.Contract` reference and its `OnSharedInterfaceInjected`
   wiring.
3. Nothing else. Do not reference CustomHUD.

Optional richness, for plugins that want it: emit
`new MenuRawNode("customhud.panorama", payload)` from a component for rows that
deserve custom treatment, and generic nodes otherwise. Three lines, no
dependency, degrades cleanly on every other renderer. ShopCore, MapChooser,
Ranks, VIPCore and Admins have page views in Mouse mode;
[What a Page Sends](PluginIntegration.md#what-a-page-sends) lists the directives those views read. A plugin with a
public API needs neither: CustomHUD draws Ranks, ShopCore, MapChooser,
WeaponSkins, VIPCore and Admins from their APIs and leaves their menus plain
([Provider Integrations](PluginIntegration.md#provider-integrations)).

## 11. Deliberately not ported

| Dropped | Why |
|---|---|
| `ICustomHudMenuApi` + builder | replaced by `Core.Menu`; its existence is the problem this port solves |
| Pagination, cursor, submenu stack | core owns them now |
| `AddInput` chat capture | core owns it via `IMenuSession.CaptureChat` |
| Freeze-on-open, close-on-death | not expressible through `IMenuRenderer`; revisit if core grows session hooks |
| Old HUD element API (toasts, countdowns, floaters) | out of scope; the narrower `CustomHUD.Panels.v1` replaces the parts consumers used, see §1 |
