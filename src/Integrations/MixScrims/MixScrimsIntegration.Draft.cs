using SwiftlyS2.Shared.Menu;
using SwiftlyS2.Shared.Players;

namespace CustomHUD.Integrations.MixPages;

internal sealed partial class MixScrimsIntegration
{
    private const int StripCells = 9;
    private const int AsideRows = 8;

    // ---- the team pick: the pick order over the page, a team down each side, the players left between them

    // MixScrims opens the page for the captain whose turn it is, a row per player left tagged with
    // their slot, and builds it anew each turn. Captains head their team's picked list, then the picks
    // in the order they were made; the pick order counts the picks after the captains, made then still
    // to come, and is empty once the teams are final.
    private void DrawDraft(PageDraw page)
    {
        var ct = _api.GetPickedCtPlayerSlots();
        var t = _api.GetPickedTPlayerSlots();
        var order = _api.GetPickOrder();
        var made = PicksMade(order, ct, t);
        var now = made < order.Count ? made : -1;

        var seconds = _api.GetPickSecondsRemaining();
        if (seconds > 0) page.AddHeader($"{Clock(seconds)}\t{T(page, "chip.to_pick")}", "chip=warn;glyph=timer");
        if (order.Count > 0) page.AddHeader($"{made} / {order.Count}\t{T(page, "chip.picked")}", "chip;glyph=lobby");

        for (var k = 0; k < page.Components.Count; k++)
            if (page.Components[k] is MenuComponentBase { Tag: int slot } && _core.PlayerManager.GetPlayer(slot) is { IsValid: true } player)
                DrawPoolRow(page, k, player);

        if (page.Regions.Contains("strip") && order.Count > 0) DrawPickOrder(page, order, ct, t, made);
        if (page.Regions.Contains("banner") && now >= 0)
        {
            var timeout = _api.GetPickTimeoutSeconds();
            var parts = $"at=banner;type={T(page, "draft.pick_of", now + 1, order.Count)}";
            if (seconds > 0 && timeout > 0)
                parts += $";note={T(page, "chip.left")};bar={Directive.Bar(Directive.Percent(seconds, timeout))}";
            page.Add(seconds > 0 ? $"{T(page, "draft.your_pick")}\t{Clock(seconds)}" : T(page, "draft.your_pick"), parts);
        }

        if (page.Regions.Contains("aside") && page.Regions.Contains("detail"))
        {
            DrawRoster(page, Team.CT, ct, order, made, "aside", AsideRows);
            DrawRoster(page, Team.T, t, order, made, "detail", AsideRows);
        }
        else if (page.Regions.Contains("foot"))
        {
            // The keyboard list draws a few lines under the pool: a team's size there, not its players.
            foreach (var side in new[] { Team.CT, Team.T })
            {
                var picked = side == Team.CT ? ct.Count : t.Count;
                page.Add($"{TeamName(side)}\t{picked}/{Size(order, side, picked)}",
                    $"at=foot;glyph={Emblem(side)};tags={TeamTag(page, side)}");
            }
        }
    }

    // A player left to pick: the rating badge, K/D and headshot rate. Ranks' figures where it holds the
    // player's snapshot, this map's K/D otherwise.
    private void DrawPoolRow(PageDraw page, int k, IPlayer player)
    {
        var parts = new List<string>(6) { "avatar", "tag2=" + T(page, "draft.pick"), "tag2tone=accent" };
        if (_ranks?.RankOf(player.SteamID) is { } rank)
        {
            parts.Insert(1, Rating(rank).TrimEnd(';'));
            parts.Add("sub=" + T(page, "draft.figures", rank.KillDeath, Directive.Decimal(Math.Round(rank.HeadshotRate))));
        }
        else
        {
            var (kd, _) = Figures(player);
            parts.Add("sub=" + T(page, "draft.kd", kd));
        }
        page.Decorate(k, string.Join(';', parts.Where(p => p.Length > 0)));
    }

    // The pick order is the picks made then a tail MixScrims projects: none while no team picks, else the
    // smaller of the picks MinimumReadyPlayers still needs (captains count) and the players left unpicked.
    private int PicksMade(IReadOnlyList<Team> order, IReadOnlyList<int> ct, IReadOnlyList<int> t)
    {
        var tail = _api.GetActivePickingTeam() is null ? 0
            : Math.Max(0, Math.Min(_api.GetMinimumReadyPlayers() - (ct.Count + t.Count), _api.GetUnpickedPlayerSlots().Count));
        return Math.Clamp(order.Count - tail, 0, order.Count);
    }

    // Whether a team's roster lines up with its picks: its captain first, then one player for every pick it
    // has made. Otherwise no player is set against a pick number.
    private bool Matched(IReadOnlyList<Team> order, Team side, IReadOnlyList<int> roster, int made)
        => roster.Count > 0 && roster[0] == (side == Team.CT ? _api.GetCtCaptainSlot() : _api.GetTCaptainSlot())
            && roster.Count - 1 == order.Take(made).Count(team => team == side);

