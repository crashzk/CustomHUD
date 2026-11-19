using CustomHUD.Config;
using CustomHUD.Navigation;
using CustomHUD.Rendering;
using CustomHUD.Services;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Translation;

namespace CustomHUD.Commands;

// Player-facing preference commands. Both take effect on the next frame: the
// renderer reads nav mode and scale during compose, so a change only needs the
// open session invalidated.
internal sealed class SettingsCommands(
    CustomHudConfigProvider config,
    PlayerPreferences prefs,
    NavModeResolver navModes,
    PanoramaMenuRenderer renderer,
    HudTranslations strings)
{
    // css_N and ms_N make the same line work on CounterStrikeSharp and ModSharp servers; CS2 prints at most 511 bytes of it (313 now).
    private static readonly string NumberBinds = string.Join("; ",
        Enumerable.Range(1, 9).Select(n => $"bind {n} \"slot{n}; css_{n}; ms_{n}; sw_{n}\""));

    [Command("setnav", permission: "", HelpText = "Choose menu navigation: wasd | buttons | numbers | mouse.")]
    public void OnSetNav(ICommandContext ctx)
    {
        var player = ctx.Sender;
        if (player is null || player.IsFakeClient) return;
        var localizer = strings.Refresh(player);

        var requested = ctx.Args.Length > 0 ? ctx.Args[0] : null;
        if (NavModeResolver.Parse(requested) is not { } mode)
        {
            var current = NavModeResolver.Name(navModes.Resolve(player.PlayerID, player.SteamID));
            Reply(ctx, localizer, "customhud.nav.usage", current);
            return;
        }

        // PlayerPreferences raises no change event for SteamID 0, so the
        // invalidate is issued here rather than left to that event.
        navModes.SetSlotMode(player.PlayerID, mode);
        prefs.SetNavMode(player.SteamID, NavModeResolver.Name(mode));
        renderer.InvalidateSlot(player.PlayerID);
        Reply(ctx, localizer, "customhud.nav.set", NavModeResolver.Name(mode));
        if (mode != NavMode.Numbers) return;

        player.SendConsole($"{localizer["customhud.prefix"]} {localizer["customhud.nav.binds_header"]}");
        player.SendConsole(NumberBinds);
        Reply(ctx, localizer, "customhud.nav.binds");
    }

    [Command("hudscale", permission: "", HelpText = "Set CustomHUD menu scale, e.g. !hudscale 110.")]
    public void OnHudScale(ICommandContext ctx)
    {
        var player = ctx.Sender;
        if (player is null || player.IsFakeClient) return;
        var localizer = strings.Refresh(player);

        var steps = config.Current.ScaleSteps();
        if (ctx.Args.Length == 0 || !int.TryParse(ctx.Args[0], out var requested))
        {
            Reply(ctx, localizer, "customhud.scale.usage", string.Join(" | ", steps));
            return;
        }

        // The ladder only has CSS classes for the configured steps, so an
        // arbitrary number would write a class the stylesheet never defined.
        var snapped = steps.Count == 0 ? 100 : steps.MinBy(s => Math.Abs(s - requested));
        navModes.SetSlotScale(player.PlayerID, snapped);
        prefs.SetScale(player.SteamID, snapped);
        renderer.InvalidateSlot(player.PlayerID);
        Reply(ctx, localizer, "customhud.scale.set", snapped);
    }

    private static void Reply(ICommandContext ctx, ILocalizer localizer, string key, params object[] args)
        => ctx.Reply($"{localizer["customhud.prefix"]} {localizer[key, args]}");
}
