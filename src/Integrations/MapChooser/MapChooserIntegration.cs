using System.Globalization;
using System.Runtime.CompilerServices;
using CustomHUD.Config;
using MapChooser.Contract;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Menu;
using SwiftlyS2.Shared.Menu.Components;

namespace CustomHUD.Integrations.MapPages;

// MapChooser's pages with each map's art and state, read from IMapChooserApi: every map row carries the
// map's name as Tag, which GetMap resolves. The ballot shows each option's share, votes and the player's
// pick under the vote's clock and turnout; the pool pages show player ranges, nominations and why a map
// can't be picked; the cycle shows each map's place with its moves on its row. After voting, MapChooser
// closes the ballot; CustomHUD then opens a read-only tally in its place, under the same hub, which
// closes when the vote ends.
internal sealed class MapChooserIntegration : IPageIntegration, IDisposable
{
    private const string RowsView = "maps-rows";
    private const string CycleView = "maps-cycle";
    private const int MaxPips = 20;

    private readonly IMapChooserApi _api;
    private readonly ISwiftlyCore _core;
    private readonly IntegrationHost _host;
    private readonly Dictionary<int, IMenu> _tallies = [];
    // The hub each player's last ballot opened under, so the tally that replaces it keeps the sidebar.
    private readonly Dictionary<int, IMenu?> _hubs = [];
    private readonly CancellationTokenSource _clock;

    private MapChooserIntegration(IMapChooserApi api, ISwiftlyCore core, IntegrationHost host)
    {
        _api = api;
        _core = core;
        _host = host;
        _api.VoteChanged += OnVoteChanged;
        _api.PlayerVoted += OnPlayerVoted;
        _api.NominationsChanged += OnNominationsChanged;
        // The chips count down; MapChooser's own timer only reopens menus, it never redraws one.
        _clock = core.Scheduler.RepeatBySeconds(1f, OnClock);
    }

    public string Provider => "MapChooser";

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IPageIntegration? TryCreate(IInterfaceManager interfaces, ISwiftlyCore core, IntegrationHost host)
    {
        ApiProbe.Require(typeof(IMapChooserApi), "MapChooser 1.6.0", nameof(IMapChooserApi.GetAvailability), nameof(IMapChooserApi.NextMap));
        return interfaces.TryGetSharedInterface<IMapChooserApi>(IMapChooserApi.InterfaceKey, out var api) && api is not null
            ? new MapChooserIntegration(api, core, host)
            : null;
    }

    public bool Owns(IMenu menu) => menu.Id.StartsWith("mapchooser.", StringComparison.Ordinal);

    public string? RegionOf(IMenu menu, IMenuComponent component) => null;

    public void Draw(PageDraw page)
    {
        var tally = IsTally(page.Menu, page.Player.Slot);
        var ballot = tally || page.Menu.Id.StartsWith(MapChooserMenuIds.Vote, StringComparison.Ordinal);
        if (!tally && Hint(page.Menu.Id) is { } hint) page.Hint(T(page, hint));
        if (_api.IsVoteActive) page.AddHeader(string.Empty, "live=" + MapChooserMenuIds.Vote);

        if (ballot) DrawBallot(page, tally);
        else if (page.Menu.Id == MapChooserMenuIds.Cycle) DrawCycle(page);
        else if (page.Menu.Id == MapChooserMenuIds.CycleRemove) DrawRemoval(page);
        else DrawPool(page);
    }

    public void Dispose()
    {
        _clock.Cancel();
        _clock.Dispose();
        _api.VoteChanged -= OnVoteChanged;
        _api.PlayerVoted -= OnPlayerVoted;
        _api.NominationsChanged -= OnNominationsChanged;
        CloseTallies();
    }

