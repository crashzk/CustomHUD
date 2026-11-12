using System.Runtime.CompilerServices;
using SwiftlyS2.Shared.Menu;

namespace CustomHUD.Integrations;

// What each page was opened from: the item, or failing that the Tag, of the row the player chose
// on the page before. Captured on a page's first draw from the previous page's session, whose
// focus is still on that row: a click focuses a row before it selects it. Game thread only.
internal sealed class Openers
{
    private readonly ConditionalWeakTable<IMenu, Dictionary<int, object?>> _byMenu = new();

    public void Capture(IMenuSession session, IMenuSession? previous)
    {
        var menu = session.Menu;
        var slot = session.Player.PlayerID;
        var seen = _byMenu.GetOrCreateValue(menu);
        if (seen.ContainsKey(slot)) return;

        object? value = null;
        if (previous is not null && !ReferenceEquals(previous, session) && ReferenceEquals(previous.Menu, menu.Parent)
            && previous.FocusedComponent is { } row)
            value = OpenerOf(row);
        seen[slot] = value;
    }

    // Before the page's first draw, which Capture then leaves alone.
    public void Record(IMenuSession session, IMenuComponent row)
        => _byMenu.GetOrCreateValue(session.Menu)[session.Player.PlayerID] = OpenerOf(row);

    private static object? OpenerOf(IMenuComponent row) => CollectionItems.Of(row) ?? (row as MenuComponentBase)?.Tag;

    // This page's opener, else the nearest ancestor's: a page under a weapon's page is about that weapon too.
    public object? Of(IMenu menu, int slot) => Find(menu, slot, static _ => true);

    // The nearest opener of one type: a sticker slot's page is opened from a slot number, but the
    // weapon it belongs to was chosen two pages up.
    public bool TryOf<T>(IMenu menu, int slot, out T value)
    {
        if (Find(menu, slot, static v => v is T) is T found)
        {
            value = found;
            return true;
        }
        value = default!;
        return false;
    }

    private object? Find(IMenu menu, int slot, Func<object, bool> match)
    {
        var current = menu;
        for (var depth = 0; current is not null && depth < 16; depth++, current = current.Parent)
            if (_byMenu.TryGetValue(current, out var seen) && seen.TryGetValue(slot, out var value) && value is not null && match(value))
                return value;
        return null;
    }
}
