using CustomHUD.Integrations.RankPages;
using SwiftlyS2.Shared.Menu;

namespace CustomHUD.Integrations;

// One provider plugin's pages, drawn from that plugin's public API instead of anything it sends
// the renderer. Only SwiftlyS2 types appear here: an implementation names its provider's contract
// and is created behind IntegrationHost's guarded factories, so a server without the provider
// never loads that contract. Game thread only.
internal interface IPageIntegration
{
    string Provider { get; }

    bool Owns(IMenu menu);

    // The region (a podium's banner) a row is drawn in rather than the main list, so paging
    // leaves it out of the main list's count where the layout has that region.
    string? RegionOf(IMenu menu, IMenuComponent component);

    // Rows Draw adds to the main list of every page of this menu (a heading), kept free when the
    // menu is paged so they are never cut. Rows added only where they fit check PageDraw.Room instead.
    int RowsAdded(IMenu menu) => 0;

    // RowsAdded for the layout drawing the menu, for a heading that only some regions' views carry.
    int RowsAdded(IMenu menu, IReadOnlySet<string> regions) => RowsAdded(menu);

    void Draw(PageDraw page);
}

// The rank chip Ranks' pages carry, offered to its modules' integrations. An interface so a module
// integration can hold one without naming Ranks.Contract.
internal interface IRankChips
{
    void AddChip(PageDraw page);

    // Another player's rank as their scoreboard draws it, with the totals behind their K/D and
    // headshot rate; null until Ranks holds their snapshot.
    PlayerRank? RankOf(ulong steamId);
}

internal sealed record PlayerRank(RankArt Art, long Points, long Kills, long Deaths, long Headshots)
{
    public string KillDeath => Directive.Decimal(Deaths == 0 ? Kills : Math.Round((double)Kills / Deaths, 2));

    public double HeadshotRate => Directive.Percent(Headshots, Kills);
}