    // One cell a pick, numbered from the first pick after the captains: who was picked, Now, or the side
    // that picks then. The strip holds nine cells, so a longer draft shows the nine around the pick being made.
    private void DrawPickOrder(PageDraw page, IReadOnlyList<Team> order, IReadOnlyList<int> ct,     IReadOnlyList<int> t, int made)
    {
        page.Add(T(page, "draft.order"), "at=strip;section");
        var now = made < order.Count ? made : -1;
        var first = Math.Clamp(made - StripCells / 2, 0, Math.Max(0, order.Count - StripCells));
        var matched = (Ct: Matched(order, Team.CT, ct, made), T: Matched(order, Team.T, t, made));
        var seen = (Ct: 0, T: 0);
        for (var i = 0; i < order.Count; i++)
        {
            var side = order[i];
            var nth = side == Team.CT ? ++seen.Ct : ++seen.T;
            if (i < first || i >= first + StripCells) continue;
            var tone = Tone(side);
            var roster = side == Team.CT ? ct : t;
            var number = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (i < made && (side == Team.CT ? matched.Ct : matched.T) && nth < roster.Count
                && _core.PlayerManager.GetPlayer(roster[nth]) is { IsValid: true } picked)
                page.Add(number, $"at=strip;tone={tone};avatar={Initial(picked.Name)}");
            else if (i == now)
                page.Add(number, $"at=strip;tone={tone};note={T(page, "draft.now")};chosen");
            else
                page.Add(number, $"at=strip;tone={tone};note={TeamTag(page, side)}");
        }
    }

    // A team as its column: its name and size over its captain, its picks, and the picks it still has to
    // make. Pick numbers count the draft's picks, as the strip does.
    private void DrawRoster(PageDraw page, Team side, IReadOnlyList<int> roster,     IReadOnlyList<Team> order, int made, string at, int rows)
    {
        var picking = made < order.Count && order[made] == side ? made : -1;
        var captain = side == Team.CT ? _api.GetCtCaptainSlot() : _api.GetTCaptainSlot();
            var matched = Matched(order, side, roster, made);
        var lines = new List<(string Text, string Payload)>(rows);
        var size = Size(order, side, roster.Count);
        var head = $"at={at};group=head;glyph={Emblem(side)};tone={Tone(side)}";
        if (Average(roster) is { } average) head += ";sub=" + T(page, "draft.avg_rating", Directive.Number(average));
        lines.Add((T(page, "draft.team", TeamName(side)) + $"\t{roster.Count}/{size}", head));

        var picks = new List<int>(order.Count);
        for (var i = 0; i < order.Count; i++)
            if (order[i] == side) picks.Add(i + 1);

        for (var n = 0; n < roster.Count; n++)
        {
            if (_core.PlayerManager.GetPlayer(roster[n]) is not { IsValid: true } player) continue;
            var sub = roster[n] == captain ? T(page, "draft.captain") : matched && n - 1 < picks.Count ? T(page, "draft.pick_n", picks[n - 1]) : "";
            var payload = $"at={at};avatar" + (sub.Length > 0 ? ";sub=" + sub : "");
            if (player.SteamID == page.Player.SteamID && player.SteamID != 0) payload += ";unit=" + T(page, "you");
            lines.Add((Directive.Clean(player.Name), payload));
        }
        // The picks still to make: those at or after the pick being made, whoever has left since.
        for (var n = picks.Count(pick => pick <= made); n < picks.Count; n++)
        {
            var now = picks[n] - 1 == picking;
            lines.Add((now ? T(page, "draft.picking_now") : T(page, "draft.pick_n", picks[n]), $"at={at};group=open" + (now ? ";chosen" : "")));
        }

        // A full region spills into the main list, so a team past the column's rows keeps its first ones.
        foreach (var (text, payload) in lines.Take(rows)) page.Add(text, payload);
    }

    // The team's final size: its captain and every pick the order gives it, or as many as it has.
    private static int Size(IReadOnlyList<Team> order, Team side, int picked)
    {
        var size = (picked > 0 ? 1 : 0) + order.Count(team => team == side);
        return Math.Max(size, picked);
    }

    // The team's average CS Rating, where Ranks rates every one of its players in Premier mode.
    private long? Average(IReadOnlyList<int> roster)
    {
        if (_ranks is null || roster.Count == 0) return null;
        long sum = 0;
        foreach (var slot in roster)
        {
            if (_core.PlayerManager.GetPlayer(slot) is not { IsValid: true } player || _ranks.RankOf(player.SteamID) is not { } rank || !Rated(rank))
                return null;
            sum += rank.Points;
        }
        return sum / roster.Count;
    }

    private static string Initial(string? name)
    {
        var clean = Directive.Clean(name).Trim();
        return clean.Length == 0 ? "?" : char.ToUpperInvariant(clean[0]).ToString();
    }
}
