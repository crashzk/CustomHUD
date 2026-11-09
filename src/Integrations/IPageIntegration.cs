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

    void Draw(PageDraw page);
}

// The rank chip Ranks' pages carry, offered to its modules' integrations. An interface so a module
// integration can hold one without naming Ranks.Contract.
internal interface IRankChips
{
    void AddChip(PageDraw page);
}
