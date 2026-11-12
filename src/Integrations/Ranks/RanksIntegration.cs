using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Ranks.Contract;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Menu;
using SwiftlyS2.Shared.Menu.Components;

namespace CustomHUD.Integrations.RankPages;

// Ranks' menus (ranks.*) as their designed pages: the rank as a hero card over the rank road,
// counters as tiles, the leaderboard as a podium over a table, settings as a form. The menus are
// Ranks' own and unchanged; every figure drawn here comes from IRanksApi.
internal sealed class RanksIntegration(IRanksApi api) : IPageIntegration, IRankChips
{
    private const int Podium = 3;

    public string Provider => "Ranks";

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IPageIntegration? TryCreate(IInterfaceManager interfaces)
    {
        ApiProbe.Require(typeof(IRanksApi), "Ranks 1.0.5",
            nameof(IRanksApi.GetLadder), nameof(IRanksApi.GetRankIcon), nameof(IRanksApi.LoadSnapshotAsync));
        ApiProbe.Require(typeof(RanksPlayerSnapshot), "Ranks 1.0.5", nameof(RanksPlayerSnapshot.Headshots));
        return interfaces.TryGetSharedInterface<IRanksApi>(IRanksApi.InterfaceKey, out var api) && api is not null
            ? new RanksIntegration(api)
            : null;
    }

    public bool Owns(IMenu menu) => menu.Id.StartsWith("ranks.", StringComparison.Ordinal);

    public string? RegionOf(IMenu menu, IMenuComponent component) =>
        menu.Id == "ranks.top" && CollectionItems.Of(component) is RanksLeaderboardEntry { Position: <= Podium } ? "banner" : null;

    // The road's heading opens every page of the ladder.
    public int RowsAdded(IMenu menu) => menu.Id == "ranks.ladder" ? 1 : 0;

    public void Draw(PageDraw page)
    {
        switch (page.Menu.Id)
        {
            case "ranks.myrank":
                AddChip(page);
                page.Hint(T(page, "hint.myrank"));
                DrawMyRank(page);
                break;
            case "ranks.ladder":
                AddChip(page);
                DrawLadder(page);
                break;
            case "ranks.stats.main":
                AddChip(page);
                page.Hint(T(page, "hint.stats"));
                if (api.GetSnapshot(page.Player.SteamID) is { } own) DrawStats(page, own);
                break;
            case "ranks.top":
                AddChip(page);
                page.Hint(T(page, "hint.top"));
                DrawTop(page);
                break;
            case "ranks.top.playtime":
                AddChip(page);
                page.Hint(T(page, "hint.top"));
                DrawTop(page, playtime: true);
                break;
            case "ranks.top.sections":
            case "ranks.top.playtime.sections":
                ChipSeasonLine(page);
                break;
            case "ranks.top.player":
                DrawPlayer(page);
                break;
            case "ranks.settings":
                AddChip(page);
                page.Hint(T(page, "hint.settings"));
                DrawSettings(page);
                break;
            case "ranks.settings.reset":
                AddChip(page);
                DrawReset(page);
                break;
        }
    }

    // The player's rank beside the title, as their scoreboard draws it.
    public void AddChip(PageDraw page)
    {
        if (api.GetSnapshot(page.Player.SteamID) is not { } me) return;
        var tiers = api.GetLadder();
        if (tiers.Count == 0) return;

        var index = IndexFor(tiers, me.Points);
        var icon = api.GetRankIcon(me.Points);

        // A CS Rating badge is the points; an emblem needs them spelled out beside it.
        var caption = Rated(icon) ? "" : $"{Directive.Number(me.Points)} {T(page, "xp")}";
        page.AddHeader($"{Directive.Clean(tiers[index].Name)}\t{caption}", "chip;" + RankArt.For(icon, index, tiers.Count).Payload);
    }

    public PlayerRank? RankOf(ulong steamId)
    {
        if (steamId == 0 || api.GetSnapshot(steamId) is not { } player) return null;
        var tiers = api.GetLadder();
        return tiers.Count == 0 ? null
            : new PlayerRank(RankArt.For(api.GetRankIcon(player.Points), IndexFor(tiers, player.Points), tiers.Count),
                player.Points, player.Kills, player.Deaths, player.Headshots);
    }

    // Ranks 1.2 names the current season on a header line under the title. A plain header line
    // joins the title, so it is drawn as a chip beside it instead.
    private static void ChipSeasonLine(PageDraw page)
    {
        if (page.Menu.GetComponents(MenuRegion.Header).OfType<TitleComponent>().FirstOrDefault()?.Text.Trim() is not { } title) return;

        for (var i = 0; i < page.HeaderLines.Count; i++)
        {
            var line = page.HeaderLines[i].Trim();
            if (line != title && line.Any(char.IsLetterOrDigit)) page.DecorateHeader(i, "chip;glyph=hourglass");
        }
    }

