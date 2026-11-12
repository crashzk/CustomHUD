using MixScrims.Contract;
using SwiftlyS2.Shared.Menu;
using SwiftlyS2.Shared.Players;

namespace CustomHUD.Integrations.MixPages;

internal sealed partial class MixScrimsIntegration
{
    // ---- the captain picker: both captain slots over the players who could fill them

    // MixScrims' page fills the side its Tag names. Its first two rows are the slots, tagged with their
    // Team; every other row is a candidate, tagged with their slot. Clicking a slot reopens the page for
    // that side, which is what the banner card's pointer says.
    private void DrawCaptains(PageDraw page)
    {
        var filling = page.Menu.Tag as Team?;
        var assigned = (_api.GetCtCaptainSlot() is not null ? 1 : 0) + (_api.GetTCaptainSlot() is not null ? 1 : 0);
        page.AddHeader($"{assigned} / 2\t{T(page, "chip.captains")}", "chip=gold;glyph=crown");

        // Readiness counts only while players ready up; in a later phase every row would read "Not ready".
        var readying = _api.GetCurrentMatchState() is MatchState.Warmup or MatchState.MapChosen;
        var banner = page.Regions.Contains("banner");
        var headed = false;
        for (var k = 0; k < page.Components.Count; k++)
        {
            switch ((page.Components[k] as MenuComponentBase)?.Tag)
            {
                case Team side when side is Team.CT or Team.T:
                    DrawCaptainSlot(page, k, side, side == filling, banner);
                    break;
                case int slot when _core.PlayerManager.GetPlayer(slot) is { IsValid: true } player:
                    if (!headed && banner)
                    {
                        page.Add(T(page, _ranks is null ? "captains.head_kd" : "captains.head"), "head", k);
                        headed = true;
                    }
                    DrawCandidate(page, k, player, filling, readying);
                    break;
            }
        }
    }

    // A slot as a card: who holds it, or the instruction to click a player while it is the side being
    // filled. The list has no banner, so there the row keeps MixScrims' own "CT captain: name" text.
    private void DrawCaptainSlot(PageDraw page, int k, Team side, bool filling, bool banner)
    {
        var parts = new List<string>(8) { "glyph=" + Emblem(side), "tone=" + Tone(side) };
        if (!banner)
        {
            if (filling) parts.Add("chosen");
            page.Decorate(k, string.Join(';', parts));
            return;
        }

        parts.Insert(0, "at=banner");
        parts.Add("type=" + T(page, side == Team.CT ? "captains.ct" : "captains.t"));
        var holder = (side == Team.CT ? _api.GetCtCaptainSlot() : _api.GetTCaptainSlot()) is { } slot
            ? _core.PlayerManager.GetPlayer(slot)
            : null;
        if (filling) parts.AddRange(["tag=" + T(page, "captains.picking"), "tagtone=accent", "chosen"]);
        if (holder is { IsValid: true })
        {
            parts.Add("avatar");
            if (holder.SteamID == page.Player.SteamID && holder.SteamID != 0) parts.Add("unit=" + T(page, "you"));
            page.Draw(k, Directive.Clean(holder.Name), string.Join(';', parts));
            return;
        }
        page.Draw(k, T(page, filling ? "captains.click" : "captains.empty"), string.Join(';', parts));
    }

    // "name  K/D" with the rating badge and headshot rate beside it: Ranks' figures where it holds the
    // player's snapshot, this map's kills and deaths otherwise.
    private void DrawCandidate(PageDraw page, int k, IPlayer player, Team? filling, bool readying)
    {
        var (kd, rest) = Figures(player);
        var parts = new List<string>(8) { "avatar", rest };
        if (filling is Team.CT or Team.T)
            parts.AddRange(["tag2=" + T(page, filling == Team.CT ? "captains.make_ct" : "captains.make_t"), "tag2tone=accent"]);
        if (player.SteamID == page.Player.SteamID && player.SteamID != 0) parts.Add("unit=" + T(page, "you"));
        if (readying && !player.IsFakeClient && !_api.IsPlayerReady(player.SteamID))
            parts.AddRange(["tag=" + T(page, "not_ready"), "tagtone=dark", "tone=faint"]);
        page.Draw(k, $"{Directive.Clean(player.Name)}\t{kd}", string.Join(';', parts.Where(p => p.Length > 0)));
    }

    // A player's K/D as text, and the payload that carries their rating badge and headshot rate.
    private (string Kd, string Payload) Figures(IPlayer player)
    {
        if (_ranks?.RankOf(player.SteamID) is { } rank)
            return (rank.KillDeath, Rating(rank) + $"note={Directive.Decimal(Math.Round(rank.HeadshotRate))}%");
        var (_, kills, deaths) = Facts(player);
        return (Directive.Decimal(deaths == 0 ? kills : Math.Round((double)kills / deaths, 2)), "");
    }

    // The CS Rating badge the views draw in their rating column, "rating=N;tone=csrN;", or nothing: a
    // ladder that is not in Premier mode names a tier image, which these rows have no place for.
    private static string Rating(PlayerRank rank)
        => rank.Art.Directive.StartsWith("rating=", StringComparison.Ordinal) ? rank.Art.Payload + ";" : "";

    private static bool Rated(PlayerRank rank) => rank.Art.Directive.StartsWith("rating=", StringComparison.Ordinal);
}
