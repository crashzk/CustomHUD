using CustomHUD.Config;
using MixScrims.Contract;
using SwiftlyS2.Shared.Menu;
using SwiftlyS2.Shared.Players;

namespace CustomHUD.Integrations.MixPages;

internal sealed partial class MixScrimsIntegration
{
    private const string BoardView = "maps-board";
    private const int MaxPips = 20;

    // ---- the map vote: MapChooser's board, each map's share of the votes cast

    private void DrawMapVote(PageDraw page)
    {
        page.Hint(T(page, "hint.map_vote"));
        // maps-board is sized to sit beside MapChooser's sidebar, which no MixScrims page has.
        if (page.View == BoardView) page.AddHeader(string.Empty, "variant=wide");

        // Tallies are keyed by a map's display name, MapPoolEntry.Name. The vote has no electorate, so
        // its turnout is the votes cast alone.
        var tallies = _api.GetMapVoteTallies();
        var total = tallies.Values.Sum();
        if (_api.GetMapVoteSecondsRemaining() is > 0 and var seconds)
            page.AddHeader($"{Clock(seconds)}\t{T(page, "chip.vote_ends")}", "chip=warn;glyph=stopwatch");
        page.AddHeader($"{total}\t{T(page, "chip.voted")}", "chip;glyph=check");

        var mine = _api.GetPlayerMapVote(page.Player.SteamID);
        for (var k = 0; k < page.Components.Count; k++)
        {
            if (page.Components[k] is not MenuComponentBase { Tag: MapPoolEntry map }) continue;
            var count = tallies.GetValueOrDefault(map.Name);
            var share = total > 0 ? (int)Math.Round(100.0 * count / total, MidpointRounding.AwayFromZero) : 0;
            var parts = new List<string>(8) { $"text=\t{share};vunit=%;bar={share}", Badge(page, map), "note=" + Votes(page, count) };
            if (Art(map) is { } art) parts.Add(art);
            if (string.Equals(map.Name, mine, StringComparison.Ordinal))
                parts.AddRange(["chosen", "tag=" + T(page, "your_vote"), "tagtone=accent"]);
            page.Decorate(k, string.Join(';', parts));
        }
    }

    // A stock map shows its screenshot and logo, any other its id's prefix (aim_redline: AIM) over stripes in
    // that prefix's tone. A workshop id is a number, so its prefix comes from the display name when that is
    // shaped like a map's.
    private static string? Art(MapPoolEntry map)
    {
        var id = map.Id.Trim().ToLowerInvariant();
        if (LockerProfiles.StockMaps.Contains(id)) return "image=map/" + id;
        var prefix = Prefix(id) ?? Prefix(map.Name.Trim().ToLowerInvariant());
        if (prefix is not null) return $"mark={prefix.ToUpperInvariant()};tone={prefix}";
        return map.IsWorkshop ? "mark=WS" : null;
    }

    private static string? Prefix(string name)
    {
        var cut = name.IndexOf('_');
        if (cut <= 0 || cut == name.Length - 1) return null;
        var prefix = name[..Math.Min(cut, 6)];
        return Directive.IsToken(prefix) ? prefix : null;
    }

    private static string Badge(PageDraw page, MapPoolEntry map)
        => map.IsWorkshop ? $"badge={T(page, "kind.workshop")};badgetone=info"
            : LockerProfiles.StockMaps.Contains(map.Id.Trim().ToLowerInvariant()) ? "badge=" + T(page, "kind.stock")
            : "badge=" + T(page, "kind.custom");

    private static string Votes(PageDraw page, int count) => T(page, count == 1 ? "votes.one" : "votes.other", count);

    // ---- the panel's Change map and Map pool: every configured map as MapChooser's pool card

    // Rows carry their MapPoolEntry. Map pool's text is "name (id)"; the card shows the name and puts
    // the id under it, as Change map's does.
    private static void DrawMapPool(PageDraw page)
    {
        for (var k = 0; k < page.Components.Count; k++)
        {
            if (page.Components[k] is not MenuComponentBase { Tag: MapPoolEntry map }) continue;
            var parts = new List<string>(4) { Badge(page, map) };
            if (!string.Equals(map.Name, map.Id, StringComparison.Ordinal)) parts.Add("sub=" + Directive.Clean(map.Id));
            if (Art(map) is { } art) parts.Add(art);
            page.Draw(k, Directive.Clean(map.Name), string.Join(';', parts));
        }
    }

