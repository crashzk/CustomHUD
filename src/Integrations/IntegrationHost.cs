using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using CustomHUD.Config;
using CustomHUD.Integrations.MapPages;
using CustomHUD.Integrations.RankPages;
using CustomHUD.Integrations.ShopPages;
using CustomHUD.Integrations.SkinPages;
using CustomHUD.Integrations.VipPages;
using CustomHUD.Rendering;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Menu;

namespace CustomHUD.Integrations;

// The provider plugins CustomHUD draws from their own APIs. Each is optional and bound like
// Cookies: by shared-interface key first, then through a factory that is the only code naming the
// provider's contract, so a server without that provider never loads it. A provider whose
// contract is older than this build is left plain rather than half-drawn.
internal sealed class IntegrationHost(
    ISwiftlyCore core, CustomHudConfigProvider config, HudTranslations strings, ILogger<IntegrationHost> logger) : IDisposable
{
    private readonly Lock _gate = new();
    private readonly ConditionalWeakTable<IMenuSession, ConcurrentDictionary<string, Task>> _loads = new();
    private readonly ConcurrentDictionary<string, byte> _reported = new();
    private volatile IPageIntegration[] _active = [];

    public Openers Openers { get; } = new();

    public string RendererId => config.Current.RendererId;

    // CustomHUD's own string in a player's language, for a menu an integration builds itself.
    public string Text(int slot, string key)
    {
        var value = strings.For(slot)[key];
        return value.EndsWith(key, StringComparison.Ordinal) ? string.Empty : value;
    }

    // Re-run on every shared-interface rebuild: a provider that unloaded takes its integration with it.
    public void Bind(IInterfaceManager interfaces)
    {
        lock (_gate)
        {
            var ranks = Create("Ranks", "Ranks.Api.v1", interfaces, RanksIntegration.TryCreate);
            var chips = ranks as IRankChips;
            IPageIntegration?[] next =
            [
                ranks,
                Create("Ranks_WeaponStats", "Ranks.WeaponStats.Api.v1", interfaces, i => WeaponStatsIntegration.TryCreate(i, chips)),
                Create("Ranks_CustomCombos", "Ranks.CustomCombos.Api.v1", interfaces, i => CombosIntegration.TryCreate(i, chips)),
                Create("Ranks_Prestige", "Ranks.Prestige.Api.v1", interfaces, i => PrestigeIntegration.TryCreate(i, chips)),
                Create("WeaponSkins", "WeaponSkins.API", interfaces, WeaponSkinsIntegration.TryCreate),
                Create("ShopCore", "ShopCore.API.v2", interfaces, ShopIntegration.TryCreate),
                Create("MapChooser", "MapChooser.Api.v1", interfaces, i => MapChooserIntegration.TryCreate(i, core, this)),
                Create("VIPCore", "VIPCore.Api.v1", interfaces, VipIntegration.TryCreate),
            ];
            Replace([.. next.OfType<IPageIntegration>()]);
        }
    }

    public void Dispose()
    {
        lock (_gate) Replace([]);
    }

    // Render thread as well as game thread: Owns reads only the menu id.
    public IPageIntegration? For(IMenu menu)
    {
        foreach (var integration in _active)
            if (integration.Owns(menu))
                return integration;
        return null;
    }

    public string? RegionOf(IPageIntegration integration, IMenu menu, IMenuComponent component)
    {
        try
        {
            return integration.RegionOf(menu, component);
        }
        catch (Exception ex)
        {
            Report(integration.Provider, ex);
            return null;
        }
    }

    public int RowsAdded(IPageIntegration integration, IMenu menu)
    {
        try
        {
            return Math.Max(0, integration.RowsAdded(menu));
        }
        catch (Exception ex)
        {
            Report(integration.Provider, ex);
            return 0;
        }
    }

    public void Draw(IPageIntegration integration, PageDraw page)
    {
        try
        {
            integration.Draw(page);
        }
        catch (Exception ex)
        {
            page.Reset();
            Report(integration.Provider, ex);
        }
    }

    // Keyed to the session, so reopening a page reads fresh data and redraws never re-query.
    public bool TryLoad<T>(IMenuSession session, string key, Func<Task<T>> load, out T value)
    {
        var task = (Task<T>)_loads.GetOrCreateValue(session).GetOrAdd(key, _ => Start(session, key, load));
        if (task.IsCompletedSuccessfully)
        {
            value = task.Result;
            return true;
        }
        value = default!;
        return false;
    }

    private Task<T> Start<T>(IMenuSession session, string key, Func<Task<T>> load)
    {
        Task<T> task;
        try
        {
            task = load();
        }
        catch (Exception ex)
        {
            task = Task.FromException<T>(ex);
        }

        _ = task.ContinueWith(done =>
        {
            if (done.IsFaulted)
                logger.LogWarning(done.Exception?.GetBaseException(), "[CustomHUD] Loading {Key} for a page failed.", key);
            core.Scheduler.NextTick(() =>
            {
                if (session.IsOpen) session.Invalidate();
            });
        }, TaskScheduler.Default);
        return task;
    }

    private IPageIntegration? Create(string provider, string key, IInterfaceManager interfaces,
        Func<IInterfaceManager, IPageIntegration?> factory)
    {
        if (!interfaces.HasSharedInterface(key)) return null;
        try
        {
            if (factory(interfaces) is { } integration) return integration;

            // Not necessarily an old build: Rampage's SkinsManager publishes "WeaponSkins.API" with its own contract.
            if (_reported.TryAdd("bind:" + provider, 0))
                logger.LogInformation(
                    "[CustomHUD] '{Key}' is published, but not as the {Provider} contract CustomHUD reads, so those menus draw plain.",
                    key, provider);
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or TypeLoadException
                                       or BadImageFormatException or MissingMemberException)
        {
            if (_reported.TryAdd("bind:" + provider, 0))
                logger.LogWarning(
                    "[CustomHUD] {Provider} is loaded, but CustomHUD cannot read its API ({Reason}), so its menus draw plain. Update {Provider} and CustomHUD together.",
                    provider, ex.Message, provider);
        }
        return null;
    }

    private void Replace(IPageIntegration[] next)
    {
        var previous = _active;
        _active = next;
        foreach (var old in previous)
            (old as IDisposable)?.Dispose();

        var before = previous.Select(i => i.Provider).ToHashSet();
        foreach (var added in next.Where(i => !before.Contains(i.Provider)))
            logger.LogInformation("[CustomHUD] Drawing {Provider}'s menus from its API.", added.Provider);
    }

    private void Report(string provider, Exception ex)
    {
        if (_reported.TryAdd(provider + ":" + ex.GetType().FullName, 0))
            logger.LogError(ex, "[CustomHUD] Drawing a {Provider} page failed; it draws as a plain menu.", provider);
    }
}