    private void DrawBallot(PageDraw page, bool tally)
    {
        var slot = page.Player.Slot;
        // MapChooser drops the parent as it dismisses a voted ballot, so a late redraw must not clear it.
        if (!tally && page.Menu.Parent is { } hub) _hubs[slot] = hub;

        var total = _api.TotalVotes;
        var voters = Math.Max(_api.EligibleVoters, total);
        var clock = Clock(_api.VoteSecondsLeft);
        if (_api.IsVoteActive)
        {
            page.AddHeader($"{clock}\t{T(page, "chip.vote_ends")}", "chip=warn;glyph=stopwatch");
            page.AddHeader($"{total} / {voters}\t{T(page, "chip.voted")}", "chip;glyph=check");
        }

        var rows = page.View == RowsView;
        var mine = _api.GetPlayerVote(slot);
        for (var k = 0; k < page.Components.Count; k++)
        {
            if (page.Components[k] is not MenuComponentBase { Tag: string option }) continue;
            var extend = option == IMapChooserApi.ExtendOption;
            var map = extend ? null : _api.GetMap(option);
            var count = _api.GetVoteCount(option);
            var share = total > 0 ? (int)Math.Round(100.0 * count / total, MidpointRounding.AwayFromZero) : 0;

            var parts = new List<string>(8) { $"text=\t{share};vunit=%;bar={share}" };
            if (Art(map) is { Length: > 0 } art) parts.Add(art);
            if (extend)
            {
                // What an extend adds: beside the votes on a card, under the name on a row.
                parts.AddRange(["tone=extend", "glyph=timer"]);
                if (rows) parts.AddRange(["sub=" + Extend(page, "long"), "note=" + Votes(page, count)]);
                else parts.Add("note=" + string.Join(" · ", new[] { Votes(page, count), Extend(page, "short") }.Where(s => s.Length > 0)));
            }
            else
            {
                if (map is not null) parts.Add(Badge(page, map));
                parts.Add("note=" + Votes(page, count));
            }
            if (option == mine) parts.AddRange(["chosen", "tag=" + T(page, "your_vote"), "tagtone=accent"]);
            page.Decorate(k, string.Join(';', parts));
        }

        // The poll rows' turnout line; the cards and the keyboard list carry it in the chips alone.
        if (rows && _api.IsVoteActive && voters > 0)
        {
            // One pip per player, up to what the layout draws; past that each pip is a share.
            var (lit, of) = voters <= MaxPips ? (Math.Min(total, voters), voters) : ((int)Math.Round((double)MaxPips * total / voters), MaxPips);
            page.Add(T(page, "turnout", total, voters), $"at=foot;info;pips={lit}/{of};note={T(page, "ends_in", clock)};notetone=bad");
        }
    }

    private void DrawPool(PageDraw page)
    {
        var nominate = page.Menu.Id == MapChooserMenuIds.Nominate;
        var startVote = page.Menu.Id == MapChooserMenuIds.StartVote;
        if (MapChip(page) is { } chip) page.AddHeader(chip.Text, chip.Payload);

        var mine = nominate ? _api.GetPlayerNomination(page.Player.Slot) : null;
        var nominated = nominate ? _api.Nominations : [];
        for (var k = 0; k < page.Components.Count; k++)
        {
            if (page.Components[k] is not MenuComponentBase { Tag: string name } row)
            {
                if (startVote && page.Components[k] is ButtonComponent start) StartAction(page, k, start);
                continue;
            }
            var map = _api.GetMap(name);
            if (map is null) continue;

            var parts = new List<string>(7) { "sub=" + Players(page, map), "subtone=info", Badge(page, map) };
            if (Art(map) is { Length: > 0 } art) parts.Add(art);
            // Start a vote ticks maps: a ticked one is outlined like a pick.
            if (row is ToggleComponent toggle && toggle.GetValue(page.Session)) parts.Add("chosen");
            if (nominate)
            {
                if (Unavailable(page, map) is { } reason) parts.Add(reason);
                else if (string.Equals(name, mine, StringComparison.OrdinalIgnoreCase))
                    parts.AddRange(["chosen", "tag=" + T(page, "your_pick"), "tagtone=accent"]);
                else if (nominated.Contains(name, StringComparer.OrdinalIgnoreCase))
                    parts.AddRange(["tag=" + T(page, "nominated"), "tagtone=info"]);
            }
            page.Decorate(k, string.Join(';', parts));
        }
    }