    // ---- the side choice: Stay and Switch as the sides each team starts on

    // Rows carry `stay` as Tag. Stay keeps the knife-round winners on their side; the view draws it left
    // whatever the rows' order (group=). Without captains the winning team votes, and each side shows its
    // votes; MixScrims does not say who voted which way, nor how the player did.
    private void DrawSides(PageDraw page)
    {
        if (_api.GetKnifeRoundWinner() is not { } winner || winner is not (Team.CT or Team.T)) return;
        page.Hint(T(page, "hint.sides"));

        var vote = !_api.GetCaptainsEnabled();
        var (switches, stays, eligible) = _api.GetStartingSideVoteTally();
        var counted = vote && eligible > 0;
        if (_api.GetStartingSideSecondsRemaining() is > 0 and var seconds)
            page.AddHeader($"{Clock(seconds)}\t{T(page, "chip.to_choose")}", "chip=warn;glyph=timer");
        if (counted) page.AddHeader($"{switches + stays} / {eligible}\t{T(page, "chip.voted")}", "chip;glyph=check");

        var who = counted ? T(page, "side.team_votes", Math.Max(0, eligible - switches - stays))
            : !vote && _api.GetStartingSidePicker() == page.Player.SteamID ? T(page, "side.you_choose")
            : null;
        // The list has no banner, so there the line goes under the two answers.
        var at = page.Regions.Contains("banner") ? "banner" : "foot";
        page.Add(T(page, "side.won", TeamName(winner)),
            $"at={at};glyph=knives;tag={TeamTag(page, winner)};tagtone={Tone(winner)}" + (who is null ? "" : ";note=" + who), 0);

        for (var k = 0; k < page.Components.Count; k++)
        {
            if (page.Components[k] is not MenuComponentBase { Tag: bool stay }) continue;
            var side = stay ? winner : Other(winner);
            var parts = new List<string>(6)
            {
                "group=" + (stay ? "stay" : "switch"),
                "glyph=" + Emblem(side),
                "tone=" + Tone(side),
                "sub=" + T(page, side == Team.CT ? "side.start_ct" : "side.start_t"),
                "note=" + T(page, "side.other", TeamName(Other(winner)), TeamTag(page, Other(side))),
            };
            if (counted)
            {
                var votes = stay ? stays : switches;
                parts.Add($"text=\t{T(page, "side.votes", votes, eligible)};bar={Directive.Bar(Directive.Percent(votes, eligible))}");
            }
            page.Decorate(k, string.Join(';', parts));
        }
    }

    // ---- the ballots: a timeout, a surrender or a vote kick, Yes and No under who called it

    // Rows carry the answer as Tag, true for Yes; a kick's menu carries its team, None for a server-wide
    // kick. Voting closes the ballot, so it never shows the player's own vote.
    private void DrawBallot(PageDraw page)
    {
        var id = page.Menu.Id;
        var kick = id == VoteKick;
        var team = kick ? page.Menu.Tag as Team? : id == TimeoutVote ? _api.GetActiveTimeoutVoteTeam() : _api.GetActiveSurrenderVoteTeam();
        if (team is not { } side) return;
        var serverWide = kick && side == Team.None;

        var seconds = id switch
        {
            TimeoutVote => _api.GetTimeoutVoteSecondsRemaining(),
            SurrenderVote => _api.GetSurrenderVoteSecondsRemaining(),
            _ => _api.GetVoteKickSecondsRemaining(side),
        };
        if (!serverWide) page.AddHeader($"{TeamTag(page, side)}\t{T(page, "chip.team_vote")}", "chip;glyph=" + Emblem(side));
        if (seconds > 0) page.AddHeader($"{Clock(seconds)}\t{T(page, "chip.left")}", "chip=warn;glyph=stopwatch");

        for (var k = 0; k < page.Components.Count; k++)
        {
            if (page.Components[k] is not MenuComponentBase { Tag: bool yes }) continue;
            // A surrender's Yes gives the match away.
            page.Decorate(k, yes ? "glyph=check;tone=good" + (id == SurrenderVote ? ";danger" : "") : "glyph=cancel;tone=bad");
        }

        // The list has no banner; its title is the question, which names a kick's target.
        if (page.Regions.Contains("banner"))
        {
            Caller(page, id, side);
            if (kick) Target(page, side, serverWide);
        }

        var (ayes, cast, eligible) = id switch
        {
            TimeoutVote => _api.GetTimeoutVoteTally(),
            SurrenderVote => _api.GetSurrenderVoteTally(),
            _ => side switch
            {
                Team.CT => _api.GetVoteKickTallyCt(),
                Team.T => _api.GetVoteKickTallyT(),
                _ => _api.GetVoteKickTallyServerWide(),
            },
        };
        if (eligible <= 0) return;

        // A team kick's Eligible is its electorate, a pip per voter; every other vote's is the Yes votes it needs.
        if (kick && !serverWide)
        {
            page.Add(T(page, "ballot.voted", cast, eligible), "at=foot;" + Pips(ayes, cast - ayes, eligible));
            page.Add(T(page, "ballot.rule.kick"), "at=foot;glyph=info");
            return;
        }
        page.Add(T(page, "ballot.yes", ayes, eligible), "at=foot;" + Pips(ayes, 0, eligible));
        var left = id == TimeoutVote ? "\t" + TimeoutsLeft(page, side) : "";
        page.Add(T(page, "ballot.rule.yes", eligible) + left, "at=foot;glyph=info");
    }

