using System.Collections.Immutable;
using SwiftlyS2.Shared.Menu;

namespace CustomHUD.Rendering;

// What one composed frame wants the layout to look like. Produced off the game
// thread by FrameComposer and never touches engine state, which is what makes
// it safe to build inside IMenuRenderer.Render.
internal sealed record FrameSnapshot(
    ImmutableArray<VarWrite> Vars,
    ImmutableArray<ClassWrite> Classes,
    // Layout this snapshot was composed against. A change means the player
    // switched nav mode, so the entity has to be respawned rather than updated.
    string LayoutPath,
    // Everything about the profile that changes the SET of writes rather than
    // their values -- row count, ladder sizes, panel naming. A live config edit
    // can shrink RowSlots, and the diff only ever visits writes the NEW frame
    // produces, so rows that disappeared would keep whatever they last had.
    // Treating a fingerprint change as a respawn clears them.
    string ProfileFingerprint,
    // Carried so the game-thread apply can spawn against the SAME profile the
    // compose resolved. Re-resolving there would miss a per-menu override and
    // spawn with the default profile's panel names, leaving the shell hidden.
    string ShellPanel,
    string HiddenClass,
    // Slots are recycled. The apply compares this against what it last drew so
    // a new occupant always starts from a blank shadow; doing it on the render
    // thread would mean writing game-thread state from the wrong thread.
    ulong SteamId,
    // Physical row -> is it focusable, and which row holds the selection.
    // The input bridge needs this to turn a clicked row into a focus movement:
    // body rows include non-focusable entries, so row index is not focus index.
    RowMap Rows,
    // Panorama only raises OnCustomHudClicked while capture is on, and capture
    // turns the player's mouse into a cursor -- so it is on for Mouse mode only.
    // Not part of RequiresRespawn: a Mouse player with no Mouse profile keeps
    // the same layout path, so the flag has to be diffed on update.
    bool CaptureInput)
{
    public static readonly FrameSnapshot Empty =
        new(ImmutableArray<VarWrite>.Empty, ImmutableArray<ClassWrite>.Empty,
            string.Empty, string.Empty, "shell", "hidden", 0, RowMap.Empty, false);
}

internal sealed record RowMap(
    ImmutableArray<bool> Focusable,
    int FocusedRow,
    // The session's FocusedIndex at compose time. A click resolves against a
    // map that may be a tick or two old, so this lets the input bridge measure
    // how far the cursor has moved since -- by keyboard, or by a component --
    // and correct its step count instead of jumping to the wrong row.
    int FocusedIndex,
    // Panel-id prefix of the profile this frame was composed with. The input
    // bridge must not re-resolve the profile itself: it would have to redo the
    // nav-mode lookup and could pick a different one than the renderer did,
    // leaving every click rejected against the wrong prefix.
    string RowPrefix)
{
    public static readonly RowMap Empty = new(ImmutableArray<bool>.Empty, -1, -1, string.Empty);
    public IMenuSession? Session { get; init; }
    // Body index of the first drawn row across the whole menu.
    public int FirstRow { get; init; }
    // SetFocus targets for the previous / next page; -1 when the renderer drew core's own window.
    public int PreviousPageFocus { get; init; } = -1;
    public int NextPageFocus { get; init; } = -1;
    // Physical rows drawn as PageTiles cards; -1 when absent.
    public int PreviousTileRow { get; init; } = -1;
    public int NextTileRow { get; init; } = -1;
    public string BackButton { get; init; } = "";
    public string CloseButton { get; init; } = "";
    public string PreviousButton { get; init; } = "";
    public string NextButton { get; init; } = "";
    public string RootButton { get; init; } = "";
    public IMenu? RootMenu { get; init; }
    public string SidebarPrefix { get; init; } = "";
    // Root-menu entries per sidebar slot; a DividerComponent where the slot is a rule.
    public ImmutableArray<IMenuComponent> SidebarItems { get; init; } = [];
    // The sidebar's AutoOpen is set, so its root page is never shown.
    public bool SkipsRoot { get; init; }
    // Set only on a frame of that root itself: the entry to open in its place.
    public IMenuComponent? AutoOpenItem { get; init; }
    // Set while the frame is drawn as a card; RowPrefix is then the field prefix.
    public CardMap? Card { get; init; }
    // Page layouts: clicked panel id -> physical row, or one of the page codes
    // below. Checked before RowPrefix, so a row's hit Button, an inline entry's
    // Button and every region slot resolve without parsing their ids.
    public ImmutableDictionary<string, int> Targets { get; init; } = ImmutableDictionary<string, int>.Empty;
    public const int PreviousPageTarget = -2;
    public const int NextPageTarget = -3;
    // Tabs: the page whose static submenus they mirror, and one per tab slot.
    public IMenu? TabHost { get; init; }
    public string TabPrefix { get; init; } = "";
    public ImmutableArray<IMenuComponent> TabItems { get; init; } = [];
    // Set only on a frame of the tab host itself: the tab to open in its place.
    public IMenuComponent? TabAutoOpenItem { get; init; }
    public int TabAutoOpenIndex { get; init; } = -1;
    // The tab host is skipped, so Back from one of its tabs has nowhere to go.
    public bool SkipsTabHost { get; init; }
    // Physical rows a number key picks, in key order. Empty means every focusable row:
    // a page layout leaves disabled rows out, since core focuses them but cannot act on them.
    public ImmutableArray<bool> Keyed { get; init; } = [];
    // Page layouts: a stepper or track-segment Button -> the row it acts on.
    public ImmutableDictionary<string, RowControl> Controls { get; init; } = ImmutableDictionary<string, RowControl>.Empty;
    // Row layouts: the spot's link Button, and the root entry it opens and what it picks there.
    public string LinkButton { get; init; } = "";
    public string LinkEntry { get; init; } = "";
    public string LinkPick { get; init; } = "";

    public bool IsKeyed(int row) => Keyed.IsEmpty ? Focusable[row] : Keyed[row];
}

