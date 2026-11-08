using SwiftlyS2.Shared.Menu;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Translation;

namespace CustomHUD.Integrations;

// One page as an integration draws it. The provider's components keep their places, so focus,
// keys and clicks still reach them: an integration changes how a row draws, drops a row nobody
// can focus, and adds rows nobody can focus. Game thread only.
internal sealed class PageDraw
{
    private readonly IntegrationHost _host;
    private readonly ILocalizer _strings;
    private readonly IReadOnlyDictionary<string, string> _icons;
    private readonly MenuNode[] _rendered;
    private readonly MenuNode?[] _nodes;
    private readonly bool[] _redrawn;
    private readonly MenuNode[] _providerHeader;
    private readonly MenuNode[] _headerNodes;
    private readonly List<(int Before, MenuNode Node)> _added = [];
    private readonly List<MenuNode> _header = [];

    public PageDraw(
        IntegrationHost host,
        IMenuSession session,
        string rendererId,
        IReadOnlyList<IMenuComponent> components,
        MenuNode[] rendered,
        IReadOnlyList<MenuNode> header,
        int pageIndex,
        int pageCount,
        int firstRow,
        int rowCount,
        ILocalizer strings,
        IReadOnlySet<string> regions,
        int room,
        IReadOnlyDictionary<string, string> icons,
        string? view = null)
    {
        _host = host;
        _strings = strings;
        _icons = icons;
        _rendered = rendered;
        _nodes = (MenuNode?[])rendered.Clone();
        _redrawn = new bool[rendered.Length];
        _providerHeader = [.. header];
        _headerNodes = [.. header];
        HeaderLines = [.. header.Select(TextOf)];
        Session = session;
        RendererId = rendererId;
        Components = components;
        PageIndex = pageIndex;
        PageCount = pageCount;
        FirstRow = firstRow;
        RowCount = rowCount;
        Regions = regions;
        Room = room;
        View = view;
    }

    public IMenuSession Session { get; }
    public IMenu Menu => Session.Menu;
    public IPlayer Player => Session.Player;
    public string RendererId { get; }
    public IReadOnlyList<IMenuComponent> Components { get; }
    public int PageIndex { get; }
    public int PageCount { get; }
    public bool LastPage => PageIndex >= PageCount - 1;

    // Where this page starts among the menu's visible rows, and how many there are: a page past
    // the first maps its rows onto a provider's list by position.
    public int FirstRow { get; }
    public int RowCount { get; }

    // Regions the layout draws (menu_locker's banner, foot ...): a row sent to one takes no main-list slot.
    public IReadOnlySet<string> Regions { get; }

    // Rows the main list can still take on this page: what is left of core's page limits in a paged
    // list, of the layout's slots in a scrolling one. A row added past it makes the page longer than
    // the list's others, or is cut.
    public int Room { get; }

    // The view drawing the page (maps-rows), or null on a layout without views; a provider's `view=` can still override it.
    public string? View { get; }

    public object? ItemOf(int index) => CollectionItems.Of(Components[index]);

    // The provider's own header lines -- its title, dividers, any text it adds -- as plain text.
    public IReadOnlyList<string> HeaderLines { get; }

    // What the player chose to open this page, or an ancestor page (Openers).
    public object? Opener => _host.Openers.Of(Menu, Player.PlayerID);

    public bool TryOpener<T>(out T value) => _host.Openers.TryOf(Menu, Player.PlayerID, out value);

    public void Decorate(int index, string payload)
    {
        if (_nodes[index] is { } node) _nodes[index] = Append(node, payload);
    }

    // Draws the component as other text: its row, key and click stay its own.
    public void Draw(int index, string text, string payload)
    {
        _nodes[index] = Row(text, payload);
        _redrawn[index] = true;
    }

    // Only a row the cursor never lands on: dropping a focusable one would hide where focus is.
    public void Hide(int index)
    {
        if (!Components[index].IsFocusable) _nodes[index] = null;
    }

    public void Add(string text, string payload, int? before = null)
        => _added.Add((Math.Clamp(before ?? Components.Count, 0, Components.Count), Row(text, payload)));

    public void AddHeader(string text, string payload) => _header.Add(Row(text, payload));

    // A provider header line drawn with a payload: "chip;…" shows it as a chip instead of joining the title.
    public void DecorateHeader(int index, string payload) => _headerNodes[index] = Append(_headerNodes[index], payload);

    // The page's footer line for a mouse player: what clicking does here. Empty keeps the default.
    public void Hint(string text)
    {
        if (text.Length > 0) _header.Add(Row(text, "hint"));
    }

    // A provider's async read, kept for this session; false until it lands, when the page redraws.
    public bool TryLoad<T>(string key, Func<Task<T>> load, out T value) => _host.TryLoad(Session, key, load, out value);

    // CustomHUD's own string in the player's language, safe as directive text.
    public string Text(string key, params object[] args)
    {
        var value = args.Length == 0 ? _strings[key] : _strings[key, args];
        return value.EndsWith(key, StringComparison.Ordinal) ? string.Empty : Directive.Clean(value);
    }

    // The layout's glyph for a label (a sidebar entry's, by its English text), or null.
    public string? Icon(string label)
        => _icons.TryGetValue(label.Trim(), out var token) && Directive.IsToken(token.Trim().ToLowerInvariant())
            ? token.Trim().ToLowerInvariant()
            : null;

    internal MenuNode? NodeAt(int index) => _nodes[index];

    internal bool Redrawn(int index) => _redrawn[index];

    internal IEnumerable<MenuNode> AddedBefore(int index) => _added.Where(a => a.Before == index).Select(a => a.Node);

    internal IReadOnlyList<MenuNode> Header => _header;

    internal IReadOnlyList<MenuNode> ProviderHeader => _headerNodes;

    // A throwing integration leaves nothing half-drawn: the page falls back to the provider's own rows.
    internal void Reset()
    {
        Array.Copy(_rendered, _nodes, _rendered.Length);
        Array.Clear(_redrawn);
        Array.Copy(_providerHeader, _headerNodes, _providerHeader.Length);
        _added.Clear();
        _header.Clear();
    }

    private static string TextOf(MenuNode node) => node switch
    {
        MenuTextNode text => text.Text,
        MenuLineNode line => string.Concat(line.Children.Select(TextOf)),
        MenuStackNode stack => string.Join(' ', stack.Children.Select(TextOf)),
        _ => string.Empty,
    };

    private MenuNode Row(string text, string payload)
        => payload.Length == 0
            ? new MenuTextNode(text)
            : MenuLineNode.Of(new MenuTextNode(text), new MenuRawNode(RendererId, payload));

    private MenuNode Append(MenuNode node, string payload)
    {
        // Into the component's own line: the composer splits a value off its first text node.
        var raw = new MenuRawNode(RendererId, payload);
        return node is MenuLineNode line ? line with { Children = [.. line.Children, raw] } : MenuLineNode.Of(node, raw);
    }
}
