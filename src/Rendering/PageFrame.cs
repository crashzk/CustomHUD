using CustomHUD.Config;
using CustomHUD.Integrations;
using SwiftlyS2.Shared.Menu;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Translation;

namespace CustomHUD.Rendering;

// What one page may hold. Items and Lines are core's two limits (ItemsPerPage,
// MaxVisibleLines, which "MenuConfig": "customhud" takes from config.jsonc instead);
// Focusables is numbers mode's, whose keys 1-6 cannot reach a seventh keyed row.
internal readonly record struct PageBudget(int Items, int Lines, int Focusables)
{
    // Slots every page gives up once the body runs past one: the PageTiles cards.
    public int Reserve { get; init; }

    // A scrolling layout's chunk: its slot count, nothing else.
    public static PageBudget Rows(int rows, int focusables = int.MaxValue, int reserve = 0)
        => new(rows, int.MaxValue, focusables) { Reserve = reserve };

    // Core's MenuFrameComposer.Measure, plus the focusable limit. `keyed` says which
    // focusables take a number key: a disabled row gets none, so it costs no key.
    // `offMain` rows are drawn in a region of the page, so they take no main-list slot.
    public int Measure(IReadOnlyList<IMenuComponent> body, int start, Func<IMenuComponent, bool>? keyed = null,
        Func<IMenuComponent, bool>? offMain = null)
    {
        var count = 0;
        var items = 0;
        var lines = 0;
        var focusables = 0;
        for (var i = start; i < body.Count && items < Items; i++)
        {
            var component = body[i];
            var cost = Math.Max(1, component.LineCount);
            var takesKey = component.IsFocusable && (keyed is null || keyed(component));
            if (count > 0 && (lines + cost > Lines || (takesKey && focusables >= Focusables))) break;
            lines += cost;
            if (takesKey) focusables++;
            count++;
            if (offMain?.Invoke(component) != true) items++;
        }
        return Math.Max(1, count);
    }

    // What a page of `count` rows from `start` can still take in its main list, counted as Measure counts.
    public int Room(IReadOnlyList<IMenuComponent> body, int start, int count, Func<IMenuComponent, bool>? offMain = null)
    {
        var items = 0;
        var lines = 0;
        for (var i = start; i < start + count; i++)
        {
            lines += Math.Max(1, body[i].LineCount);
            if (offMain?.Invoke(body[i]) != true) items++;
        }
        return Math.Max(0, Math.Min(Items - items, Lines - lines));
    }
}

// Index and Total count only pages holding something focusable: a page is chosen
// by where focus is, so a page of nothing but text can never be shown. The focus
// targets are SetFocus indexes and wrap like core's MoveFocus; -1 on one page.
internal sealed record MenuPage(
    int Start, int Count, int Index, int Total, int FocusablesBefore, int PreviousFocus, int NextFocus)
{
    // Focusable and enabled entries before this page: where its row numbers continue from.
    public int KeyedBefore { get; init; }

    // Set instead of a page when the cursor sits on a label (PageFrame.LabelSkip): the entry to move it to.
    public int Retarget { get; init; } = -1;

    public static MenuPage Single(int count) => new(0, count, 0, 1, 0, -1, -1);
}

