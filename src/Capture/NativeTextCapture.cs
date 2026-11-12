using CustomHUD.Config;
using CustomHUD.Rendering;
using CustomHUD.Services;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.ProtobufDefinitions;

namespace CustomHUD.Capture;

// Intercepts native CS2 HUD text and draws it through CustomHUD instead.
//
// THE HOOK CHOICE IS LOAD-BEARING. CPlayer::SendMsg -- which every
// IPlayer.SendCenter / SendAlert / SendChat / SendMessage call reaches --
// sets `bypassPostEventAbstractHook` around its send, and the hook behind
// [ServerNetMessageHandler] tests that flag. So the ordinary server hook sees
// none of SwiftlyS2's own HUD text, which is exactly the traffic worth
// capturing.
//
// HookServerMessageInternal hooks CServerSideClient::SendNetMessage instead --
// further down, no bypass, and it supplies the target player, which
// PostEventAbstract only carries as a recipient mask.
internal sealed class NativeTextCapture(
    ISwiftlyCore core,
    CustomHudConfigProvider config,
    SteamIdCache steamIds,
    HudSurface surface,
    ILogger logger)
{
    private Guid? _hook;
    private bool _stopped;

    // GAME THREAD ONLY. Run at load and after every config reload. Installed only while a kind is
    // captured: an installed hook sits on the path of every outbound message for every player.
    public void Sync()
    {
        if (_stopped) return;
        var wanted = config.Current.CapturesMessages();
        if (wanted && _hook is null)
            _hook = core.NetMessage.HookServerMessageInternal<CUserMessageTextMsg>(OnTextMsg);
        else if (!wanted && _hook is { } guid)
        {
            core.NetMessage.Unhook(guid);
            _hook = null;
        }
    }

    public void Unsubscribe()
    {
        // A Sync queued by a reload must not hook again after unload.
        _stopped = true;
        if (_hook is not { } guid) return;
        core.NetMessage.Unhook(guid);
        _hook = null;
    }

    private HookResult OnTextMsg(CUserMessageTextMsg msg, int playerId)
    {
        try
        {
            var cfg = config.Current;
            var kind = (NativeTextKind)msg.Dest;
            if (!Enum.IsDefined(kind)) return HookResult.Continue;

            if (cfg.ResolveMessage(kind) is not { } profile) return HookResult.Continue;

            // The message wrapper is temporary and must not outlive this call,
            // so the text is copied before anything is deferred.
            if (msg.Param.Count == 0) return HookResult.Continue;
            var text = msg.Param[0];
            if (string.IsNullOrEmpty(text)) return HookResult.Continue;

            var steamId = steamIds.Get(playerId);
            var layoutPath = cfg.ResolveMessagePath(profile);

            // This runs on the engine's send path. Entity writes belong on the
            // game thread, so the render is deferred -- the string is already
            // copied above, so nothing touches the wrapper afterwards.
            core.Scheduler.NextTick(() => surface.Show(playerId, steamId, profile, layoutPath, kind, text));

            if (cfg.DetailedLogging)
                logger.LogInformation(
                    "[CustomHUD] captured native text slot={Slot} kind={Kind} suppress={Suppress}",
                    playerId, kind, profile.Suppress);

            // Continue renders in CustomHUD *and* leaves the native HUD showing
            // it, which is only useful while authoring a layout.
            return profile.Suppress ? HookResult.Stop : HookResult.Continue;
        }
        catch (Exception ex)
        {
            // Never let a capture failure swallow somebody's message: on any
            // error the native HUD still gets it.
            logger.LogError(ex, "[CustomHUD] Native text capture threw; passing the message through.");
            return HookResult.Continue;
        }
    }
}
