using CustomHUD.Config;
using CustomHUD.Rendering;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Translation;

namespace CustomHUD.Commands;

// Sends through IPlayer.SendCenter / SendAlert, never HudSurface, so the capture hook sees what any plugin sends.
// custom_hud is the permission the private CustomHUD's ch_test used.
internal sealed class TestCommands(CustomHudConfigProvider config, HudTranslations strings)
{
    [Command("ch_test", permission: "custom_hud", HelpText = "Send yourself a sample native HUD message: ch_test center | alert [text].")]
    public void OnTest(ICommandContext ctx)
    {
        var player = ctx.Sender;
        if (player is null || player.IsFakeClient) return;
        var localizer = strings.Refresh(player);

        NativeTextKind? requested = ctx.Args.FirstOrDefault()?.ToLowerInvariant() switch
        {
            "center" => NativeTextKind.Center,
            "alert" => NativeTextKind.Alert,
            _ => null,
        };
        if (requested is not { } kind)
        {
            Reply(ctx, localizer, localizer["customhud.test.usage"]);
            return;
        }

        var own = string.Join(' ', ctx.Args[1..]);
        var text = string.IsNullOrWhiteSpace(own)
            ? localizer[kind == NativeTextKind.Center ? "customhud.test.center" : "customhud.test.alert"]
            : own;

        var cfg = config.Current;
        var name = cfg.Messages.For(kind) ?? "";
        var drawnBy = cfg.ResolveMessage(kind) is not null ? localizer["customhud.test.layout", kind, name]
            : name.Equals(BuiltInLayouts.Game, StringComparison.OrdinalIgnoreCase) ? localizer["customhud.test.game", kind]
            : localizer["customhud.test.unknown", kind, name];
        // Reply first: with chat captured too, the later capture takes the player's one HudSurface.
        Reply(ctx, localizer, drawnBy);

        if (kind == NativeTextKind.Center) player.SendCenter(text);
        else player.SendAlert(text);
    }

    private static void Reply(ICommandContext ctx, ILocalizer localizer, string line)
        => ctx.Reply($"{localizer["customhud.prefix"]} {line}");
}