    // Ranks lists the rank, the XP and the progress as three lines and the ladder link last. The
    // first line becomes the hero card; the other two are folded into it, since it shows both.
    // The ladder page draws the same road whole, so its link only earns a place under a road
    // that is windowed.
    private void DrawMyRank(PageDraw page)
    {
        if (api.GetSnapshot(page.Player.SteamID) is not { } me) return;
        var tiers = api.GetLadder();
        if (tiers.Count == 0) return;

        int hero = -1, link = -1;
        for (var k = 0; k < page.Components.Count; k++)
        {
            if (page.Components[k] is SubmenuComponent)
            {
                if (link < 0) link = k;
                page.Decorate(k, tiers.Count > RoadSpan ? "at=foot" : "fold");
            }
            else if (hero < 0)
            {
                hero = k;
            }
            else
            {
                page.Decorate(k, "fold");
            }
        }

        if (hero >= 0) DrawHero(page, hero, tiers, me.Points);
        AddRoad(page, tiers, me.Points, windowed: true, before: link >= 0 ? link : null);
    }

    private void DrawHero(PageDraw page, int row, IReadOnlyList<RanksTier> tiers, int points)
    {
        var index = IndexFor(tiers, points);
        var tier = tiers[index];
        var next = index + 1 < tiers.Count ? tiers[index + 1] : null;
        var icon = api.GetRankIcon(points);
        var rated = Rated(icon);
        var name = Directive.Clean(tier.Name);

        var payload = new StringBuilder("info;at=banner;")
            .Append(RankArt.For(icon, index, tiers.Count).Payload)
            .Append(";type=").Append(T(page, rated ? "cs_rating" : "current_rank"))
            .Append(";sub=").Append(T(page, rated ? "band" : "position", index + 1, tiers.Count))
            .Append(";bar=").Append(Directive.Bar(Progress(tiers, index, points) * 100))
            .Append(";tag=").Append(name).Append(" · ").Append(Directive.Number(tier.Points));

        if (next is null)
        {
            payload.Append(";note=").Append(T(page, "top_band"));
        }
        else
        {
            var nextName = Directive.Clean(next.Name);
            payload.Append(";tag2=").Append(nextName).Append(" · ").Append(Directive.Number(next.Points))
                .Append(";note=").Append(T(page, "to_next", Directive.Number(next.Points - points), nextName));
        }

        // The badge already shows a CS Rating's points; an emblem gets them as the value.
        page.Draw(row, rated ? name : $"{name}\t{Directive.Number(points)} {T(page, "xp")}", payload.ToString());
    }

    // The page fits ten nodes on a line, so a longer ladder shows the seven around the player's own.
    private const int RoadSpan = 10;

    private void AddRoad(PageDraw page, IReadOnlyList<RanksTier> tiers, int points, bool windowed, int? before)
    {
        // The heading and as many nodes as the page has room for, so a paged list's page is no longer
        // than its others; fewer than three say no more than the hero card, and Show all ranks remains.
        var shown = Math.Min(windowed && tiers.Count > RoadSpan ? 7 : tiers.Count, page.Room - 1);
        if (shown < Math.Min(3, tiers.Count)) return;

        var index = IndexFor(tiers, points);
        var first = Math.Clamp(index - (shown - 1) / 2, 0, tiers.Count - shown);

        var count = T(page, "road_count", tiers.Count);
        if (shown < tiers.Count) count += " · " + T(page, "road_shown", first + 1, first + shown);
        page.Add($"{T(page, "road")}\t{count}", "section", before);

        for (var i = first; i < first + shown; i++)
            page.Add(Directive.Clean(tiers[i].Name), RoadNode(page, tiers, i, index), before);
    }

    // One line per tier, in ladder order: each becomes a node of the full road, a page of them at a time.
    private void DrawLadder(PageDraw page)
    {
        var tiers = api.GetLadder();
        if (tiers.Count == 0 || page.RowCount != tiers.Count || page.FirstRow + page.Components.Count > tiers.Count) return;

        var index = api.GetSnapshot(page.Player.SteamID) is { } me ? IndexFor(tiers, me.Points) : -1;
        var count = T(page, "road_count", tiers.Count);
        if (page.Components.Count < tiers.Count) count += " · " + T(page, "road_shown", page.FirstRow + 1, page.FirstRow + page.Components.Count);
        page.Add($"{T(page, "road")}\t{count}", "section", 0);
        for (var k = 0; k < page.Components.Count; k++)
            page.Draw(k, Directive.Clean(tiers[page.FirstRow + k].Name), RoadNode(page, tiers, page.FirstRow + k, index));
    }

