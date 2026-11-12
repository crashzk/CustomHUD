using MixScrims.Contract;
using SwiftlyS2.Shared.Menu;
using SwiftlyS2.Shared.Menu.Components;
using SwiftlyS2.Shared.Players;

namespace CustomHUD.Integrations.MixPages;

internal sealed partial class MixScrimsIntegration
{
    // ---- the !mix control panel: the match's road down the page, the switches as tiles beside it

    // MixScrims keys its panel on component ids, which it documents as fixed: these are its tiles, by
    // section, with their glyphs. Its status line (phase, ready count) is a header line, drawn as a chip.
    private static readonly (string Id, string Glyph, string? Section, bool Danger)[] ControlTiles =
    [
        ("start", "play", "control.flow", false),
        ("hold", "pause", null, false),
        ("reset", "restart", null, true),
        ("force-ready", "ready", "control.players", false),
        ("force-unready", "unready", null, false),
        ("captains", "crown", null, false),
        ("lock-joining", "lock", null, false),
        ("kick-spectators", "kick", null, true),
        ("change-map", "map-vote", "control.map", false),
        ("map-pool", "globe", null, false),
    ];

    private static string? ControlRegion(IMenuComponent component)
        => Array.Exists(ControlTiles, tile => tile.Id == component.Id) ? "detail" : null;

    private void DrawControl(PageDraw page)
    {
        ChipStatusLine(page);
        var tiles = page.Regions.Contains("detail");
        var last = -1;
        for (var k = 0; k < page.Components.Count; k++)
        {
            var id = page.Components[k].Id;
            var index = Array.FindIndex(ControlTiles, tile => tile.Id == id);
            if (index < 0) continue;
            var (_, glyph, section, danger) = ControlTiles[index];
            if (tiles && section is not null) page.Add(T(page, section), "at=detail;section", k);
            page.Decorate(k, (tiles ? "at=detail;" : "") + "glyph=" + glyph + (danger ? ";danger" : ""));
            last = k;
        }
        if (!tiles || last < 0) return;

        page.Add(T(page, "control.hint"), "at=detail;group=hint;glyph=info");
        var steps = Steps(page);
        // The road is the main list's only rows; a list too short for all of it leaves it out whole.
        if (page.Room < steps.Count + 1) return;
        page.Add(T(page, "control.road"), "section", 0);
        foreach (var (text, payload) in steps) page.Add(text, payload, 0);
    }

    // MixScrims' status line, the line under the title, as a chip beside it.
    private static void ChipStatusLine(PageDraw page)
    {
        if (page.Menu.GetComponents(MenuRegion.Header).OfType<TitleComponent>().FirstOrDefault()?.Text.Trim() is not { } title) return;
        for (var i = 0; i < page.HeaderLines.Count; i++)
        {
            var line = page.HeaderLines[i].Trim();
            if (line != title && line.Any(char.IsLetterOrDigit)) page.DecorateHeader(i, "chip;glyph=vote");
        }
    }

    // The phases this match goes through, in order, each done, current or still to come. Which ones
    // there are follows MixScrims' settings: no map vote skips the vote, the map change and the second
    // ready-up; no captains skips them and the team pick.
    private List<(string Text, string Payload)> Steps(PageDraw page)
    {
        var at = Position(_api.GetCurrentMatchState());
        var voting = !_api.GetSkipMapVotingEnabled();
        var captains = _api.GetCaptainsEnabled();
        var picking = captains && !_api.GetSkipTeamPickingEnabled();
        var ready = $"{_api.GetEffectiveReadyCount()} / {_api.GetPlayersRequiredToStart()}";

        var steps = new List<(string Text, string Payload)>(8);
        void Step(string key, int position, string? sub)
        {
            var mark = steps.Count + 1;
            var state = position < at ? "group=done;glyph=check" : position == at ? $"group=current;mark={mark}" : $"mark={mark}";
            steps.Add((T(page, "step." + key), state + (sub is { Length: > 0 } ? ";sub=" + Directive.Clean(sub) : "")));
        }

        Step("warmup", 0, at == 0 ? T(page, "step.ready", ready) : null);
        if (voting)
        {
            Step("map_vote", 1, at == 1 ? MapVoteLine(page) : null);
            Step("map_change", 2, null);
            Step("ready_up", 3, at == 3 ? T(page, "step.ready", ready) : null);
        }
        if (captains) CaptainsStep(page, steps, at);
        if (picking) Step("team_pick", 5, at == 5 && _api.GetPickOrder() is { Count: > 0 } order
            ? T(page, "draft.pick_of", Math.Min(order.Count, PicksMade(order, _api.GetPickedCtPlayerSlots(), _api.GetPickedTPlayerSlots()) + 1), order.Count) : null);
        Step("knife_round", 6, at == 6 && _api.GetKnifeRoundWinner() is { } winner && winner is Team.CT or Team.T
            ? T(page, "side.won", TeamName(winner)) : null);
        Step("match", 7, MatchLine(page, at));
        return steps;
    }

    // Captains are set by hand at any point before the pick, so the step is done once both are set and
    // otherwise names the ones there are.
    private void CaptainsStep(PageDraw page, List<(string Text, string Payload)> steps, int at)
    {
        var names = new List<string>(2);
        foreach (var slot in new[] { _api.GetCtCaptainSlot(), _api.GetTCaptainSlot() })
            if (slot is { } s && _core.PlayerManager.GetPlayer(s) is { IsValid: true } player) names.Add(Directive.Clean(player.Name));
        var mark = steps.Count + 1;
        var state = names.Count == 2 || at > 4 ? "group=done;glyph=check" : $"mark={mark}";
        var sub = names.Count > 0 ? string.Join(" · ", names) : "";
        steps.Add((T(page, "step.captains"), state + (sub.Length > 0 ? ";sub=" + sub : "")));
    }

    private string MapVoteLine(PageDraw page)
    {
        var cast = _api.GetMapVoteTallies().Values.Sum();
        var line = T(page, cast == 1 ? "votes.one" : "votes.other", cast);
        return _api.GetMapVoteSecondsRemaining() is > 0 and var seconds ? $"{line} · {Clock(seconds)}" : line;
    }

    // The score once the match is on, a timeout's clock while one runs, and before it the timeouts each team gets.
    private string? MatchLine(PageDraw page, int at)
    {
        if (at < 7)
            return _api.GetTotalTimeoutsPerTeam() is > 0 and var total
                ? T(page, total == 1 ? "step.timeouts.one" : "step.timeouts.other", total) : null;
        var (ct, t) = _api.GetMatchScore();
        var score = T(page, "step.score", TeamTag(page, Team.CT), ct, t, TeamTag(page, Team.T));
        return _api.GetActiveTimeoutRemainingSeconds() is > 0 and var seconds && _api.GetActiveTimeoutTeam() is { } team
            ? $"{score} · {T(page, "step.timeout", TeamTag(page, team), Clock(seconds))}"
            : score;
    }

    // Where a phase sits on the road. MixScrims comes back to MapChosen once the new map is up, for the
    // second ready-up; that is the only MapChosen a player lingers in, so it counts as the ready-up.
    private static int Position(MatchState state) => state switch
    {
        MatchState.MapVoting => 1,
        MatchState.MapLoading => 2,
        MatchState.MapChosen => 3,
        MatchState.PickingTeam => 5,
        MatchState.KnifeRound or MatchState.PickingStartingSide => 6,
        MatchState.Match or MatchState.Timeout => 7,
        MatchState.Ended => 8,
        _ => 0,
    };
}
