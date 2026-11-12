using SwiftlyS2.Shared.Menu;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace CustomHUD.Rendering;

// One player's live render state.
//
// THREAD OWNERSHIP IS THE WHOLE POINT OF THIS CLASS:
//   Entity, Shadow, ClassNames,
//   ScrollKey, RootHandled, TabHandled,
//   Closing, Rewind*, Settle, MovedOff -- game thread only.
//   LastComposed        -- render thread only.
//   Pending, Rows       -- written by the render thread, read by the game
//                          thread. Reference assignments of immutable values,
//                          published through the Interlocked gate below.
//   Queued              -- Interlocked from both.
//   OwnerSlot           -- effectively immutable once set.
//
// Nothing outside those rules may be added without re-reasoning the coalescer.
internal sealed class PlayerRenderState
{
    public CHandle<CCSCustomHudLayout> Entity;

    // What the entity is currently showing.
    public FrameSnapshot Shadow = FrameSnapshot.Empty;

    // Distinct class names written to Entity since it spawned; see ClassNameBudget.
    public readonly HashSet<string> ClassNames = new(StringComparer.Ordinal);

    // Menu and first drawn row of the list on screen; a change rewinds the
    // layout's scroll position.
    public (IMenu Menu, int FirstRow)? ScrollKey;

    // Session of a skipped sidebar root that has already been opened past, or
    // that the input bridge is navigating from, so it is never auto-opened twice.
    public IMenuSession? RootHandled;

    // The same for a skipped tab host (MenuPageProfile.TabAutoOpen).
    public IMenuSession? TabHandled;

    // Set by Clear until the next tick: the session closed, but the entity stays up in case a session
    // of ours opens in its place (Back reopens the parent right after Clear), which then draws as a diff.
    public bool Closing;

    // A scroll rewind under way: ticks LayoutClasses.Rewind has left on the shell, or, for a layout
    // that styles no rewind class, the frame an empty list stands in for until the next tick.
    public int RewindTicks;
    public string RewindClass = "";
    public FrameSnapshot? Settle;
    public bool RewindQueued;

    // The label the cursor was last moved off (PageFrame.LabelSkip), cleared once a frame draws: should core
    // keep it there, the page draws rather than asking again every tick.
    public (IMenuSession Session, int Index)? MovedOff;

    // Last frame the render thread composed. Used only for its own early-out,
    // never for deciding what to draw.
    public FrameSnapshot? LastComposed;

    // Latest work awaiting apply, latest wins: a FrameSnapshot, or a
    // DeferredCompose the game thread has to compose itself.
    public volatile object? Pending;

    // Row focusability of the frame most recently composed, for the input
    // bridge. Kept beside Pending rather than inside it so a click can be
    // resolved even when no apply is outstanding.
    public volatile RowMap? Rows;

    // 1 while an apply is already queued for this player. Gates task creation
    // so a fast invalidation loop cannot stack several applies on one tick.
    public int Queued;

    // Set by the game thread when an apply or spawn failed, so the next render
    // republishes even though its content matches LastComposed. Without it a
    // failed frame is suppressed forever: the render thread would see no
    // change and never retry, leaving the entity showing the previous frame.
    // Interlocked rather than written through LastComposed, which the render
    // thread owns.
    public int ForceRepublish;

    public int OwnerSlot = -1;
}

// The sidebar slot and tab a player last opened through CustomHUD: the
// active-entry fallback for plugins whose entries carry neither the page's id
// nor its title. Outlives PlayerRenderState, because a plugin that reopens a
// page in place (ShopCore after a purchase) drops that with the old session.
// Game thread only.
internal sealed class NavMemory
{
    public (IMenu Root, int Index)? Entry;
    public (IMenu Host, int Index)? Tab;
}