// Step -1 / +1 sends NavigateLeft / NavigateRight; Step 0 sets a slider to Segment of Segments.
// Action set: an `act=` button, sending that menu action to the row instead.
internal readonly record struct RowControl(int Row, int Step, int Segment, int Segments, string? Action = null);

// Card field -> physical row of the entry each click target acts on, -1 for none.
internal sealed record CardMap(
    ImmutableArray<int> Main,
    ImmutableArray<int> Aside,
    ImmutableArray<int> Track,
    string HitSuffix,
    string AsideSuffix,
    string SegmentSuffix,
    int Segments)
{
    // A field's slot tiles: per field, what each tile picks on the page its row opens. Empty when
    // the layout has none.
    public string SlotSuffix { get; init; } = "";
    public ImmutableArray<ImmutableArray<string>> Picks { get; init; } = [];

    public bool SameAs(CardMap? other)
        => other is not null && Segments == other.Segments
        && HitSuffix == other.HitSuffix && AsideSuffix == other.AsideSuffix && SegmentSuffix == other.SegmentSuffix
        && SlotSuffix == other.SlotSuffix
        && Main.SequenceEqual(other.Main) && Aside.SequenceEqual(other.Aside) && Track.SequenceEqual(other.Track)
        && Picks.Length == other.Picks.Length
        && Picks.Zip(other.Picks).All(pair => pair.First.SequenceEqual(pair.Second));
}

internal readonly record struct VarWrite(string PanelId, string Variable, string Value);

internal readonly record struct ClassWrite(string PanelId, string ClassName, bool On);

// Only the writes that would actually change what the player sees.
internal sealed record FrameDelta(
    ImmutableArray<VarWrite> Vars,
    ImmutableArray<ClassWrite> Classes)
{
    public static readonly FrameDelta Empty =
        new(ImmutableArray<VarWrite>.Empty, ImmutableArray<ClassWrite>.Empty);

    public bool IsEmpty => Vars.IsEmpty && Classes.IsEmpty;
}

internal static class FrameSnapshotExtensions
{
    // The whole point of the renderer's cost model: an invalidation that
    // changes nothing costs one node walk and zero engine calls. Components we
    // do not own invalidate freely (TextProvider runs every frame,
    // ProgressBarComponent self-invalidates), so this is the only thing
    // standing between a third-party menu and a per-tick write storm.
    //
    // GAME THREAD ONLY -- `previous` is the applied shadow.
    public static FrameDelta DiffTo(this FrameSnapshot previous, FrameSnapshot next)
    {
        // A respawn shares no addressing vocabulary with what came before, and
        // the fresh entity shows nothing, so every write is new by definition.
        if (RequiresRespawn(previous, next))
            return new FrameDelta(next.Vars, next.Classes);

        var vars = ImmutableArray.CreateBuilder<VarWrite>();
        var seenVars = new Dictionary<(string, string), string>(previous.Vars.Length);
        foreach (var w in previous.Vars) seenVars[(w.PanelId, w.Variable)] = w.Value;
        foreach (var write in next.Vars)
        {
            if (seenVars.TryGetValue((write.PanelId, write.Variable), out var old)
                && string.Equals(old, write.Value, StringComparison.Ordinal))
                continue;
            vars.Add(write);
        }

        var classes = ImmutableArray.CreateBuilder<ClassWrite>();
        var seenClasses = new Dictionary<(string, string), bool>(previous.Classes.Length);
        foreach (var w in previous.Classes) seenClasses[(w.PanelId, w.ClassName)] = w.On;
        var nextClasses = new HashSet<(string, string)>(next.Classes.Length);
        foreach (var write in next.Classes)
        {
            nextClasses.Add((write.PanelId, write.ClassName));
            if (seenClasses.TryGetValue((write.PanelId, write.ClassName), out var old)
                && old == write.On)
                continue;
            classes.Add(write);
        }

        // Every class except Hidden is written only while on, so one the next
        // frame no longer names is cleared here.
        foreach (var (key, on) in seenClasses)
            if (on && !nextClasses.Contains(key))
                classes.Add(new ClassWrite(key.Item1, key.Item2, false));

        return new FrameDelta(vars.ToImmutable(), classes.ToImmutable());
    }