// Core slides its window a row at a time and backfills the last page
// (MenuFrameComposer.ResolvePage), so its pages overlap and move with the
// cursor. This cuts the body into fixed pages from the top instead.
internal static class PageFrame
{
    // GAME THREAD ONLY: row sources, IsVisible, LineCount and Render are consumer
    // code, which core itself only ever runs there. Renders only the page holding focus.
    // An integration (IntegrationHost) draws over the rendered rows before focus markers go on.
    public static (MenuFrame Frame, MenuPage Page) Build(
        IMenuRenderContext context,
        IMenuRendererRegistry registry,
        IPlayerManagerService players,
        Func<string, string> translate,
        PageBudget budget,
        Action<IMenuComponent, Exception> onRenderError,
        PageIntegrationContext? integration = null,
        int? cameFrom = null)
    {
        var menu = context.Menu;
        var session = context.Session;
        var rowContext = new MenuRowContext { Translate = translate, Players = players };

        var body = new List<IMenuComponent>();
        foreach (var component in menu.GetComponents(MenuRegion.Body))
        {
            IReadOnlyList<IMenuComponent> rows = component is IMenuRowSource source
                ? source.GetRows(session, rowContext)
                : [component];
            foreach (var row in rows)
                if (row.IsVisible(session)) body.Add(row);
        }

        // Resolved from this expansion, as core does: a row source may hand out
        // new instances per call, so session.FocusedComponent need not be one of them.
        var focusableCount = body.Count(c => c.IsFocusable);
        var added = integration is null ? 0 : integration.Host.RowsAdded(integration.Integration, menu, integration.Regions);
        if (added > 0)
            budget = budget with { Items = Math.Max(1, Math.Min(budget.Items, integration!.RowSlots - added)) };
        if (budget.Reserve > 0 && body.Count > budget.Items)
            budget = budget with { Items = Math.Max(1, budget.Items - budget.Reserve), Reserve = 0 };
        var focusPosition = -1;
        if (focusableCount > 0)
        {
            var target = Math.Clamp(session.FocusedIndex, 0, focusableCount - 1);
            for (int i = 0, seen = 0; i < body.Count; i++)
                if (body[i].IsFocusable && seen++ == target) { focusPosition = i; break; }
        }

        var pages = new List<(int Start, int Count, int FocusablesBefore, int KeyedBefore, bool Reachable)>();
        var current = 0;
        Func<IMenuComponent, bool>? offMain = integration is { Regions.Count: > 0 } hook
            ? component => hook.Host.RegionOf(hook.Integration, menu, component) is { } region && hook.Regions.Contains(region)
            : null;
        for (int start = 0, before = 0, keyedBefore = 0; start < body.Count;)
        {
            var count = budget.Measure(body, start, c => c.IsEnabled(session), offMain);
            var focusables = 0;
            var keyed = 0;
            for (var i = start; i < start + count; i++)
            {
                if (!body[i].IsFocusable) continue;
                focusables++;
                if (body[i].IsEnabled(session)) keyed++;
            }
            if (focusPosition >= start && focusPosition < start + count) current = pages.Count;
            pages.Add((start, count, before, keyedBefore, focusables > 0));
            before += focusables;
            keyedBefore += keyed;
            start += count;
        }

        var page = pages.Count > 0 ? pages[current] : (Start: 0, Count: 0, FocusablesBefore: 0, KeyedBefore: 0, Reachable: false);
        if (cameFrom is { } from && focusPosition >= 0 && IsLabel(body[focusPosition])
            && LabelSkip(body, page.Start, page.Count, focusableCount, session.FocusedIndex, from) is { } skip)
            return (context.Frame, MenuPage.Single(0) with { Retarget = skip });
        var reachable = pages.Where(p => p.Reachable).ToList();
        var index = Math.Max(0, reachable.IndexOf(page));
        var (previous, next) = reachable.Count > 1
            ? (reachable[(index + reachable.Count - 1) % reachable.Count].FocusablesBefore,
               reachable[(index + 1) % reachable.Count].FocusablesBefore)
            : (-1, -1);

        var rendererId = menu.Renderer.Id;
        var numbered = menu.InputMethod == MenuInputMethod.Chat;
        var components = new IMenuComponent[page.Count];
        var rendered = new MenuNode[page.Count];
        var specialisedRows = new bool[page.Count];
        var enabledRows = new bool[page.Count];
        for (var k = 0; k < page.Count; k++)
        {
            var i = page.Start + k;
            var component = body[i];
            var enabled = component.IsEnabled(session);
            var specialised = false;
            MenuNode node;
            try
            {
                var renderContext = new RenderContext(menu, session, context.Player, component,
                    i == focusPosition, enabled, rendererId, translate);
                MenuNode? output = null;
                specialised = registry.ResolveComponentRenderer(rendererId, component.GetType()) is { } renderer
                    && renderer.TryRender(component, renderContext, out output) && output is not null;
                node = specialised ? output! : component.Render(renderContext);
            }
            catch (Exception ex)
            {
                // Core never rendered this row, so nothing else would surface the
                // failure; a blank keeps row indices aligned with core's focus.
                onRenderError(component, ex);
                node = new MenuTextNode(string.Empty);
            }
            components[k] = component;
            rendered[k] = node;
            specialisedRows[k] = specialised;
            enabledRows[k] = enabled;
        }

        PageDraw? draw = null;
        if (integration is { } owner)
        {
            draw = new PageDraw(owner.Host, session, rendererId, components, rendered, context.Frame.Header, index, Math.Max(1, reachable.Count),
                page.Start, body.Count, owner.Strings, owner.Regions, budget.Room(body, page.Start, page.Count, offMain), owner.Icons, owner.View);
            owner.Host.Draw(owner.Integration, draw);
        }

        var nodes = new List<MenuNode>(page.Count);
        var selectables = 0;
        for (var k = 0; k < page.Count; k++)
        {
            if (draw is not null) nodes.AddRange(draw.AddedBefore(k));
            var node = draw is null ? rendered[k] : draw.NodeAt(k);
            if (node is null) continue;

            // The page was measured with disabled rows taking no key; the composer only
            // knows a row is disabled from this marker, which our component renderer adds
            // unless an integration drew the row in its place.
            if (!enabledRows[k] && (!specialisedRows[k] || draw?.Redrawn(k) == true))
                node = MenuLineNode.Of(node, new MenuRawNode(rendererId, "disabled"));

            if (components[k].IsFocusable)
            {
                selectables++;
                node = MenuLineNode.Of(new MenuSelectionNode(page.Start + k == focusPosition, numbered ? selectables : 0), node);
            }
            nodes.Add(node);
        }
        if (draw is not null) nodes.AddRange(draw.AddedBefore(page.Count));

        var header = draw is null ? context.Frame.Header : [.. draw.ProviderHeader, .. draw.Header];
        var frame = context.Frame with { Header = header, Body = nodes, PageOffset = page.Start, TotalItems = focusableCount };
        return (frame, new MenuPage(page.Start, page.Count, index, Math.Max(1, reachable.Count),
            page.FocusablesBefore, previous, next) { KeyedBefore = page.KeyedBefore });
    }

