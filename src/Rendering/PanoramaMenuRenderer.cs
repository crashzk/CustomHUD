using System.Collections.Concurrent;
using System.Diagnostics;
using CustomHUD.Config;
using CustomHUD.Input;
using CustomHUD.Integrations;
using CustomHUD.Navigation;
using CustomHUD.Services;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Menu;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace CustomHUD.Rendering;

// The renderer server owners select with `"Renderer": "customhud.panorama"` in
// configs/menus/menus.jsonc.
//
// THREADING -- Render runs on a dedicated background thread, Clear on the game
// thread. Every CCSCustomHudLayout write is thread-unsafe, so Render composes a
// pure snapshot and hands a single batched action to the scheduler. A menu
// longer than one page is the exception: its rows come from consumer code, so
// Render only hands over the context and the apply pages it (PageFrame).
// Nothing in the Render path may touch a pawn, a controller, an entity, or any
// IPlayer member backed by a native call -- see SteamIdCache for why SteamID is
// not read here.
internal sealed class PanoramaMenuRenderer(
    ISwiftlyCore core,
    CustomHudConfigProvider config,
    NavModeResolver navModes,
    SteamIdCache steamIds,
    MenuEntityHost entities,
    CoreTranslations translations,
    HudTranslations strings,
    CoreMenuSettings coreMenus,
    IntegrationHost integrations,
    ILogger logger) : IMenuRenderer
{
    private readonly ConcurrentDictionary<int, PlayerRenderState> _states = new();
    private readonly ConcurrentDictionary<Type, byte> _renderErrors = new();
    // Game thread. A skipped sidebar root a player is backing into; outlives
    // Drop because core reopens the parent after clearing the renderer.
    private readonly ConcurrentDictionary<int, (IMenu Root, long At)> _returning = new();
    // Game thread. Survives Drop; cleared when the player disconnects.
    private readonly ConcurrentDictionary<int, NavMemory> _memory = new();
    private readonly FrameComposer _composer = new(config, strings);
    private static readonly IReadOnlyDictionary<string, string> EmptyIcons = new Dictionary<string, string>();

    // Set before unload tears anything down. Checked on both threads so an
    // in-flight render cannot repopulate _states and respawn an entity after
    // CloseEverything has already run -- the registration handle does not
    // revoke references consumer sessions already hold.
    private volatile bool _shuttingDown;

    public string Id => config.Current.RendererId;

    public void Render(IMenuRenderContext context)
    {
        try
        {
            RenderCore(context);
        }
        catch (Exception ex)
        {
            // A renderer that throws takes the menu down for the player and
            // leaves core's session believing it drew. Swallow and log.
            logger.LogError(ex, "[CustomHUD] Render failed for {Menu}.", context.Menu.Id);
        }
    }

    private void RenderCore(IMenuRenderContext context)
    {
        if (_shuttingDown) return;

        var cfg = config.Current;
        var slot = context.Player.PlayerID;
        var steamId = steamIds.Get(slot);

        var state = _states.GetOrAdd(slot, _ => new PlayerRenderState { OwnerSlot = slot });
        var mode = navModes.Resolve(slot, steamId);
        var profile = cfg.Resolve(context.Menu.Id, mode);
        var layoutPath = cfg.ResolveLayoutPath(profile);
        var scale = navModes.ResolveScale(slot, steamId, profile);

        // Numbers keys 1-6 cannot reach a seventh row, whatever the layout holds.
        var focusables = mode == NavMode.Numbers ? NavModeResolver.NumberKeys : int.MaxValue;
        var budget = profile.ShowAllRows
            ? PageBudget.Rows(profile.RowSlots, focusables, mode == NavMode.Mouse && profile.PageTiles ? 2 : 0)
            : new PageBudget(Math.Min(context.Menu.ItemsPerPage, profile.RowSlots), cfg.PageLines(coreMenus), focusables);

        // Core resolves a Chat menu's !N against its own window, so that window is
        // what has to be drawn there.
        var coreWindow = !profile.ShowAllRows && context.Menu.InputMethod == MenuInputMethod.Chat;

        // An integration reads its provider's API, which only the game thread may touch.
        var integration = coreWindow ? null : integrations.For(context.Menu);
        // So does moving a keyboard cursor off a label (PageFrame.LabelSkip).
        var onLabel = mode != NavMode.Mouse && PageFrame.FocusOnLabel(context.Frame, cfg.RendererId);

        if (!coreWindow && (profile.ShowAllRows || integration is not null || onLabel || !PageFrame.FitsOnePage(context.Frame, budget)))
        {
            // Rows past core's window can only be rendered on the game thread, so
            // the compose moves into the apply; its diff keeps an unchanged
            // frame free there.
            state.LastComposed = null;
            Interlocked.Exchange(ref state.ForceRepublish, 0);
            state.Pending = new DeferredCompose(context, profile, layoutPath, mode, scale, steamId, budget) { Integration = integration };
            ScheduleApply(state, slot);
            return;
        }

        var snapshot = _composer.Compose(
            coreWindow ? context : new FrameContext(context, context.Frame, MenuPage.Single(context.Frame.Body.Count)),
            profile,
            layoutPath,
            mode,
            scale,
            steamId,
            context.ReportUnsupported);

        // Compared against what THIS thread last composed, never against the
        // applied shadow. See FrameSnapshotExtensions.SameContent -- diffing
        // against the shadow here silently loses frames.
        //
        // The forced flag is what makes a FAILED apply recoverable: without it
        // an identical next frame matches LastComposed, returns here, and the
        // failure is never retried.
        var forced = Interlocked.Exchange(ref state.ForceRepublish, 0) == 1;
        if (!forced && FrameSnapshotExtensions.SameContent(state.LastComposed, snapshot)) return;
        state.LastComposed = snapshot;

        state.Pending = snapshot;
        ScheduleApply(state, slot);
    }

    private void ScheduleApply(PlayerRenderState state, int slot)
    {
        // One queued apply per player per tick regardless of how often core
        // invalidates. Safe because the apply clears Queued BEFORE it reads
        // Pending: a render that sees 1 knows the queued apply has not read yet
        // and will pick up this newer snapshot.
        if (Interlocked.Exchange(ref state.Queued, 1) == 1) return;

        try
        {
            core.Scheduler.NextTick(() => ApplyOnGameThread(slot));
        }
        catch
        {
            // Nothing will clear Queued if the callback was never scheduled,
            // and this player would then never apply another frame.
            Interlocked.Exchange(ref state.Queued, 0);
            throw;
        }
    }

    private void ApplyOnGameThread(int slot)
    {
        if (!_states.TryGetValue(slot, out var state)) return;
        Interlocked.Exchange(ref state.Queued, 0);

        var work = state.Pending;
        if (work is null) return;

        // Render runs on a background thread, so a frame can still be in flight
        // when the session closes and its entity is dropped. Without this the apply
        // would recreate an entity for a menu nobody has open, and since Clear
        // has already run nothing would ever despawn it -- leaking the entity
        // and the per-viewer transmit blocks held against its recycled index.
        var session = _shuttingDown ? null : CurrentSession(slot);
        if (session is null)
        {
            Drop(slot);
            return;
        }
        // A session of ours is open again, so an entity Clear kept is this one's to update.
        state.Closing = false;

        long started = Stopwatch.GetTimestamp(), built = started, composed = started;
        try
        {
            FrameSnapshot pending;
            FrameSnapshot? blank = null;
            var rewound = false;
            if (work is DeferredCompose deferred)
            {
                // A newer session has its own render queued behind this one.
                if (!ReferenceEquals(session, deferred.Context.Session)) return;

                // A skipped root or tab host opens its entry in its place, so its own page is never built.
                var skips = FrameComposer.PlanSkips(deferred.Context.Menu, deferred.Profile, deferred.Mode);
                if (SkipRoot(slot, state, session, skips, TakeReturn(slot))) return;
                if (SkipTabHost(slot, state, session, skips)) return;

                PageIntegrationContext? hook = null;
                if (deferred.Integration is { } integration)
                {
                    // The last frame still belongs to the page this one was opened from.
                    integrations.Openers.Capture(session, state.Rows?.Session);
                    var regions = deferred.Profile.Page?.Regions.Where(r => r.Slots > 0).Select(r => r.Name)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
                    hook = new PageIntegrationContext(integrations, integration, strings.For(slot), regions, deferred.Profile.RowSlots,
                        deferred.Profile.Sidebar?.Icons ?? EmptyIcons,
                        deferred.Profile.Page?.ResolveView(deferred.Context.Menu.Id, null, config.Current.PageViews));
                }
                // The cursor's last place on this page tells which way a keyboard move was going.
                int? cameFrom = deferred.Mode == NavMode.Mouse ? null
                    : state.Rows is { } drawn && ReferenceEquals(drawn.Session, session) ? drawn.FocusedIndex : -1;
                var (frame, page) = PageFrame.Build(deferred.Context, core.Menu.Renderers, core.PlayerManager,
                    translations.For(slot), deferred.Budget, ReportRowRenderError, hook, cameFrom);
                if (page.Retarget >= 0 && state.MovedOff != (session, session.FocusedIndex))
                {
                    state.MovedOff = (session, session.FocusedIndex);
                    session.SetFocus(page.Retarget);
                    session.Invalidate();
                    return;
                }
                built = Stopwatch.GetTimestamp();
                pending = ComposeDeferred(deferred, frame, page, slot);

                if (deferred.Profile.ShowAllRows)
                {
                    var scrollKey = (deferred.Context.Menu, pending.Rows.FirstRow);
                    rewound = state.Entity.IsValid && !FrameSnapshotExtensions.RequiresRespawn(state.Shadow, pending)
                        && state.ScrollKey is { } onScreen && !onScreen.Equals(scrollKey);
                    state.ScrollKey = scrollKey;
                    if (rewound)
                    {
                        // Panorama keeps a scrolled panel's offset when its content changes, so a new
                        // list would open mid-scroll. The layout's rewind class shortens the scroll
                        // content for a few network frames with the new rows already up, which clamps
                        // the offset to the top; a layout without one gets an empty list for a tick.
                        state.RewindClass = deferred.Profile.Classes.Rewind;
                        if (state.RewindClass.Length > 0) state.RewindTicks = RewindHoldTicks;
                        else blank = ComposeDeferred(deferred, frame with { Body = [] }, page, slot);
                    }
                }
                else
                {
                    state.ScrollKey = null;
                }
                composed = Stopwatch.GetTimestamp();
            }
            else
            {
                pending = (FrameSnapshot)work;
                // Left in flight by a session that has closed since; the open one's own frame is on its way.
                if (pending.Rows.Session is { } owner && !ReferenceEquals(owner, session)) return;
                if (SkipRoot(slot, state, session, pending.Rows, TakeReturn(slot))) return;
                if (SkipTabHost(slot, state, session, pending.Rows)) return;
                state.ScrollKey = null;
            }

            var respawn = !state.Entity.IsValid || FrameSnapshotExtensions.RequiresRespawn(state.Shadow, pending)
                || FrameSnapshotExtensions.ExceedsClassBudget(state.ClassNames.Count, state.Shadow, pending);

            if (respawn)
            {
                if (state.Shadow.CaptureInput && state.Entity.Value is { } old)
                    entities.SetInputCapture(old, slot, false);
                entities.Despawn(state.Entity);
                state.Entity = default;

                // Panel names come off the snapshot, not a re-resolve:
                // re-resolving here cannot see which per-menu override the
                // compose used and would spawn against the default profile's
                // names, leaving the shell hidden forever.
                state.Entity = entities.Spawn(
                    pending.SteamId, slot, pending.LayoutPath,
                    pending.ShellPanel, pending.HiddenClass);

                // The fresh entity shows nothing, so the next diff has to be
                // against an empty shadow or most writes would be skipped.
                state.Shadow = FrameSnapshot.Empty;
                state.ClassNames.Clear();

                // And it starts scrolled to the top.
                state.RewindTicks = 0;
                blank = null;
                rewound = false;
            }

            if (state.Entity.Value is not { } entity)
            {
                RequestRetry(state, slot);
                return;
            }

            // While a rewind holds, every frame keeps its class on; an empty list goes up first instead.
            var shown = blank ?? (state.RewindTicks > 0 ? WithRewind(pending, state.RewindClass) : pending);
            var delta = Show(state, slot, entity, shown, out var diffed);
            state.MovedOff = null;
            state.Settle = blank is null ? null : pending;
            if ((state.Settle is not null || state.RewindTicks > 0) && !state.RewindQueued) QueueRewind(slot, state);

            if (config.Current.DetailedLogging)
                LogApply(slot, shown, delta, started, built, composed, diffed,
                    respawn ? " respawn" : blank is not null ? " rewind-blank" : rewound ? " rewind" : "");
        }
        catch (Exception ex)
        {
            // Shadow is only advanced after a clean apply, so a partial write
            // leaves it describing the last good frame; the retry makes the
            // render thread re-offer this one.
            RequestRetry(state, slot);
            logger.LogError(ex, "[CustomHUD] Apply failed for slot {Slot}.", slot);
        }
    }

    // Game thread. Brings the entity from its shadow to `next`, which is then what it shows.
    private FrameDelta Show(PlayerRenderState state, int slot, CCSCustomHudLayout entity, FrameSnapshot next, out long diffed)
    {
        var delta = state.Shadow.DiffTo(next);
        diffed = Stopwatch.GetTimestamp();
        if (!delta.IsEmpty) entities.Apply(entity, delta);
        foreach (var write in delta.Classes) state.ClassNames.Add(write.ClassName);

        // Reasserted on every apply while on, not just on change: it is one
        // native call, and a fresh entity or a client-side reset otherwise
        // leaves the cursor hidden with nothing left to restore it.
        if (next.CaptureInput || state.Shadow.CaptureInput)
            entities.SetInputCapture(entity, slot, next.CaptureInput);

        // Advanced even on an empty delta: a Mouse-mode focus move or a
        // capture-only change writes nothing, and returning early would
        // leave the click map below describing the previous frame.
        state.Shadow = next;

        // Published here, not at compose time, so the map the input bridge
        // resolves clicks against always describes what is actually on the
        // player's screen. A map published early would interpret a click
        // against rows the player cannot see yet. A closed session's map is not republished.
        if (!state.Closing) state.Rows = next.Rows;
        return delta;
    }

    // Three network frames: one is enough for the clamp, the others cover a dropped packet. The rows
    // are drawn throughout, so the hold costs nothing visible but the scrollbar's few ticks of absence.
    private const int RewindHoldTicks = 3;

    private static FrameSnapshot WithRewind(FrameSnapshot frame, string rewindClass)
        => frame with { Classes = frame.Classes.Add(new ClassWrite(frame.ShellPanel, rewindClass, true)) };

    private void QueueRewind(int slot, PlayerRenderState state)
    {
        try
        {
            core.Scheduler.NextTick(() => RewindTick(slot, state));
            state.RewindQueued = true;
        }
        catch (Exception ex)
        {
            // A redraw with no hold left settles the rewind in its place.
            state.RewindTicks = 0;
            state.Settle = null;
            logger.LogError(ex, "[CustomHUD] Could not schedule the end of a scroll rewind for slot {Slot}.", slot);
            RequestRetry(state, slot);
        }
    }

    // Game thread. A rewind's end: the frame an empty list stood in for goes up, or once the
    // hold runs out the rewind class comes off, which writes that one class and nothing else.
    private void RewindTick(int slot, PlayerRenderState state)
    {
        state.RewindQueued = false;
        if (!_states.TryGetValue(slot, out var current) || !ReferenceEquals(current, state)) return;
        if (state.Settle is null && --state.RewindTicks > 0)
        {
            QueueRewind(slot, state);
            return;
        }

        var next = state.Settle;
        state.Settle = null;
        state.RewindTicks = 0;
        if (next is null)
        {
            var on = state.Shadow.Classes.IndexOf(new ClassWrite(state.Shadow.ShellPanel, state.RewindClass, true));
            if (on < 0) return;
            next = state.Shadow with { Classes = state.Shadow.Classes.RemoveAt(on) };
        }
        if (state.Entity.Value is not { } entity) return;

        try
        {
            var started = Stopwatch.GetTimestamp();
            var delta = Show(state, slot, entity, next, out var diffed);
            if (config.Current.DetailedLogging)
                LogApply(slot, next, delta, started, started, started, diffed, " rewound");
        }
        catch (Exception ex)
        {
            RequestRetry(state, slot);
            logger.LogError(ex, "[CustomHUD] Ending a scroll rewind failed for slot {Slot}.", slot);
        }
    }

    // One line per apply: what was written, and the game-thread time each step took (build includes
    // the integration's Draw; write is the native calls, so write / (vars + classes) prices one).
    private void LogApply(int slot, FrameSnapshot frame, FrameDelta delta, long started, long built, long composed,
        long diffed, string what)
    {
        var done = Stopwatch.GetTimestamp();
        logger.LogInformation(
            "[CustomHUD] applied slot={Slot} menu={Menu} vars={Vars} classes={Classes} layout={Layout} " +
            "ms build={Build:0.00} compose={Compose:0.00} diff={Diff:0.00} write={Write:0.00} total={Total:0.00}{What}",
            slot, frame.Rows.Session?.Menu.Id, delta.Vars.Length, delta.Classes.Length, frame.LayoutPath,
            Ms(started, built), Ms(built, composed), Ms(composed, diffed), Ms(diffed, done), Ms(started, done), what);
    }

    private static double Ms(long from, long to) => (to - from) * 1000.0 / Stopwatch.Frequency;

    // Setting the flag alone is not enough: a static menu may never be
    // invalidated again, so nothing would ever call Render to consume it and
    // the failed frame would stay undrawn forever. Invalidating is what
    // guarantees the retry actually happens.
    private void RequestRetry(PlayerRenderState state, int slot)
    {
        Interlocked.Exchange(ref state.ForceRepublish, 1);
        InvalidateSlot(slot);
    }

    // Game thread. A sidebar root with AutoOpen never shows its own page: a
    // fresh one opens its entry in its place, and one a player has just backed
    // into from that level is closed, since showing it would only reopen the
    // page they left. Falls through to drawing the root when the entry cannot open.
    private bool SkipRoot(int slot, PlayerRenderState state, IMenuSession session, RowMap rows, IMenu? returnedTo)
    {
        if (!rows.SkipsRoot || !ReferenceEquals(session.Menu, rows.RootMenu)) return false;
        if (ReferenceEquals(returnedTo, session.Menu))
        {
            session.Close();
            return true;
        }
        if (ReferenceEquals(state.RootHandled, session) || rows.AutoOpenItem is not { } item) return false;
        state.RootHandled = session;
        if (!item.IsVisible(session) || !item.IsEnabled(session)) return false;

        Memory(slot).Entry = (session.Menu, rows.SidebarItems.IndexOf(item));
        MenuActivation.Select(item, session, MenuKey.Mouse1, translations.For(slot), logger);
        return !ReferenceEquals(core.Menu.GetSession(session.Player), session);
    }

    // Game thread. A tab host with TabAutoOpen never shows its own page either: its
    // tab opens in its place. Backing into it opens that tab again rather than closing,
    // since Back is hidden on its tabs and only a plugin's own Close lands here.
    private bool SkipTabHost(int slot, PlayerRenderState state, IMenuSession session, RowMap rows)
    {
        if (rows.TabAutoOpenItem is not { } item || !ReferenceEquals(session.Menu, rows.TabHost)) return false;
        if (ReferenceEquals(state.TabHandled, session)) return false;
        state.TabHandled = session;
        if (!item.IsVisible(session) || !item.IsEnabled(session)) return false;

        Memory(slot).Tab = (session.Menu, rows.TabAutoOpenIndex);
        MenuActivation.Select(item, session, MenuKey.Mouse1, translations.For(slot), logger);
        return !ReferenceEquals(core.Menu.GetSession(session.Player), session);
    }

    // Game thread. The input bridge opens a skipped root itself to reach a
    // sidebar entry; auto-opening the first entry there would cancel that.
    // Opening the root closed the old session and dropped its state, so this
    // starts the new one rather than writing to nothing.
    // A negative entry (a root action rather than a page) keeps the page remembered so far.
    public void KeepRootOpen(int slot, IMenuSession rootSession, int entry)
    {
        var state = _states.GetOrAdd(slot, _ => new PlayerRenderState { OwnerSlot = slot });
        state.RootHandled = rootSession;
        if (entry >= 0) Memory(slot).Entry = (rootSession.Menu, entry);
    }

    // Game thread. The same for a tab, opened from its host.
    public void KeepTabOpen(int slot, IMenuSession hostSession, int tab)
    {
        var state = _states.GetOrAdd(slot, _ => new PlayerRenderState { OwnerSlot = slot });
        state.TabHandled = hostSession;
        Memory(slot).Tab = (hostSession.Menu, tab);
    }

    // Game thread. A page the input bridge opened through another it never let draw (a card's slot tile).
    public void RecordOpener(IMenuSession opened, IMenuComponent row) => integrations.Openers.Record(opened, row);

    private NavMemory Memory(int slot) => _memory.GetOrAdd(slot, _ => new NavMemory());

    // Game thread. Closing the whole stack passes through the root on its way out.
    public void ForgetReturn(int slot) => _returning.TryRemove(slot, out _);

    // Game thread. Taken only once a frame is known to be the open session's, so a frame a closed
    // child left in flight cannot spend it and let the root reopen the page Back just left.
    private IMenu? TakeReturn(int slot)
        => _returning.TryRemove(slot, out var back) && Environment.TickCount64 - back.At < 1000 ? back.Root : null;

    // Runs on the game thread when a session closes. Core's Back calls this and then opens the
    // parent in the same call, so the entity is kept until the next tick: a session of ours by then
    // updates it as a diff instead of spawning a whole new layout, and FinishClose drops it otherwise.
    public void Clear(IPlayer player)
    {
        var slot = player.PlayerID;
        if (_shuttingDown || !_states.TryGetValue(slot, out var state) || !state.Entity.IsValid)
        {
            Drop(slot);
            return;
        }
        RecordReturn(slot, state);
        // What a fresh state would hold; the entity, its shadow and ScrollKey stay for the diff.
        state.Pending = null;
        state.Rows = null;
        state.RootHandled = null;
        state.TabHandled = null;
        state.Settle = null;
        if (state.Closing) return;
        state.Closing = true;
        try
        {
            core.Scheduler.NextTick(() => FinishClose(slot, state));
        }
        catch (Exception ex)
        {
            // Nothing else would ever let a kept entity go.
            logger.LogError(ex, "[CustomHUD] Could not schedule the close for slot {Slot}; dropping now.", slot);
            Drop(slot);
        }
    }

    // Game thread. Input capture stays on while the entity is kept: releasing it for a tick and
    // taking it back moves the player's cursor. Drop releases it when nothing took the entity over.
    private void FinishClose(int slot, PlayerRenderState state)
    {
        if (!state.Closing || !_states.TryGetValue(slot, out var current) || !ReferenceEquals(current, state)) return;
        state.Closing = false;
        if (!_shuttingDown && CurrentSession(slot) is not null) return;
        Drop(slot);
    }

    // Core does not call Clear when a player disconnects mid-menu, so without
    // this the entity survives with nothing left to despawn it -- and because
    // DenyForJoiningClient skips the entity's own slot, the next player to take
    // that slot would start receiving it.
    public void OnPlayerDisconnected(int slot)
    {
        Drop(slot);
        _returning.TryRemove(slot, out _);
        _memory.TryRemove(slot, out _);
        translations.Forget(slot);
        strings.Forget(slot);
    }

    // Game thread.
    private void Drop(int slot)
    {
        if (!_states.TryRemove(slot, out var state)) return;
        RecordReturn(slot, state);
        // Released before the despawn, or the client keeps a cursor with no
        // panel left to aim it at.
        if (state.Shadow.CaptureInput && state.Entity.Value is { } entity)
            entities.SetInputCapture(entity, state.OwnerSlot, false);
        entities.Despawn(state.Entity);
        state.Entity = default;
        state.Shadow = FrameSnapshot.Empty;
        state.ScrollKey = null;
        state.Pending = null;
        state.Rows = null;
        // LastComposed is deliberately left alone -- it belongs to the render
        // thread, and the state object is already unreachable from the map, so
        // a later render creates a fresh one rather than reusing this.
    }

    // Core clears the renderer before reopening the parent, so a page of a
    // skipped root is recognised here, while its frame is still at hand.
    private void RecordReturn(int slot, PlayerRenderState state)
    {
        if (state.Rows is { SkipsRoot: true, RootMenu: { } root, Session: { } closing }
            && ReferenceEquals(closing.Menu.Parent, root))
            _returning[slot] = (root, Environment.TickCount64);
        // A session that closes before drawing a frame is the root Back opened, so its return is spent;
        // left in place, it would close the same root opened afresh within the second.
        else if (state.Rows is null)
            _returning.TryRemove(slot, out _);
    }

    // Game thread. A session belonging to another renderer means a menu we are
    // not drawing, so an entity of ours has no business existing for it.
    private IMenuSession? CurrentSession(int slot)
    {
        var player = core.PlayerManager.GetPlayer(slot);
        if (player is null) return null;

        var session = core.Menu.GetSession(player);
        return session is { IsOpen: true }
            && string.Equals(session.Menu.Renderer.Id, config.Current.RendererId, StringComparison.OrdinalIgnoreCase)
            ? session
            : null;
    }

    // Game thread.
    private FrameSnapshot ComposeDeferred(DeferredCompose work, MenuFrame frame, MenuPage page, int slot)
        => _composer.Compose(
            new FrameContext(work.Context, frame, page) { RememberedEntry = Memory(slot).Entry, RememberedTab = Memory(slot).Tab },
            work.Profile,
            work.LayoutPath,
            work.Mode,
            work.ScalePercent,
            work.SteamId,
            work.Context.ReportUnsupported);

    private void ReportRowRenderError(IMenuComponent component, Exception ex)
    {
        if (_renderErrors.TryAdd(component.GetType(), 0))
            logger.LogWarning(ex, "[CustomHUD] {Component} threw while rendering a row past core's window; drawn blank.",
                component.GetType().Name);
    }

    // Read by the input bridge to turn a clicked row into a focus movement.
    public RowMap? GetRows(int slot)
        => _states.TryGetValue(slot, out var state) ? state.Rows : null;

    // A transmit deny only covers clients connected when it was issued, and a
    // reconnect rebuilds the player object that holds it. So every live entity
    // has to be re-denied against each joining client or a spectator who
    // connects mid-menu starts receiving somebody else's menu.
    public void DenyForJoiningClient(int viewerSlot)
    {
        foreach (var (ownerSlot, state) in _states)
        {
            if (ownerSlot == viewerSlot) continue;
            entities.DenyFor(state.Entity, viewerSlot);
        }
    }

    // Nav mode and scale are read during compose, so a preference change only
    // needs the session invalidated -- the next frame picks up the new layout
    // and the apply path respawns the entity.
    public void OnPreferenceChanged(ulong steamId)
    {
        if (steamIds.FindSlot(steamId) is { } slot)
        {
            InvalidateSlot(slot);
            return;
        }
        var player = core.PlayerManager.GetPlayerFromSteamId(steamId, allowUnauthorized: true);
        if (player is null) return;
        core.Menu.GetSession(player)?.Invalidate();
    }

    // Game thread. Same mechanism by slot, for callers that have no SteamID
    // yet -- which is the whole point when authorization is what just changed.
    public void InvalidateSlot(int slot)
    {
        var player = core.PlayerManager.GetPlayer(slot);
        if (player is null) return;
        core.Menu.GetSession(player)?.Invalidate();
    }

    // Game thread, during unload.
    public void Shutdown()
    {
        _shuttingDown = true;
        foreach (var slot in _states.Keys)
            Drop(slot);
        entities.DrainRetries();
    }
}