    // "Alex called it · 60 seconds at the next round start", while the caller is still here.
    private void Caller(PageDraw page, string id, Team side)
    {
        var caller = id switch
        {
            TimeoutVote => _api.GetTimeoutVoteCaller(),
            SurrenderVote => _api.GetSurrenderVoteCaller(),
            _ => _api.GetVoteKickCaller(side),
        };
        if (caller is not { } steamId || Online(steamId) is not { } player) return;

        var line = id switch
        {
            TimeoutVote => T(page, "ballot.timeout", _api.GetTimeoutDurationSeconds()),
            SurrenderVote => T(page, "ballot.surrender"),
            _ => "",
        };
        var sub = line.Length > 0 ? $"{T(page, "ballot.called")} · {line}" : T(page, "ballot.called");
        page.Add(Directive.Clean(player.Name), "at=banner;avatar;sub=" + sub, 0);
    }

    // Whom a kick is about, as a card: K/D and side from the game, the rank from Ranks when it has them.
    private void Target(PageDraw page, Team side, bool serverWide)
    {
        var target = side switch
        {
            Team.CT => _api.GetVoteKickTargetCt(),
            Team.T => _api.GetVoteKickTargetT(),
            _ => _api.GetVoteKickTargetServerWide(),
        };
        if (target is not { } steamId || Online(steamId) is not { } player) return;

        var (team, kills, deaths) = Facts(player);
        var kd = T(page, "ballot.target", Directive.Decimal(deaths == 0 ? kills : (double)kills / deaths));
        var where = !serverWide ? T(page, "ballot.target_team") : team is Team.CT or Team.T ? TeamTag(page, team) : "";
        var payload = $"at=banner;group=target;avatar;sub={(where.Length > 0 ? $"{kd} · {where}" : kd)}";
        if (_ranks?.RankOf(steamId) is { } rank) payload += ";" + rank.Art.Payload;
        page.Add(Directive.Clean(player.Name), payload, 0);
    }

    private static (Team Team, int Kills, int Deaths) Facts(IPlayer player)
    {
        var controller = player.Controller;
        if (controller is not { IsValid: true }) return (Team.None, 0, 0);
        var tracking = controller.ActionTrackingServices;
        var stats = tracking is { IsValid: true } ? tracking.MatchStats : null;
        return ((Team)controller.TeamNum, stats?.Kills ?? 0, stats?.Deaths ?? 0);
    }

    private string TimeoutsLeft(PageDraw page, Team side)
    {
        var left = side == Team.CT ? _api.GetRemainingTimeoutsCt() : _api.GetRemainingTimeoutsT();
        return T(page, left == 1 ? "ballot.timeouts.one" : "ballot.timeouts.other", left);
    }

    // One pip a vote up to what the layout draws; past that each pip is a share.
    private static string Pips(int yes, int no, int of)
    {
        if (of > MaxPips)
        {
            (yes, no) = ((int)Math.Round((double)MaxPips * yes / of), (int)Math.Round((double)MaxPips * no / of));
            of = MaxPips;
        }
        return $"pips={Math.Clamp(yes, 0, of)}/{of}" + (no > 0 ? $";nays={no}" : "");
    }
}