    // Start a vote's own button, among the maps: an action card whose line is the button's hint, which
    // says what it does or why it can't in MapChooser's words. Ready to start, it takes the accent.
    private static void StartAction(PageDraw page, int k, ButtonComponent start)
    {
        var parts = new List<string>(4) { "group=start", "glyph=ticket" };
        if (start.GetHint(page.Session, static key => key) is { Length: > 0 } hint) parts.Add("sub=" + Directive.Clean(hint));
        if (start.IsEnabled(page.Session)) parts.Add("tone=ready");
        page.Decorate(k, string.Join(';', parts));
    }

    // The cycle in order. Every row is a map, so a row's place is its map's: on a later page the count
    // starts at FirstRow. MapChooser's rows take MoveUp, MoveDown and Remove as their own actions.
    private void DrawCycle(PageDraw page)
    {
        if (MapChip(page) is { } chip) page.AddHeader(chip.Text, chip.Payload);

        var column = page.View == CycleView;
        var next = _api.NextMap;
        var place = page.FirstRow;
        for (var k = 0; k < page.Components.Count; k++)
        {
            if (page.Components[k] is not MenuComponentBase { Tag: string name } || _api.GetMap(name) is not { } map) continue;
            var parts = new List<string>(10) { "sub=" + Players(page, map), "subtone=info", Badge(page, map) };
            if (Art(map) is { Length: > 0 } art) parts.Add(art);
            if (_api.GetAvailability(map.Name) == MapChooserAvailability.CurrentMap)
                parts.AddRange(["tag=" + T(page, "now_playing"), "tagtone=good", "tagglyph=play"]);
            else if (string.Equals(map.Name, next, StringComparison.OrdinalIgnoreCase))
                parts.AddRange(["tag=" + T(page, "up_next"), "tagtone=info", "tagglyph=clock"]);
            parts.Add(Act(MapChooserCycleActions.MoveUp, "up", off: place == 0));
            parts.Add(Act(MapChooserCycleActions.MoveDown, "down", off: place == page.RowCount - 1));
            parts.Add(Act(MapChooserCycleActions.Remove, "trash", danger: true));
            // MapChooser's label carries the place and an arrow on the current and next map, which the tags replace.
            var label = Directive.Clean(map.Name);
            place++;
            page.Draw(k, column ? $"{label}\t{place}" : $"{place}. {label}", string.Join(';', parts));
        }
    }

    private static string Act(string action, string glyph, bool off = false, bool danger = false)
        => $"act={action}|{glyph}" + (danger ? "|danger" : "") + (off ? "|off" : "");

    // The question before a removal, under the list: the map from the row that opened it, then
    // MapChooser's Yes and Cancel, Yes in red.
    private void DrawRemoval(PageDraw page)
    {
        if (page.Opener is string name && _api.GetMap(name) is { } map)
        {
            var parts = new List<string>(4) { "info", Badge(page, map) };
            if (Art(map) is { Length: > 0 } art) parts.Add(art);
            page.Add(Directive.Clean(map.Name), string.Join(';', parts), 0);
        }
        if (page.Components is [ButtonComponent, ButtonComponent]) page.Decorate(0, "danger;glyph=trash");
    }

