using System.Globalization;
using CustomHUD.Config;
using CustomHUD.Navigation;
using CustomHUD.Rendering;
using CustomHUD.Services;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Menu;
using SwiftlyS2.Shared.Menu.Components;
using SwiftlyS2.Shared.Players;

namespace CustomHUD.Input;

// Feeds the input methods core has no concept of into a core menu session:
// mouse clicks, numbers, and the wasd keys.
//
// Keyboard input is handled only for players in wasd mode, and only for keys
// the menu's keymap does not already own -- a key bound in menus.jsonc is
// dispatched by core, and dispatching it here as well would act twice. Core's
// traversal binds on any other key are masked for them, so only W S A D E R Tab
// navigate; mouse and numbers players lose core's binds outright (Masked).
//
// MenuActionContext is a public readonly record struct with required init
// properties, and IMenuComponent.HandleActionAsync is on the public interface,
// so synthesizing an activation is a supported seam rather than a trick. Its
// Translate is core's translator, as core's router passes: components look up
// core's keys through it ("menu.input.chat_hint"), which CustomHUD's files lack.
internal sealed class MenuInputBridge(
    ISwiftlyCore core,
    CustomHudConfigProvider config,
    PanoramaMenuRenderer renderer,
    NavModeResolver navModes,
    SteamIdCache steamIds,
    CoreTranslations translations,
    ILogger logger)
{
    private EventDelegates.OnCustomHudClicked? _onClicked;
    private EventDelegates.OnClientKeyStateChanged? _onKey;
    private EventDelegates.OnTick? _onTick;

    private const int MaxSlots = 128;
    // Keys this tick's OnTick removed, per slot: a press of one reaching OnKey means the mask did not hold.
    private readonly MenuKey[] _masked = new MenuKey[MaxSlots];

    public void Subscribe()
    {
        _onClicked = OnClicked;
        core.Event.OnCustomHudClicked += _onClicked;
        _onKey = OnKey;
        core.Event.OnClientKeyStateChanged += _onKey;
        _onTick = OnTick;
        core.Event.OnTick += _onTick;
    }

    public void Unsubscribe()
    {
        if (_onClicked is not null) core.Event.OnCustomHudClicked -= _onClicked;
        if (_onKey is not null) core.Event.OnClientKeyStateChanged -= _onKey;
        if (_onTick is not null) core.Event.OnTick -= _onTick;
        _onClicked = null;
        _onKey = null;
        _onTick = null;
    }

    private void OnTick()
    {
        Array.Clear(_masked);
        foreach (var player in core.PlayerManager.GetAllPlayers())
        {
            var keys = Masked(player);
            if (keys == MenuKey.None) continue;

            // SW2's OnGameFramePlayerHook runs OnTick after simulation, BEFORE
            // CPlayer::Think samples ButtonPressed for menu key events.
            // Pawn matches that sampler, including the spectator pawn.
            if (player.Pawn is { IsValid: true, MovementServices: { } movement })
            {
                movement.Buttons.ButtonPressed &= ~ButtonsOf(keys);
                if ((uint)player.PlayerID < MaxSlots) _masked[player.PlayerID] = keys;
            }
        }
    }

    // The keys core must not see while this player's own menu takes keyboard input. Mouse and numbers draw
    // no cursor, yet core's keys act on its focus, the row last clicked or picked: E would activate it again.
    private MenuKey Masked(IPlayer player)
    {
        if (player is not { IsValid: true, IsFakeClient: false }
            || GetOwnSession(player) is not { Menu.InputMethod: MenuInputMethod.Buttons } session)
            return MenuKey.None;

        var keymap = session.Menu.Keymap;
        return navModes.Resolve(player.PlayerID, steamIds.Get(player.PlayerID)) switch
        {
            NavMode.Mouse => BoundTo(keymap, Traversal, MenuKey.None),
            NavMode.Numbers => BoundTo(keymap, ReplacedByNumbers, MenuKey.None),
            NavMode.Wasd => BoundTo(keymap, Traversal, WasdKeys),
            _ => MenuKey.None,
        };
    }

    private const MenuKey WasdKeys = MenuKey.W | MenuKey.S | MenuKey.A | MenuKey.D | MenuKey.E | MenuKey.R | MenuKey.Tab;

    // Every key the keymap gives one of these actions, except the mode's own. A key the menu binds to an
    // action of its own is the plugin's feature, not navigation, and stays.
    private static MenuKey BoundTo(IMenuKeymap keymap, HashSet<string> actions, MenuKey kept)
    {
        var keys = MenuKey.None;
        foreach (var (key, _) in Buttons)
            if ((key & kept) == MenuKey.None && keymap.TryResolve(key, out var action) && actions.Contains(action.Name))
                keys |= key;
        return keys;
    }

    // By name, as core's router matches them, so a menu's own rebind of one counts too.
    private static readonly HashSet<string> Traversal = Names(MenuActions.NavigateUp, MenuActions.NavigateDown,
        MenuActions.NavigateLeft, MenuActions.NavigateRight, MenuActions.Select, MenuActions.Close);

    // Not NavigateLeft / NavigateRight: core ignores Select on a selector, so they are the one way a
    // numbers player changes the selector they picked.
    private static readonly HashSet<string> ReplacedByNumbers = Names(MenuActions.NavigateUp, MenuActions.NavigateDown,
        MenuActions.Select, MenuActions.Close);

    private static HashSet<string> Names(params MenuActionId[] actions)
        => new(actions.Select(a => a.Name), StringComparer.OrdinalIgnoreCase);

    private static GameButtonFlags ButtonsOf(MenuKey keys)
    {
        var buttons = GameButtonFlags.None;
        foreach (var (key, bits) in Buttons)
            if ((keys & key) != MenuKey.None) buttons |= bits;
        return buttons;
    }

    // Each menu key and the engine bits core reports it from: SwiftlyS2's internal GameButtons.ToKeyKind
    // and MenuKeys.FromKeyKind, inverted. A and D each arrive on two bits.
    private static readonly (MenuKey Key, GameButtonFlags Bits)[] Buttons =
    [
        (MenuKey.Mouse1, GameButtonFlags.Mouse1), (MenuKey.Mouse2, GameButtonFlags.Mouse2),
        (MenuKey.Space, GameButtonFlags.Space), (MenuKey.Ctrl, GameButtonFlags.Ctrl),
        (MenuKey.W, GameButtonFlags.W), (MenuKey.S, GameButtonFlags.S),
        (MenuKey.A, GameButtonFlags.A | GameButtonFlags.A2), (MenuKey.D, GameButtonFlags.D | GameButtonFlags.D2),
        (MenuKey.E, GameButtonFlags.E), (MenuKey.Esc, GameButtonFlags.Esc), (MenuKey.R, GameButtonFlags.R),
        (MenuKey.Alt, GameButtonFlags.Alt), (MenuKey.Shift, GameButtonFlags.Shift),
        (MenuKey.Weapon1, GameButtonFlags.UnknownKeyWeapon1), (MenuKey.Weapon2, GameButtonFlags.UnknownKeyWeapon2),
        (MenuKey.Grenade1, GameButtonFlags.UnknownKeyGrenade1), (MenuKey.Grenade2, GameButtonFlags.UnknownKeyGrenade2),
        (MenuKey.Tab, GameButtonFlags.Tab), (MenuKey.F, GameButtonFlags.F),
    ];

    private void OnKey(IOnClientKeyStateChangedEvent e)
    {
        if (!e.Pressed) return;

        if ((uint)e.PlayerId < MaxSlots && (_masked[e.PlayerId] & MenuKeys.FromKeyKind(e.Key)) != MenuKey.None)
        {
            logger.LogWarning("{Key} reached the menu router for player {PlayerId} although CustomHUD removed it this tick, so core acted on it. Please report this with your SwiftlyS2 version.", e.Key, e.PlayerId);
            return;
        }

        if (e.Key is not (KeyKind.W or KeyKind.S or KeyKind.A or KeyKind.D or KeyKind.E or KeyKind.R or KeyKind.Tab)) return;

        var mode = navModes.Resolve(e.PlayerId, steamIds.Get(e.PlayerId));
        if (mode != NavMode.Wasd) return;

        var player = core.PlayerManager.GetPlayer(e.PlayerId);
        if (player is null || player.IsFakeClient) return;

        var session = GetOwnSession(player);
        if (session is null) return;

        if (session.Menu.InputMethod == MenuInputMethod.Buttons
            && session.Menu.Keymap.TryResolve(MenuKeys.FromKeyKind(e.Key), out _))
            return;

        switch (e.Key)
        {
            case KeyKind.W:
                session.MoveFocus(-1);
                break;
            case KeyKind.S:
                session.MoveFocus(1);
                break;
            case KeyKind.A:
            case KeyKind.D:
                // Only on a Chat menu, or one whose keymap moved NavigateLeft/Right off A and D.
                if (session.FocusedComponent is { } stepped && stepped.IsVisible(session) && stepped.IsEnabled(session))
                    MenuActivation.Run(stepped, session, e.Key == KeyKind.A ? MenuActions.NavigateLeft : MenuActions.NavigateRight,
                        MenuKeys.FromKeyKind(e.Key), translations.For(player.PlayerID), logger);
                break;
            case KeyKind.E:
                if (session.FocusedComponent is { } component)
                    MenuActivation.Select(component, session, MenuKey.E, translations.For(player.PlayerID), logger);
                break;
            default:
                // A plugin that unbound Close (MixScrims' picks) meant the menu to have no way out.
                if (FrameComposer.Closable(session.Menu)) session.Close();
                break;
        }
    }

    private void OnClicked(IOnCustomHudClickedEvent e)
    {
        var player = core.PlayerManager.GetPlayer(e.PlayerId);
        if (player is null || player.IsFakeClient) return;

        var session = GetOwnSession(player);
        if (session is null) return;
        if (navModes.Resolve(player.PlayerID, steamIds.Get(player.PlayerID)) != NavMode.Mouse) return;

        // The prefix comes off the frame the renderer actually composed, never
        // from a fresh profile lookup here: this code cannot know which
        // nav-mode profile or per-menu override the renderer resolved, and
        // guessing rejects every click against the wrong prefix.
        var rows = renderer.GetRows(player.PlayerID);
        if (rows is null || !ReferenceEquals(rows.Session, session)) return;

        // Hidden on a menu without Close, but a click can be a tick older than the frame.
        var closable = FrameComposer.Closable(session.Menu);
        if (rows.BackButton.Length > 0 && e.ButtonId == rows.BackButton)
        {
            if (closable) session.Close();
            return;
        }
        if (rows.CloseButton.Length > 0 && e.ButtonId == rows.CloseButton)
        {
            if (closable) CloseStack(player, session);
            return;
        }
        if (e.ButtonId == rows.PreviousButton || e.ButtonId == rows.NextButton)
        {
            MovePage(player, e.ButtonId == rows.PreviousButton ? -1 : 1);
            return;
        }
        if (rows.RootMenu is { } root)
        {
            if (e.ButtonId == rows.RootButton)
            {
                if (!ReferenceEquals(root, session.Menu)) root.Open(player);
                return;
            }
            if (e.ButtonId.StartsWith(rows.SidebarPrefix, StringComparison.Ordinal)
                && int.TryParse(e.ButtonId.AsSpan(rows.SidebarPrefix.Length), out var category)
                && category >= 0 && category < rows.SidebarItems.Length)
            {
                // A divider slot holds a DividerComponent and falls out here; a root action
                // (a Help button) runs on the root's session as it would on the root's page.
                var target = rows.SidebarItems[category];
                if (target is not (SwiftlyS2.Shared.Menu.Components.SubmenuComponent
                        { Visible: true, Enabled: true, VisibleWhen: null, EnabledWhen: null, TextProvider: null }
                    or SwiftlyS2.Shared.Menu.Components.ButtonComponent
                        { Visible: true, Enabled: true, VisibleWhen: null, EnabledWhen: null, TextProvider: null }))
                    return;
                var rootSession = ReferenceEquals(root, session.Menu) ? session : root.Open(player);
                renderer.KeepRootOpen(player.PlayerID, rootSession,
                    target is SwiftlyS2.Shared.Menu.Components.SubmenuComponent ? category : -1);
                if (target.IsVisible(rootSession) && target.IsEnabled(rootSession))
                    MenuActivation.Select(target, rootSession, MenuKey.Mouse1, translations.For(player.PlayerID), logger);
                return;
            }
        }

        if (rows.TabHost is { } host && rows.TabPrefix.Length > 0
            && e.ButtonId.StartsWith(rows.TabPrefix, StringComparison.Ordinal)
            && int.TryParse(e.ButtonId.AsSpan(rows.TabPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var tab)
            && tab < rows.TabItems.Length)
        {
            // From any depth under the host: reopen it and pick the tab there, as the sidebar does with the root.
            var item = rows.TabItems[tab];
            var hostSession = ReferenceEquals(host, session.Menu) ? session : host.Open(player);
            renderer.KeepTabOpen(player.PlayerID, hostSession, tab);
            if (item.IsVisible(hostSession) && item.IsEnabled(hostSession))
                MenuActivation.Select(item, hostSession, MenuKey.Mouse1, translations.For(player.PlayerID), logger);
            return;
        }

        if (rows.LinkButton.Length > 0 && e.ButtonId == rows.LinkButton)
        {
            FollowLink(player, session, rows.LinkEntry, rows.LinkPick);
            return;
        }

        if (rows.Controls.TryGetValue(e.ButtonId, out var control))
        {
            if (control.Action is { } action) SendAction(player, session, rows, control.Row, action);
            else if (control.Step != 0) NavigateRow(player, session, rows, control.Row, control.Step);
            else SetTrack(session, rows, control.Row, control.Segment, control.Segments);
            return;
        }

        if (rows.Targets.TryGetValue(e.ButtonId, out var targetRow))
        {
            if (targetRow is RowMap.PreviousPageTarget or RowMap.NextPageTarget)
                MovePage(player, targetRow == RowMap.PreviousPageTarget ? -1 : 1);
            else
                ActivateRow(player, session, rows, targetRow, MenuKey.Mouse1);
            return;
        }

        if (rows.Card is { } card)
        {
            ClickCard(player, session, rows, card, e.ButtonId);
            return;
        }

        if (rows.RowPrefix.Length == 0) return;
        if (!e.ButtonId.StartsWith(rows.RowPrefix, StringComparison.Ordinal)) return;
        if (!int.TryParse(e.ButtonId.AsSpan(rows.RowPrefix.Length), out var row)) return;
        if (row == rows.PreviousTileRow || row == rows.NextTileRow)
        {
            MovePage(player, row == rows.PreviousTileRow ? -1 : 1);
            return;
        }

        ActivateRow(player, session, rows, row, MenuKey.Mouse1);
    }

    // Core's Close() returns to the parent menu, so leaving a stack of
    // submenus takes one Close per level; each reopens the next one up.
    private void CloseStack(IPlayer player, IMenuSession session)
    {
        for (var depth = 0; depth < 32 && session.IsOpen; depth++)
        {
            var parent = session.Menu.Parent;
            session.Close();
            if (parent is null || core.Menu.GetSession(player) is not { } next
                || !ReferenceEquals(next.Menu, parent))
                break;
            session = next;
        }
        renderer.ForgetReturn(player.PlayerID);
    }

    // Numbers nav: ordinal is 1-based among the page's SELECTABLE rows, which
    // is what the layout's key hint shows.
    public void SelectByNumber(IPlayer player, int ordinal)
    {
        var session = GetOwnSession(player);
        if (session is null) return;

        var rows = renderer.GetRows(player.PlayerID);
        if (rows is null || !ReferenceEquals(rows.Session, session)) return;

        var seen = 0;
        for (var row = 0; row < rows.Focusable.Length; row++)
        {
            if (!rows.IsKeyed(row)) continue;
            if (++seen != ordinal) continue;
            ActivateRow(player, session, rows, row, MenuKey.E);
            return;
        }
    }

    public void MovePage(IPlayer player, int delta)
    {
        var session = GetOwnSession(player);
        if (session is null) return;

        var rows = renderer.GetRows(player.PlayerID);
        if (rows is null || !ReferenceEquals(rows.Session, session)) return;

        // Core exposes no page API, so a page key moves focus onto the target
        // page; the renderer draws whichever page holds focus.
        var target = delta < 0 ? rows.PreviousPageFocus : rows.NextPageFocus;
        if (target >= 0)
        {
            session.SetFocus(target);
            return;
        }

        // Core's own window (a Chat menu) slides once focus passes its edge.
        var selectable = CountSelectable(rows, 0, rows.Focusable.Length);
        session.MoveFocus(delta * Math.Max(1, selectable));
    }

    public void Close(IPlayer player)
    {
        if (GetOwnSession(player) is { } session && FrameComposer.Closable(session.Menu)) session.Close();
    }

    // A player may have a menu open that belongs to another renderer -- a
    // plugin that forced centerhtml, or a server-wide override. Acting on it
    // would move a cursor nothing of ours is drawing.
    private IMenuSession? GetOwnSession(IPlayer player)
    {
        var session = core.Menu.GetSession(player);
        if (session is null || !session.IsOpen) return null;
        return string.Equals(session.Menu.Renderer.Id, config.Current.RendererId, StringComparison.OrdinalIgnoreCase)
            ? session
            : null;
    }

    private void ActivateRow(IPlayer player, IMenuSession session, RowMap rows, int row, MenuKey key)
    {
        if (!FocusRow(session, rows, row)) return;

        var component = session.FocusedComponent;
        if (component is null) return;

        MenuActivation.Select(component, session, key, translations.For(player.PlayerID), logger);
    }

    // A stepper beside a slider's or selector's value: the same NavigateLeft / NavigateRight A and D send.
    private void NavigateRow(IPlayer player, IMenuSession session, RowMap rows, int row, int step)
    {
        if (!FocusRow(session, rows, row)) return;
        if (session.FocusedComponent is not { } component) return;
        if (!component.IsVisible(session) || !component.IsEnabled(session)) return;

        MenuActivation.Run(component, session, step < 0 ? MenuActions.NavigateLeft : MenuActions.NavigateRight,
            MenuKey.Mouse1, translations.For(player.PlayerID), logger);
    }

    // An `act=` button: the menu's own action it names, sent to its row as a key bound to it would be.
    private void SendAction(IPlayer player, IMenuSession session, RowMap rows, int row, string action)
    {
        if (!FocusRow(session, rows, row)) return;
        if (session.FocusedComponent is not { } component) return;
        if (!component.IsVisible(session) || !component.IsEnabled(session)) return;

        MenuActivation.Run(component, session, new MenuActionId(session.Menu.Id, action),
            MenuKey.Mouse1, translations.For(player.PlayerID), logger);
    }

    private static bool FocusRow(IMenuSession session, RowMap rows, int row)
    {
        if (row < 0 || row >= rows.Focusable.Length) return false;
        if (!rows.Focusable[row]) return false;   // a spacer or plain label
        if (rows.FocusedRow < 0) return false;

        // SetFocus indexes the visible FOCUSABLE components, while a click
        // reports a PHYSICAL row -- and body rows include non-focusable
        // entries, so the two are different numbers. Counting the selectable
        // rows between the cursor and the target turns one into the other
        // without depending on how core numbers focus across pages.
        var steps = row > rows.FocusedRow
            ? CountSelectable(rows, rows.FocusedRow + 1, row + 1)
            : -CountSelectable(rows, row, rows.FocusedRow);

        // The map describes the last COMPOSED frame, which may be a tick or
        // two behind the session -- a keyboard press between compose and click
        // moves core's cursor without the map knowing. Correcting by the drift
        // in FocusedIndex keeps the landing row right instead of off by that
        // much.
        steps -= session.FocusedIndex - rows.FocusedIndex;

        if (steps != 0) session.MoveFocus(steps);
        return true;
    }

    // `field_3_hit`, `field_3_aside`, `field_3_seg17` or `field_3_slot2`: the field, then which of its targets.
    private void ClickCard(IPlayer player, IMenuSession session, RowMap rows, CardMap card, string buttonId)
    {
        if (!buttonId.StartsWith(rows.RowPrefix, StringComparison.Ordinal)) return;
        var rest = buttonId.AsSpan(rows.RowPrefix.Length);
        var digits = 0;
        while (digits < rest.Length && char.IsAsciiDigit(rest[digits])) digits++;
        if (!int.TryParse(rest[..digits], NumberStyles.None, CultureInfo.InvariantCulture, out var field)
            || field >= card.Main.Length)
            return;

        var target = rest[digits..];
        if (target.SequenceEqual(card.HitSuffix))
            ActivateRow(player, session, rows, card.Main[field], MenuKey.Mouse1);
        else if (target.SequenceEqual(card.AsideSuffix))
            ActivateRow(player, session, rows, card.Aside[field], MenuKey.Mouse1);
        else if (target.StartsWith(card.SegmentSuffix, StringComparison.Ordinal)
                 && int.TryParse(target[card.SegmentSuffix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var segment))
            SetTrack(session, rows, card.Track[field], segment, card.Segments);
        else if (card.SlotSuffix.Length > 0 && target.StartsWith(card.SlotSuffix, StringComparison.Ordinal)
                 && int.TryParse(target[card.SlotSuffix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var tile)
                 && field < card.Picks.Length && tile < card.Picks[field].Length)
            PickSlot(player, session, rows, card.Main[field], card.Picks[field][tile]);
    }

    // A slot tile: its field's row opens its page, then the entry the slot names is picked there, as
    // two clicks would. Neither page is drawn in between, so the renderer is told what opened them.
    private void PickSlot(IPlayer player, IMenuSession session, RowMap rows, int row, string pick)
    {
        if (!FocusRow(session, rows, row) || session.FocusedComponent is not { } component) return;
        if (!component.IsVisible(session) || !component.IsEnabled(session)) return;

        MenuActivation.Select(component, session, MenuKey.Mouse1, translations.For(player.PlayerID), logger);
        if (pick.Length == 0 || OpenedFrom(player, session) is not { } page) return;
        renderer.RecordOpener(page, component);
        Pick(player, page, pick);
    }

    // A spot's link into another branch of the menu (an item's Properties): the root's entry, then the
    // entry `pick` names on its page. The page reached returns here, not to pages never drawn.
    private void FollowLink(IPlayer player, IMenuSession session, string entryId, string pick)
    {
        var root = FrameComposer.SidebarRoot(session.Menu);
        var entry = root.GetComponents(MenuRegion.Body).FirstOrDefault(c =>
            c is SubmenuComponent && string.Equals(c.Id, entryId, StringComparison.OrdinalIgnoreCase));
        if (entry is null || !entry.IsVisible(session) || !entry.IsEnabled(session)) return;

        var origin = session.Menu;
        var rootSession = ReferenceEquals(root, origin) ? session : root.Open(player);
        renderer.KeepRootOpen(player.PlayerID, rootSession, -1);
        MenuActivation.Select(entry, rootSession, MenuKey.Mouse1, translations.For(player.PlayerID), logger);
        if (OpenedFrom(player, rootSession) is not { } page) return;
        renderer.RecordOpener(page, entry);
        if (pick.Length > 0 && Pick(player, page, pick) is { } picked) page = picked;
        page.Menu.Parent = origin;
    }

    // Selects the entry `pick` names on an open page, focused first so Back lands on it, and returns
    // the page it opened, its opener recorded.
    private IMenuSession? Pick(IPlayer player, IMenuSession page, string pick)
    {
        var position = 0;
        foreach (var entry in page.Menu.GetComponents(MenuRegion.Body))
        {
            if (!entry.IsFocusable || !entry.IsVisible(page)) continue;
            if (!Names(entry, pick))
            {
                position++;
                continue;
            }
            if (!entry.IsEnabled(page)) return null;
            page.SetFocus(position);
            MenuActivation.Select(entry, page, MenuKey.Mouse1, translations.For(player.PlayerID), logger);
            if (OpenedFrom(player, page) is not { } next) return null;
            renderer.RecordOpener(next, entry);
            return next;
        }
        return null;
    }

    // The session a plain submenu just opened from `parent`, or null when the activation opened none.
    private IMenuSession? OpenedFrom(IPlayer player, IMenuSession parent)
        => core.Menu.GetSession(player) is { } opened && !ReferenceEquals(opened, parent)
           && ReferenceEquals(opened.Menu.Parent, parent.Menu)
            ? opened
            : null;

    // The entry a slot's pick names: the item it stands for, else its Tag, written out.
    private static bool Names(IMenuComponent entry, string pick)
        => (Integrations.CollectionItems.Of(entry) ?? (entry as MenuComponentBase)?.Tag) is { } value
           && string.Equals(Convert.ToString(value, CultureInfo.InvariantCulture), pick, StringComparison.Ordinal);

    // Segment n of `segments` is n / (segments - 1) of the way along, snapped
    // to the slider's Step. SetValue runs OnChanged exactly as a key press does.
    private static void SetTrack(IMenuSession session, RowMap rows, int row, int segment, int segments)
    {
        if (segment < 0 || segment >= segments) return;
        if (!FocusRow(session, rows, row)) return;
        if (session.FocusedComponent is not SliderComponent slider) return;
        if (!slider.IsVisible(session) || !slider.IsEnabled(session)) return;

        var min = MathF.Min(slider.Min, slider.Max);
        var max = MathF.Max(slider.Min, slider.Max);
        var target = min + (max - min) * segment / Math.Max(1, segments - 1);
        var snapped = min + MathF.Round((target - min) / slider.Step) * slider.Step;
        slider.SetValue(session, MathF.Round(Math.Clamp(snapped, min, max), 6));
    }

    private static int CountSelectable(RowMap rows, int start, int endExclusive)
    {
        var count = 0;
        for (var i = Math.Max(0, start); i < Math.Min(endExclusive, rows.Focusable.Length); i++)
            if (rows.Focusable[i]) count++;
        return count;
    }
}