    // Reached, current or still locked; with no standing (-1) nothing is marked. `road` makes the
    // list draw each as a card, like MapChooser's maps.
    private string RoadNode(PageDraw page, IReadOnlyList<RanksTier> tiers, int i, int current)
    {
        var icon = api.GetRankIcon(tiers[i].Points);
        var payload = "info;group=road;" + RankArt.For(icon, i, tiers.Count).Payload;

        // A CS Rating badge shows the threshold itself.
        if (!Rated(icon)) payload += ";sub=" + Directive.Number(tiers[i].Points);

        if (current >= 0 && i == current) payload += ";chosen;tagtone=accent;tag=" + T(page, "you");
        else if (current >= 0 && i > current) payload += ";disabled;glyph=lock";
        return payload;
    }

    // Ranks prints a line per counter, ten since playtime joined them in 1.3; each becomes a tile, its
    // figure and caption from the snapshot. Only a body of exactly those lines is drawn so (a loading or
    // empty notice stays), and a page of a paged list maps onto them by position.
    private void DrawStats(PageDraw page, RanksPlayerSnapshot s)
    {
        var tiles = Tiles(page, s);
        if (page.RowCount != tiles.Count || page.FirstRow + page.Components.Count > tiles.Count) return;
        for (var k = 0; k < page.Components.Count; k++)
            page.Draw(k, tiles[page.FirstRow + k].Value, tiles[page.FirstRow + k].Payload);
    }

    private List<(string Value, string Payload)> Tiles(PageDraw page, RanksPlayerSnapshot s)
    {
        var kd = s.Deaths == 0 ? s.Kills : Math.Round(s.Kills / (double)s.Deaths, 2);
        var headshotRate = Directive.Percent(s.Headshots, s.Kills);

        // Hits + misses: one shotgun blast is a single trigger pull that can land nine hits.
        var attempts = s.Hits + s.Misses;
        var accuracy = Directive.Percent(s.Hits, attempts);
        var rounds = s.RoundWins + s.RoundLosses;
        var matches = s.MatchWins + s.MatchLosses;

        List<(string Value, string Payload)> tiles =
        [
            Tile(page, Directive.Number(s.Kills), "kills", "kill"),
            Tile(page, Directive.Number(s.Deaths), "deaths", "health"),
            Tile(page, Directive.Decimal(kd), "kd", "graph", sub: T(page, "kd_sub")),
            Tile(page, Directive.Number(s.Assists), "assists", "team"),
            Tile(page, Directive.Number(s.Headshots), "headshots", "headshot",
                sub: T(page, "headshots_sub", Directive.Decimal(headshotRate)), share: s.Kills > 0 ? headshotRate : null, meter: "accent"),
            Tile(page, Directive.Decimal(accuracy), "accuracy", "crosshair", unit: "%",
                sub: T(page, "accuracy_sub", Directive.Number(s.Hits), Directive.Number(s.Misses)), share: attempts > 0 ? accuracy : null),
            Tile(page, Directive.Number(s.RoundWins), "rounds", "trophy", unit: T(page, "won"),
                sub: Lost(s.RoundLosses, s.RoundWins, rounds), share: rounds > 0 ? s.RoundWins * 100.0 / rounds : null, meter: "split"),
            Tile(page, Directive.Number(s.MatchWins), "matches", "star", unit: T(page, "won"),
                sub: Lost(s.MatchLosses, s.MatchWins, matches), share: matches > 0 ? s.MatchWins * 100.0 / matches : null, meter: "split"),
            Tile(page, Directive.Number(s.BombsPlanted), "bombs", "bomb", unit: T(page, "planted"),
                sub: T(page, "defused", Directive.Number(s.BombsDefused))),
        ];
        if (PlaytimeOf(s) is { } seconds) tiles.Add(PlaytimeTile(page, seconds));
        return tiles;

        // A record that never played a round has no share to state.
        string Lost(long lost, long won, long total) => total > 0
            ? T(page, "lost_share", Directive.Number(lost), Directive.Bar(won * 100.0 / total))
            : T(page, "lost", Directive.Number(lost));
    }