    // A stock map shows its screenshot and logo. Any other map shows its prefix (aim_redline: AIM) over
    // stripes in that prefix's tone. A workshop map's own name is not known until it loads, so its prefix
    // comes from its pool name when that is shaped like a map's.
    private static string? Art(MapChooserMap? map)
    {
        if (map is null) return null;
        var loaded = map.MapName?.Trim().ToLowerInvariant();
        if (loaded is not null && LockerProfiles.StockMaps.Contains(loaded)) return "image=map/" + loaded;

        var prefix = Prefix(loaded ?? map.Name.Trim().ToLowerInvariant());
        if (prefix is not null) return $"mark={prefix.ToUpperInvariant()};tone={prefix}";
        return map.WorkshopId is null ? null : "mark=WS";
    }

    private static string? Prefix(string name)
    {
        var cut = name.IndexOf('_');
        if (cut <= 0 || cut == name.Length - 1) return null;
        var prefix = name[..Math.Min(cut, 6)];
        return Directive.IsToken(prefix) ? prefix : null;
    }

    // Why a map can't be nominated, as its tag; null when it can be.
    private string? Unavailable(PageDraw page, MapChooserMap map) => _api.GetAvailability(map.Name) switch
    {
        MapChooserAvailability.Cooldown => $"tag={T(page, "cooldown")};tagtone=dark;tagglyph=lock",
        MapChooserAvailability.TooFewPlayers => $"tag={T(page, "needs", map.MinPlayers)};tagtone=dark;tagglyph=teamplayer",
        MapChooserAvailability.TooManyPlayers => $"tag={T(page, "too_many")};tagtone=dark;tagglyph=teamplayer",
        _ => null,
    };

    private (string Text, string Payload)? MapChip(PageDraw page)
    {
        if (_api.MapSecondsLeft is { } seconds) return ($"{Clock(seconds)}\t{T(page, "chip.time_left")}", "chip;glyph=clock");
        if (_api.MapRoundsLeft is { } rounds) return ($"{rounds}\t{T(page, "chip.rounds_left")}", "chip;glyph=clock");
        return null;
    }

    // "+15 min · 2 left" on the board, "+15 minutes · 2 extends left" on a row.
    private string Extend(PageDraw page, string form)
    {
        var steps = new List<string>(3);
        if (_api.ExtendMinutes > 0) steps.Add(T(page, $"extend.minutes.{form}", _api.ExtendMinutes));
        if (_api.ExtendRounds > 0) steps.Add(T(page, $"extend.rounds.{form}", _api.ExtendRounds));
        steps.Add(T(page, $"extend.left.{form}", _api.ExtendsLeft));
        return string.Join(" · ", steps.Where(s => s.Length > 0));
    }

    // The map's kind, boxed under its name on every page and layout: a workshop map's in blue.
    private static string Badge(PageDraw page, MapChooserMap map)
        => map.WorkshopId is not null ? $"badge={T(page, "kind.workshop")};badgetone=info"
            : map.MapName is { } loaded && LockerProfiles.StockMaps.Contains(loaded.Trim().ToLowerInvariant()) ? "badge=" + T(page, "kind.stock")
            : "badge=" + T(page, "kind.custom");

    private static string Players(PageDraw page, MapChooserMap map) => (map.MinPlayers, map.MaxPlayers) switch
    {
        ( <= 0, <= 0) => T(page, "players.any"),
        ( > 0, > 0) => T(page, "players.range", map.MinPlayers, map.MaxPlayers),
        ( > 0, _) => T(page, "players.min", map.MinPlayers),
        _ => T(page, "players.max", map.MaxPlayers),
    };

    private static string Votes(PageDraw page, int count) => T(page, count == 1 ? "votes.one" : "votes.other", count);

    private static string Clock(int seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : $"{(int)time.TotalMinutes}:{time.Seconds:00}";
    }