    // StrLayout is fixed once an entity has spawned, and a profile or occupant
    // change invalidates the addressing the shadow was built from, so all three
    // are replacements rather than updates.
    public static bool RequiresRespawn(FrameSnapshot previous, FrameSnapshot next)
        => !string.Equals(previous.LayoutPath, next.LayoutPath, StringComparison.Ordinal)
        || !string.Equals(previous.ProfileFingerprint, next.ProfileFingerprint, StringComparison.Ordinal)
        || previous.SteamId != next.SteamId;

    // Each class name an entity is given joins its networked m_vecClassNames, which no API
    // can shrink, and every econ thumbnail is a name of its own. Past the budget, the next
    // change of list respawns the entity: the list redraws from the top at that point anyway.
    public const int ClassNameBudget = 2048;

    public static bool ExceedsClassBudget(int namesWritten, FrameSnapshot shown, FrameSnapshot next)
        => namesWritten >= ClassNameBudget
        && (!ReferenceEquals(shown.Rows.Session, next.Rows.Session) || shown.Rows.FirstRow != next.Rows.FirstRow);

    // Render-thread early-out. Compared against the last snapshot THIS THREAD
    // composed -- never against the applied shadow, which the game thread owns
    // and which lags. Diffing against the shadow here loses frames: a menu that
    // changes to B and back to A while the apply for B is still queued produces
    // an empty diff for A, returns before publishing it, and leaves B on screen
    // permanently.
    public static bool SameContent(FrameSnapshot? previous, FrameSnapshot next)
    {
        if (previous is null) return false;
        if (RequiresRespawn(previous, next)) return false;
        if (previous.Vars.Length != next.Vars.Length) return false;
        if (previous.Classes.Length != next.Classes.Length) return false;

        // Compose is deterministic, so matching frames are produced in the same
        // order and an ordinal walk is enough.
        for (var i = 0; i < next.Vars.Length; i++)
            if (!previous.Vars[i].Equals(next.Vars[i])) return false;
        for (var i = 0; i < next.Classes.Length; i++)
            if (!previous.Classes[i].Equals(next.Classes[i])) return false;

        // Focusability is not visible in the writes -- a label and a button can
        // render identical text -- but the input bridge resolves clicks against
        // it, so a change here has to republish even when nothing is redrawn.
        if (previous.CaptureInput != next.CaptureInput) return false;
        if (!ReferenceEquals(previous.Rows.Session, next.Rows.Session)) return false;
        if (!ReferenceEquals(previous.Rows.RootMenu, next.Rows.RootMenu)) return false;
        if (!previous.Rows.SidebarItems.SequenceEqual(next.Rows.SidebarItems)) return false;
        if (!ReferenceEquals(previous.Rows.AutoOpenItem, next.Rows.AutoOpenItem)) return false;
        if (!ReferenceEquals(previous.Rows.TabHost, next.Rows.TabHost)) return false;
        if (!previous.Rows.TabItems.SequenceEqual(next.Rows.TabItems)) return false;
        if (!ReferenceEquals(previous.Rows.TabAutoOpenItem, next.Rows.TabAutoOpenItem)) return false;
        if (!SameTargets(previous.Rows.Targets, next.Rows.Targets)) return false;
        if (!SameControls(previous.Rows.Controls, next.Rows.Controls)) return false;
        if (!previous.Rows.Keyed.SequenceEqual(next.Rows.Keyed)) return false;
        if (previous.Rows.Card is null ? next.Rows.Card is not null : !previous.Rows.Card.SameAs(next.Rows.Card)) return false;
        if (previous.Rows.LinkButton != next.Rows.LinkButton || previous.Rows.LinkEntry != next.Rows.LinkEntry
            || previous.Rows.LinkPick != next.Rows.LinkPick) return false;
        if (previous.Rows.FocusedRow != next.Rows.FocusedRow) return false;
        if (previous.Rows.FocusedIndex != next.Rows.FocusedIndex) return false;
        if (previous.Rows.Focusable.Length != next.Rows.Focusable.Length) return false;
        for (var i = 0; i < next.Rows.Focusable.Length; i++)
            if (previous.Rows.Focusable[i] != next.Rows.Focusable[i]) return false;

        return true;
    }

    private static bool SameTargets(ImmutableDictionary<string, int> a, ImmutableDictionary<string, int> b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Count != b.Count) return false;
        foreach (var (key, value) in a)
            if (!b.TryGetValue(key, out var other) || other != value) return false;
        return true;
    }

    private static bool SameControls(ImmutableDictionary<string, RowControl> a, ImmutableDictionary<string, RowControl> b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Count != b.Count) return false;
        foreach (var (key, value) in a)
            if (!b.TryGetValue(key, out var other) || other != value) return false;
        return true;
    }
}