    // `info`: a counter is a fact, with no key and nothing to click. A meter only comes with a share.
    private static (string, string) Tile(PageDraw page, string value, string caption, string glyph,
        string? unit = null, string? sub = null, double? share = null, string? meter = null)
    {
        var payload = new StringBuilder("info;type=").Append(T(page, caption)).Append(";glyph=").Append(glyph);
        if (unit is not null) payload.Append(";unit=").Append(unit);
        if (sub is not null) payload.Append(";sub=").Append(sub);
        if (share is { } percent)
        {
            payload.Append(";bar=").Append(Directive.Bar(percent));
            if (meter is not null) payload.Append(";metertone=").Append(meter);
        }
        return (value, payload.ToString());
    }

    // The first three stand on the podium, the rest form a table under column heads, and a viewer
    // outside the list is pinned under it once their position has loaded. The playtime board is the
    // same page with time in place of points.
    private void DrawTop(PageDraw page, bool playtime = false)
    {
        var tiers = api.GetLadder();
        if (tiers.Count == 0) return;

        var viewer = page.Player.SteamID;
        var rated = Rated(api.GetRankIcon(0));

        page.Add(playtime
            ? $"#\t{(rated ? "" : T(page, "col_rank"))}\t{T(page, "col_player")}\t\t{T(page, "playtime")}"
            : rated
                ? $"#\t{T(page, "cs_rating")}\t{T(page, "col_player")}\t{T(page, "col_tier")}"
                : $"#\t{T(page, "col_rank")}\t{T(page, "col_player")}\t\t{T(page, "xp")}", "head", 0);

        var lastPosition = 0;
        bool listed = false, any = false;
        for (var k = 0; k < page.Components.Count; k++)
        {
            if (page.ItemOf(k) is not RanksLeaderboardEntry entry) continue;
            any = true;
            var mine = entry.SteamId == viewer;
            listed |= mine;
            lastPosition = Math.Max(lastPosition, entry.Position);
            var podium = entry.Position <= Podium;

            // As GetTopAsync resolves the tier: a resident record may hold points not yet saved.
            var (text, payload) = playtime
                ? PlaytimeRow(page, tiers, entry.Name, entry.Position, TierOf(tiers, entry.RankName), entry.Value, podium, mine, rated)
                : PointsRow(page, tiers, entry.Name, entry.Position,
                    api.IsLoaded(entry.SteamId) ? api.GetPoints(entry.SteamId) : (int)entry.Value, podium, mine, rated);
            page.Draw(k, text, payload);
        }

        if (!any || listed || !page.LastPage || api.GetSnapshot(viewer) is not { } me) return;
        var stat = playtime ? PlaytimeStat : "points";
        if (!page.TryLoad("position." + stat, () => api.GetPositionAsync(viewer, stat), out var position) || position <= lastPosition) return;

        if (page.Room >= 2) page.Add("· · ·", "info;group=gap");
        if (page.Room >= 1)
        {
            var (text, payload) = playtime
                ? PlaytimeRow(page, tiers, me.Name, position, IndexFor(tiers, me.Points), PlaytimeOf(me) ?? 0, podium: false, mine: true, rated)
                : PointsRow(page, tiers, me.Name, position, me.Points, podium: false, mine: true, rated);
            page.Add(text, payload);
        }
    }

    private (string Text, string Payload) PointsRow(PageDraw page, IReadOnlyList<RanksTier> tiers, string player,
        int position, int points, bool podium, bool mine, bool rated)
    {
        var index = IndexFor(tiers, points);

        // A CS Rating badge is the points; an emblem leaves them to the value column.
        var value = rated ? null : podium ? $"{Directive.Number(points)} {T(page, "xp")}" : Directive.Number(points);
        return TopRow(player, position, tiers[index].Name, RankArt.For(api.GetRankIcon(points), index, tiers.Count).Payload, value, podium, mine);
    }

    // The badge of the tier the entry names. A CS Rating would be the player's points, which this
    // board does not carry, so Premier keeps only the tier's colour.
    private (string Text, string Payload) PlaytimeRow(PageDraw page, IReadOnlyList<RanksTier> tiers, string player,
        int position, int index, long seconds, bool podium, bool mine, bool rated)
    {
        var art = "";
        if (index >= 0)
        {
            var icon = RankArt.For(api.GetRankIcon(tiers[index].Points), index, tiers.Count);
            art = (rated ? icon with { Directive = "" } : icon).Payload;
        }
        return TopRow(player, position, index < 0 ? "" : tiers[index].Name, art, Duration(page, seconds), podium, mine);
    }