    private static bool IsLabel(IMenuComponent component)
        => component.IsFocusable && PanoramaComponentRenderer.IsFact(component.GetType());

    // A label holds core's cursor like an option. On a page that also has something to act on, the
    // cursor moves on past it, the way it was going; a page of labels alone keeps it, or could never
    // be reached. The answer is a SetFocus index; null leaves focus where it is.
    private static int? LabelSkip(List<IMenuComponent> body, int start, int count, int focusables, int focused, int from)
    {
        var actionable = false;
        for (var i = start; i < start + count && !actionable; i++)
            actionable = body[i].IsFocusable && !IsLabel(body[i]);
        if (!actionable) return null;

        var labels = body.Where(c => c.IsFocusable).Select(IsLabel).ToArray();
        var at = Math.Clamp(focused, 0, focusables - 1);
        // One step back is the only move upward; a page jump or the wrap past the end goes down.
        var step = from >= 0 && (at - from + focusables) % focusables == focusables - 1 ? -1 : 1;
        for (var n = 1; n < focusables; n++)
        {
            var next = ((at + step * n) % focusables + focusables) % focusables;
            if (!labels[next]) return next;
        }
        return null;
    }

    // RENDER THREAD. Whether core's frame has its cursor on a label, which PanoramaComponentRenderer marks `info`.
    public static bool FocusOnLabel(MenuFrame frame, string rendererId)
    {
        foreach (var node in frame.Body)
            if (node is MenuLineNode { Children: [MenuSelectionNode { Focused: true }, ..] } line)
                return line.Children.Skip(1).Any(child => Marks(child, rendererId, "info"));
        return false;
    }

    private static bool Marks(MenuNode node, string rendererId, string token) => node switch
    {
        MenuRawNode raw => string.Equals(raw.RendererId, rendererId, StringComparison.OrdinalIgnoreCase)
            && raw.Payload.Split(';').Any(part => part.Trim().Equals(token, StringComparison.OrdinalIgnoreCase)),
        MenuLineNode line => line.Children.Any(child => Marks(child, rendererId, token)),
        MenuStackNode stack => stack.Children.Any(child => Marks(child, rendererId, token)),
        _ => false,
    };

    // RENDER THREAD. True when core's frame already is the menu's only page --
    // the common case -- so the compose can stay off the game thread.
    public static bool FitsOnePage(MenuFrame frame, PageBudget budget)
    {
        // Every entry costs at least one line.
        if (frame.PageOffset != 0 || frame.Body.Count > budget.Items || frame.Body.Count > budget.Lines) return false;
        var focusables = 0;
        foreach (var node in frame.Body)
            if (node is MenuLineNode { Children: [MenuSelectionNode, ..] }) focusables++;
        return focusables == frame.TotalItems && focusables <= budget.Focusables;
    }

    private sealed record RenderContext(
        IMenu Menu,
        IMenuSession Session,
        IPlayer Player,
        IMenuComponent Component,
        bool IsFocused,
        bool IsEnabled,
        string RendererId,
        Func<string, string> Translate) : IMenuComponentRenderContext;
}

// The core context with its page swapped for one built here. A null Page means
// core's own window is drawn as it came (a Chat menu, whose !N core resolves).
internal sealed record FrameContext(IMenuRenderContext Inner, MenuFrame Frame, MenuPage? Page) : IMenuRenderContext
{
    public IMenu Menu => Inner.Menu;
    public IMenuSession Session => Inner.Session;
    public IPlayer Player => Inner.Player;
    public void ReportUnsupported(MenuNode node) => Inner.ReportUnsupported(node);

    // Game thread: the sidebar entry and tab this player last opened through
    // CustomHUD, for plugins whose entries carry neither the page's id nor its title.
    public (IMenu Root, int Index)? RememberedEntry { get; init; }
    public (IMenu Host, int Index)? RememberedTab { get; init; }
}

// What the render thread resolved for a frame the game thread has to page.
internal sealed record DeferredCompose(
    IMenuRenderContext Context,
    MenuLayoutProfile Profile,
    string LayoutPath,
    NavMode Mode,
    int ScalePercent,
    ulong SteamId,
    PageBudget Budget)
{
    public IPageIntegration? Integration { get; init; }
}

// The integration drawing a page, with what it draws through: the layout's regions with slots
// (menu_locker's banner, foot ...), where a row routed there takes no main-list slot, and the
// main list's slot count. Icons is the layout's label-to-glyph map (MenuSidebarProfile.Icons);
// View the page's view from the operator's PageViews and the profile, before any `view=`.
internal sealed record PageIntegrationContext(
    IntegrationHost Host, IPageIntegration Integration, ILocalizer Strings, IReadOnlySet<string> Regions, int RowSlots,
    IReadOnlyDictionary<string, string> Icons, string? View = null);
