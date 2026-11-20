using CustomHUD.Config;
using CustomHUD.Navigation;
using CustomHUD.Services;
using SwiftlyS2.Shared.Commands;

namespace CustomHUD.Input;

// Numbers nav. Core's own Chat input method (!1 .. !0) is chosen per MENU, so
// it cannot serve a per-PLAYER preference -- a numbers-mode player looking at a
// Buttons menu would have no way to pick a row. These commands work regardless
// of the menu's input method, but only for players in numbers mode.
//
// 1-6 select a row on the current page, 7/8 page, 9 closes.
internal sealed class NumbersNavCommands(
    MenuInputBridge input,
    NavModeResolver navModes,
    SteamIdCache steamIds)
{
    private const int SelectKeys = NavModeResolver.NumberKeys;

    // sw_N only: the printed binds (`!setnav numbers`) also send css_N and ms_N, so aliases for those would run a press once per name.
    [Command("1", permission: "", HelpText = "CustomHUD numbers nav: select option 1.")]
    public void On1(ICommandContext ctx) => Route(ctx, 1);

    [Command("2", permission: "", HelpText = "CustomHUD numbers nav: select option 2.")]
    public void On2(ICommandContext ctx) => Route(ctx, 2);

    [Command("3", permission: "", HelpText = "CustomHUD numbers nav: select option 3.")]
    public void On3(ICommandContext ctx) => Route(ctx, 3);

    [Command("4", permission: "", HelpText = "CustomHUD numbers nav: select option 4.")]
    public void On4(ICommandContext ctx) => Route(ctx, 4);

    [Command("5", permission: "", HelpText = "CustomHUD numbers nav: select option 5.")]
    public void On5(ICommandContext ctx) => Route(ctx, 5);

    [Command("6", permission: "", HelpText = "CustomHUD numbers nav: select option 6.")]
    public void On6(ICommandContext ctx) => Route(ctx, 6);

    [Command("7", permission: "", HelpText = "CustomHUD numbers nav: previous page.")]
    public void On7(ICommandContext ctx) => Route(ctx, 7);

    [Command("8", permission: "", HelpText = "CustomHUD numbers nav: next page.")]
    public void On8(ICommandContext ctx) => Route(ctx, 8);

    [Command("9", permission: "", HelpText = "CustomHUD numbers nav: close menu.")]
    public void On9(ICommandContext ctx) => Route(ctx, 9);

    private void Route(ICommandContext ctx, int slot)
    {
        var player = ctx.Sender;
        if (player is null || player.IsFakeClient) return;
        if (navModes.Resolve(player.PlayerID, steamIds.Get(player.PlayerID)) != NavMode.Numbers) return;

        switch (slot)
        {
            case >= 1 and <= SelectKeys:
                // SelectByNumber takes a 1-based ordinal among selectable rows,
                // which is exactly what the key hint shows.
                input.SelectByNumber(player, slot);
                break;
            case 7:
                input.MovePage(player, -1);
                break;
            case 8:
                input.MovePage(player, +1);
                break;
            case 9:
                input.Close(player);
                break;
        }
    }
}