    private static (string Text, string Payload) TopRow(string player, int position, string tier, string art, string? value,
        bool podium, bool mine)
    {
        var payload = new StringBuilder(podium ? "at=banner;" : "").Append(art)
            .Append(";tag=#").Append(position)
            .Append(";sub=").Append(Directive.Clean(tier));
        if (mine) payload.Append(";chosen");

        var name = Directive.Clean(player);
        return (value is null ? name : $"{name}\t{value}", payload.ToString());
    }

    // Opened from a leaderboard row, whose entry says whose counters these are.
    private void DrawPlayer(PageDraw page)
    {
        if (page.Opener is not RanksLeaderboardEntry entry) return;
        if (page.TryLoad("snapshot", () => api.LoadSnapshotAsync(entry.SteamId), out var snapshot) && snapshot is not null)
            DrawStats(page, snapshot);
    }

    // A form: the setting under a heading with a line explaining it, and the reset, always last,
    // fenced off as a card whose button is Ranks' own reset entry.
    private static void DrawSettings(PageDraw page)
    {
        var last = page.Components.Count - 1;
        if (last < 0) return;

        page.Add(T(page, "notifications"), "section", 0);
        if (page.Components[0] is ToggleComponent)
            page.Decorate(0, "sub=" + T(page, "xp_messages_sub"));

        if (page.LastPage && last > 0 && page.Components[last] is SubmenuComponent reset)
        {
            page.Add(Directive.Clean(reset.Text), "group=danger;sub=" + T(page, "reset_sub"), last);
            page.Draw(last, T(page, "reset_button"), "inline;danger;submenu");
        }
    }

    private static void DrawReset(PageDraw page)
    {
        for (var k = 0; k < page.Components.Count; k++)
        {
            if (page.Components[k] is ButtonComponent) page.Decorate(k, "danger");
            else if (page.Components[k] is TextComponent) page.Decorate(k, "info;group=danger");
        }
    }

    private static bool Rated(RanksRankIcon icon) => icon.Mode == RanksDisplayModes.Premier;

    // Ranks' stat key for playtime, which its playtime board ranks by.
    private const string PlaytimeStat = "core.playtime";

    // Ranks 1.3's playtime, read by name: the contract this build compiles against predates it, and an
    // older Ranks has none, so its page keeps nine lines.
    private static readonly PropertyInfo? PlaytimeProperty = typeof(RanksPlayerSnapshot).GetProperty("Playtime");

    private static long? PlaytimeOf(RanksPlayerSnapshot snapshot) => PlaytimeProperty?.GetValue(snapshot) as long?;

    // Whole hours large with the exact time under them, or the minutes before the first hour.
    private static (string, string) PlaytimeTile(PageDraw page, long seconds)
    {
        var minutes = Math.Max(0, seconds) / 60;
        return minutes < 60
            ? Tile(page, Directive.Number(minutes), "playtime", "clock", unit: T(page, "minutes"))
            : Tile(page, Directive.Number(minutes / 60), "playtime", "clock", unit: T(page, "hours"), sub: Duration(page, seconds));
    }

    // As Ranks writes a playtime: 4d 5h 30m, 5h 30m or 30m.
    private static string Duration(PageDraw page, long seconds)
    {
        var total = Math.Max(0, seconds);
        var (days, hours, minutes) = (total / 86_400, total % 86_400 / 3_600, total % 3_600 / 60);
        return days > 0 ? T(page, "duration_days", days, hours, minutes)
            : hours > 0 ? T(page, "duration_hours", hours, minutes)
            : T(page, "duration_minutes", minutes);
    }

    // The tier a leaderboard entry names, as GetTopAsync resolved it from the player's points.
    private static int TierOf(IReadOnlyList<RanksTier> tiers, string name)
    {
        for (var i = tiers.Count - 1; i >= 0; i--)
            if (string.Equals(tiers[i].Name, name, StringComparison.Ordinal))
                return i;
        return -1;
    }

    // Ranks' own rule: the highest threshold the points meet.
    private static int IndexFor(IReadOnlyList<RanksTier> tiers, int points)
    {
        for (var i = tiers.Count - 1; i >= 0; i--)
            if (points >= tiers[i].Points)
                return i;
        return 0;
    }

    private static double Progress(IReadOnlyList<RanksTier> tiers, int index, int points)
    {
        if (index + 1 >= tiers.Count) return 1.0;
        var floor = tiers[index].Points;
        var ceiling = tiers[index + 1].Points;
        return ceiling <= floor ? 1.0 : Math.Clamp((points - floor) / (double)(ceiling - floor), 0.0, 1.0);
    }

    private static string T(PageDraw page, string key, params object[] args) => page.Text("customhud.ranks." + key, args);
}
