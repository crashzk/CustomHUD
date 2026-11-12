using System.Text.Json.Serialization;

namespace CustomHUD.Config;

// The layout each kind of native CS2 HUD text draws in, by name ("hud_text", "hud_alert", their _v2, or
// the operator's MessageLayouts), so CustomHUD draws it instead of the game's own HUD. "game", the
// default for every kind, leaves it to the game: silently swallowing another plugin's messages is
// never a safe default, and a mis-set layout would make them disappear with nothing to replace them.
//
// WHICH MESSAGES THIS REACHES, and why it is not obvious:
//
// SwiftlyS2's IPlayer.SendCenter / SendAlert / SendChat / SendMessage all end
// up in CPlayer::SendMsg, which emits a CUserMessageTextMsg whose `dest` is the
// MessageType value (Notify 1, Console 2, Chat 3, Center 4, ChatEOT 5,
// Alert 6). So one hook covers every one of them.
//
// But SendMsg sets `bypassPostEventAbstractHook` around its send, and
// PostEventAbstractHook -- the hook behind [ServerNetMessageHandler] -- checks
// that flag. So the ordinary server hook sees NONE of SwiftlyS2's own HUD text.
// The capture therefore uses [ServerNetMessageInternalHandler], which hooks
// CServerSideClient::SendNetMessage further down the stack where no bypass
// exists, and which hands us the target player as well.
//
// SendCenterHTML (MessageType 100) is NOT a TextMsg at all: it is buffered and
// re-sent every tick as the `show_survival_respawn_status` game event inside
// CMsgSource1LegacyGameEvent. It is deliberately out of scope here -- see
// docs/PublicPort.md.
public sealed class MessageChoices
{
    // dest = 4. The one most servers actually want.
    public string Center { get; set; } = BuiltInLayouts.Game;
    // dest = 6.
    public string Alert { get; set; } = BuiltInLayouts.Game;
    // dest = 1.
    public string Notify { get; set; } = BuiltInLayouts.Game;
    // dest = 3 and 5, kept out of config.jsonc. Capturing these replaces CHAT, which is almost
    // never wanted -- a chat line the player can scroll back to is not a HUD element.
    [JsonIgnore]
    public string Chat { get; set; } = BuiltInLayouts.Game;
    [JsonIgnore]
    public string ChatEndOfRound { get; set; } = BuiltInLayouts.Game;
    // dest = 2. Console output; capturing it is almost certainly a mistake,
    // but the knob exists rather than being silently impossible.
    [JsonIgnore]
    public string Console { get; set; } = BuiltInLayouts.Game;

    public string? For(NativeTextKind kind) => (kind switch
    {
        NativeTextKind.Center => Center,
        NativeTextKind.Alert => Alert,
        NativeTextKind.Notify => Notify,
        NativeTextKind.Chat => Chat,
        NativeTextKind.ChatEndOfRound => ChatEndOfRound,
        NativeTextKind.Console => Console,
        _ => null,
    })?.Trim() is { Length: > 0 } name ? name : null;
}

public sealed class NativeTextProfile
{
    // Compiled layout, resolved against CustomHudConfig.LayoutRoot exactly like
    // a menu profile's File.
    public string File { get; set; } = "hud_text.vxml_c";

    public string ShellPanel { get; set; } = "shell";
    public string HiddenClass { get; set; } = "hidden";
    public string TextPanel { get; set; } = "text";
    public string TextVariable { get; set; } = "text";

    // Class marking which kind produced the text, so one layout can style a
    // center message and an alert differently. `{0}` is the lowercase kind.
    public string KindClass { get; set; } = "kind-{0}";

    // How long the text stays up. The native center message is ~5s, so that is
    // the default; a value <= 0 leaves it up until replaced.
    public float DurationSeconds { get; set; } = 5f;

    // Drop the message entirely instead of forwarding it to the native HUD.
    // False renders in CustomHUD *and* lets the native one through, which is
    // mainly useful while authoring a layout.
    public bool Suppress { get; set; } = true;

    // The built-in layouts, by the name Messages picks.
    public static Dictionary<string, NativeTextProfile> Defaults() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["hud_text"] = new(),
        ["hud_alert"] = new() { File = "hud_alert.vxml_c", DurationSeconds = 4f },
        ["hud_text_v2"] = new() { File = "hud_text_v2.vxml_c" },
        ["hud_alert_v2"] = new() { File = "hud_alert_v2.vxml_c", DurationSeconds = 4f },
    };
}

// Mirrors SwiftlyS2's MessageType, which is what lands in
// CUserMessageTextMsg.Dest. Values are the wire values, not an invention.
public enum NativeTextKind
{
    Notify = 1,
    Console = 2,
    Chat = 3,
    Center = 4,
    ChatEndOfRound = 5,
    Alert = 6,
}