    // What a click does on each page; the read-only tally and the admin pages keep the default.
    private static string? Hint(string menuId) => menuId switch
    {
        _ when menuId.StartsWith(MapChooserMenuIds.Vote, StringComparison.Ordinal) => "hint",
        MapChooserMenuIds.Votemap => "hint",
        MapChooserMenuIds.Nominate => "hint.nominate",
        MapChooserMenuIds.ChangeMap => "hint.change",
        MapChooserMenuIds.SetNextMap => "hint.next",
        MapChooserMenuIds.StartVote => "hint.start",
        MapChooserMenuIds.Cycle => "hint.cycle",
        _ => null,
    };

    private bool IsTally(IMenu menu, int slot) => _tallies.TryGetValue(slot, out var tally) && ReferenceEquals(tally, menu);

    // A ballot's counts change with every other player's vote, so every open board redraws.
    private void OnVoteChanged()
    {
        if (!_api.IsVoteActive)
        {
            CloseTallies();
            _hubs.Clear();
        }
        Invalidate(_ => true);
    }

    private void OnNominationsChanged() => Invalidate(menu => menu.Id == MapChooserMenuIds.Nominate);

    private void OnClock()
    {
        var vote = _api.IsVoteActive;
        var map = _api.MapSecondsLeft is not null;
        if (vote || map) Invalidate(menu => menu.Id.StartsWith(MapChooserMenuIds.Vote, StringComparison.Ordinal) ? vote : map);
    }

    private void Invalidate(Func<IMenu, bool> which)
    {
        foreach (var player in _core.PlayerManager.GetAllPlayers())
            if (_core.Menu.GetSession(player) is { IsOpen: true } session && Owns(session.Menu) && which(session.Menu))
                session.Invalidate();
    }

    // Next tick: MapChooser closes the ballot after raising this, and that close must not take the tally with it.
    private void OnPlayerVoted(int slot, string option) => _core.Scheduler.NextTick(() =>
    {
        if (!_api.IsVoteActive || _core.PlayerManager.GetPlayer(slot) is not { IsValid: true, IsFakeClient: false } player) return;

        // The Vote page's id, so the hub's Vote entry stays the active one; IsTally tells the two apart.
        var builder = _core.Menu.CreateMenu(MapChooserMenuIds.Vote).WithTitle(_host.Text(slot, "customhud.maps.tally"));
        foreach (var ballot in _api.VoteOptions)
        {
            var extend = ballot == IMapChooserApi.ExtendOption;
            builder.Add(new TallyRow(extend ? _host.Text(slot, "customhud.maps.extend") : ballot, ballot, extend));
        }

        var menu = builder.Build();

        // Only this renderer draws counts; any other would show a list of names that does nothing.
        if (!string.Equals(menu.Renderer.Id, _host.RendererId, StringComparison.OrdinalIgnoreCase))
        {
            menu.Dispose();
            return;
        }

        if (_tallies.Remove(slot, out var previous)) previous.Dispose();
        _tallies[slot] = menu;
        menu.Parent = _hubs.GetValueOrDefault(slot);
        menu.Open(player);
    });

    private void CloseTallies()
    {
        foreach (var menu in _tallies.Values)
        {
            menu.CloseAll();
            menu.Dispose();
        }
        _tallies.Clear();
    }

    private static string T(PageDraw page, string key, params object[] args) => page.Text("customhud.maps." + key, args);

    // Read-only: E, Shift and F also act in game, so nothing on the tally can be focused.
    private sealed class TallyRow : MenuComponentBase
    {
        private static readonly MenuTextStyle Map = MenuTextStyle.Default.WithColor("#90EE90");
        private static readonly MenuTextStyle Extend = MenuTextStyle.Default.WithColor("#FFA500");

        private readonly string _text;
        private readonly bool _extend;

        public TallyRow(string text, string option, bool extend)
        {
            _text = text;
            _extend = extend;
            Tag = option;
        }

        public override bool IsFocusable => false;

        // The ballot's own colours, which the board's view keys its tones on.
        public override MenuNode Render(IMenuComponentRenderContext context) => new MenuTextNode(_text, _extend ? Extend : Map);
    }
}
