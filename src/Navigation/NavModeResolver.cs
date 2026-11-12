using System.Collections.Concurrent;
using CustomHUD.Config;
using CustomHUD.Services;

namespace CustomHUD.Navigation;

// Resolves a player's nav mode and HUD scale. Called from the renderer's
// background thread during compose, so it reads only the preference cache and
// config -- never engine state.
internal sealed class NavModeResolver(CustomHudConfigProvider config, PlayerPreferences prefs, CoreMenuSettings coreMenus)
{
    // Numbers mode: 1-6 select, 7/8 page, 9 close -- so a page holds at most six choices.
    public const int NumberKeys = 6;

    // Session-level choice, consulted before the stored preference. Cookies
    // cannot persist a SteamID of 0 (LAN / insecure servers) and load
    // asynchronously, so without this `!setnav` would appear to do nothing
    // until the next map -- or ever, on a dev box.
    private readonly ConcurrentDictionary<int, NavMode> _slotModes = new();
    private readonly ConcurrentDictionary<int, int> _slotScales = new();

    public void SetSlotMode(int slot, NavMode mode) => _slotModes[slot] = mode;
    public void SetSlotScale(int slot, int percent) => _slotScales[slot] = percent;

    public void ClearSlot(int slot)
    {
        _slotModes.TryRemove(slot, out _);
        _slotScales.TryRemove(slot, out _);
    }

    public NavMode Resolve(int slot, ulong steamId)
    {
        if (_slotModes.TryGetValue(slot, out var chosen)) return chosen;
        if (Parse(prefs.GetNavMode(steamId)) is { } stored) return stored;
        return config.Current.StartMode(coreMenus) ?? NavMode.Buttons;
    }

    public int ResolveScale(int slot, ulong steamId, MenuLayoutProfile profile)
    {
        var steps = profile.Classes.ScaleSteps;
        if (steps.Count == 0) return 100;

        int? stored = _slotScales.TryGetValue(slot, out var chosen) ? chosen : prefs.GetScale(steamId);
        // An unknown value cannot be rendered -- the ladder only has classes
        // for the configured steps -- so snap to the nearest one rather than
        // writing a class the stylesheet has never heard of.
        if (stored is null) return steps.Contains(100) ? 100 : steps[0];
        return steps.MinBy(s => Math.Abs(s - stored.Value));
    }

    public static NavMode? Parse(string? value) => value?.ToLowerInvariant() switch
    {
        "wasd" => NavMode.Wasd,
        "buttons" => NavMode.Buttons,
        // Pre-rename spelling. Kept so stored preferences and hand-written
        // configs survive the move to core's "buttons" vocabulary.
        "ctrl_shift" or "ctrlshift" => NavMode.Buttons,
        "numbers" => NavMode.Numbers,
        "mouse" => NavMode.Mouse,
        _ => null,
    };

    // Menus.InputMethod in menus.jsonc: core's "buttons" and "chat", or a mode of this plugin's,
    // which core ignores. A buttons player sees a Chat menu just as core runs it, numbered for !N.
    public static NavMode? FromInputMethod(string? value)
        => string.Equals(value?.Trim(), "chat", StringComparison.OrdinalIgnoreCase) ? NavMode.Buttons : Parse(value?.Trim());

    public static string Name(NavMode mode) => mode switch
    {
        NavMode.Wasd => "wasd",
        NavMode.Buttons => "buttons",
        NavMode.Numbers => "numbers",
        NavMode.Mouse => "mouse",
        _ => "buttons",
    };
}
