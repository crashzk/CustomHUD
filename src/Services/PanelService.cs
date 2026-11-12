using CustomHUD.Config;
using CustomHUD.Contract;
using CustomHUD.Rendering;
using SwiftlyS2.Shared;

namespace CustomHUD.Services;

// Published before Load wires it (SW2 does not order ConfigureSharedInterface
// against Load), so every call is a no-op until Attach.
internal sealed class PanelService : ICustomHudPanels
{
    private volatile Wiring? _wiring;

    private sealed record Wiring(ISwiftlyCore Core, CustomHudConfigProvider Config, SteamIdCache SteamIds, PanelSurface Surface);

    public void Attach(ISwiftlyCore core, CustomHudConfigProvider config, SteamIdCache steamIds, PanelSurface surface)
        => _wiring = new Wiring(core, config, steamIds, surface);

    public void Detach() => _wiring = null;

    public IReadOnlyCollection<string> Profiles
        => _wiring?.Config.Current.PanelNames().ToArray() ?? [];

    public bool Show(
        int playerId,
        string profile,
        IReadOnlyDictionary<string, string> variables,
        IEnumerable<string>? classes = null,
        float? durationSeconds = null)
    {
        if (_wiring is not { } w || string.IsNullOrWhiteSpace(profile)) return false;

        var config = w.Config.Current;
        if (config.ResolvePanel(profile) is not { } panel || string.IsNullOrWhiteSpace(panel.File))
            return false;

        // Copied here: the caller may mutate its collections before the tick runs.
        var vars = new Dictionary<string, string>(variables ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        var cls = classes?.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.Ordinal).ToArray();
        var duration = durationSeconds ?? panel.DurationSeconds;
        var layoutPath = config.ResolvePanelPath(panel);

        w.Core.Scheduler.NextTick(() =>
        {
            if (_wiring is null) return;
            var steamId = w.SteamIds.Get(playerId);
            if (steamId == 0) return;
            w.Surface.Show(playerId, steamId, profile, panel, layoutPath, vars, cls, duration);
        });
        return true;
    }

    public void Hide(int playerId, string profile)
        => OnTick(w => w.Surface.Hide(playerId, profile));

    public void HideAll(int playerId)
        => OnTick(w => w.Surface.HideAll(playerId));

    public void HideEverywhere(string profile)
        => OnTick(w => w.Surface.HideEverywhere(profile));

    private void OnTick(Action<Wiring> action)
    {
        if (_wiring is not { } w) return;
        w.Core.Scheduler.NextTick(() =>
        {
            if (_wiring is not null) action(w);
        });
    }
}
