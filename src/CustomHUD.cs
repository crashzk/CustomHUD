using CustomHUD.Capture;
using CustomHUD.Commands;
using CustomHUD.Config;
using CustomHUD.Contract;
using CustomHUD.Input;
using CustomHUD.Integrations;
using CustomHUD.Navigation;
using CustomHUD.Rendering;
using CustomHUD.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Plugins;

namespace CustomHUD;

[PluginMetadata(
    Id = "CustomHUD",
    Version = "2.0.0",
    Name = "CustomHUD",
    Author = "Shmitzas",
    Description = "CS2-themed Panorama renderer for the SwiftlyS2 menu framework. Consumer plugins need no code changes.")]
public sealed class CustomHUD(ISwiftlyCore core) : BasePlugin(core)
{
    private ServiceProvider? _services;
    private ILogger<CustomHUD>? _logger;
    private IDisposable? _configReload;
    private IDisposable? _rendererRegistration;
    private IDisposable? _componentRendererRegistration;
    private PanoramaMenuRenderer? _renderer;
    private PlayerPreferences? _prefs;
    private SteamIdCache? _steamIds;
    private NavModeResolver? _navModes;
    private MenuInputBridge? _input;
    private EventDelegates.OnClientPutInServer? _onPutInServer;
    private EventDelegates.OnClientSteamAuthorize? _onSteamAuthorize;
    private EventDelegates.OnClientDisconnected? _onDisconnected;
    private CancellationTokenSource? _retryTimer;
    private CancellationTokenSource? _nativeTextTimer;
    private HudSurface? _hudSurface;
    private NativeTextCapture? _nativeText;
    private PanelSurface? _panelSurface;
    private IntegrationHost? _integrations;
    private readonly PanelService _panels = new();
    private const string CookiesInterfaceKey = "Cookies.Player.v1";
    private readonly Lock _cookiesGate = new();
    private bool? _cookiesAttached;
    private bool _cookiesMissingLogged;

    public override void ConfigureSharedInterface(IInterfaceManager interfaceManager) =>
        interfaceManager.AddSharedInterface<ICustomHudPanels, PanelService>(ICustomHudPanels.InterfaceKey, _panels);

    public override void UseSharedInterface(IInterfaceManager interfaceManager)
    {
        TryAttachCookies(interfaceManager);
        _integrations?.Bind(interfaceManager);
    }

    public override void OnAllPluginsLoaded()
    {
        lock (_cookiesGate)
        {
            if (_cookiesAttached == false && !_cookiesMissingLogged)
            {
                _logger?.LogInformation(
                    "[CustomHUD] Cookies was not found; player preferences are in-memory only.");
                _cookiesMissingLogged = true;
            }
        }
    }

    public override void Load(bool hotReload)
    {
        Core.Configuration
            .InitializeJsonWithModel<CustomHudConfig>("config.jsonc", "CustomHUD")
            .Configure(b => b.AddJsonFile("config.jsonc", optional: false, reloadOnChange: true));

        var configProvider = new CustomHudConfigProvider();
        configProvider.Replace(ReadConfig() ?? new CustomHudConfig());

        var collection = new ServiceCollection();
        collection.AddSwiftly(Core);
        collection.AddSingleton(configProvider);
        collection.AddSingleton(sp => new PlayerPreferences(
            sp.GetRequiredService<ILogger<PlayerPreferences>>(),
            action => Core.Scheduler.NextTick(action)));
        collection.AddSingleton(new SteamIdCache(Core));
        collection.AddSingleton<NavModeResolver>();
        collection.AddSingleton(sp => new MenuEntityHost(Core, sp.GetRequiredService<ILogger<MenuEntityHost>>()));
        collection.AddSingleton<CoreTranslations>();
        // Core.Localizer is the server language, the fallback until a player's is resolved.
        collection.AddSingleton(new HudTranslations(player => Core.Translation.GetPlayerLocalizer(player), Core.Localizer));
        // BasePath is {configs}/plugins/CustomHUD; core reads menus.jsonc from {configs}.
        collection.AddSingleton(sp => new CoreMenuSettings(
            Path.GetFullPath(Path.Combine(Core.Configuration.BasePath, "..", "..")),
            () => !configProvider.Current.OwnMenuSettings(),
            sp.GetRequiredService<ILogger<CoreMenuSettings>>()));
        collection.AddSingleton(sp => new IntegrationHost(Core, sp.GetRequiredService<CustomHudConfigProvider>(),
            sp.GetRequiredService<HudTranslations>(), sp.GetRequiredService<ILogger<IntegrationHost>>()));
        collection.AddSingleton(sp => new PanoramaMenuRenderer(
            Core,
            sp.GetRequiredService<CustomHudConfigProvider>(),
            sp.GetRequiredService<NavModeResolver>(),
            sp.GetRequiredService<SteamIdCache>(),
            sp.GetRequiredService<MenuEntityHost>(),
            sp.GetRequiredService<CoreTranslations>(),
            sp.GetRequiredService<HudTranslations>(),
            sp.GetRequiredService<CoreMenuSettings>(),
            sp.GetRequiredService<IntegrationHost>(),
            sp.GetRequiredService<ILogger<PanoramaMenuRenderer>>()));
        collection.AddSingleton(sp => new MenuInputBridge(
            Core,
            sp.GetRequiredService<CustomHudConfigProvider>(),
            sp.GetRequiredService<PanoramaMenuRenderer>(),
            sp.GetRequiredService<NavModeResolver>(),
            sp.GetRequiredService<SteamIdCache>(),
            sp.GetRequiredService<CoreTranslations>(),
            sp.GetRequiredService<ILogger<MenuInputBridge>>()));
        collection.AddSingleton(sp => new HudSurface(
            sp.GetRequiredService<MenuEntityHost>(),
            sp.GetRequiredService<ILogger<HudSurface>>()));
        collection.AddSingleton(sp => new PanelSurface(
            sp.GetRequiredService<MenuEntityHost>(),
            sp.GetRequiredService<ILogger<PanelSurface>>()));
        collection.AddSingleton(_panels);
        collection.AddSingleton(sp => new NativeTextCapture(
            Core,
            sp.GetRequiredService<CustomHudConfigProvider>(),
            sp.GetRequiredService<SteamIdCache>(),
            sp.GetRequiredService<HudSurface>(),
            sp.GetRequiredService<ILogger<NativeTextCapture>>()));
        collection.AddSingleton(sp => new NumbersNavCommands(
            Core,
            sp.GetRequiredService<MenuInputBridge>(),
            sp.GetRequiredService<NavModeResolver>(),
            sp.GetRequiredService<SteamIdCache>(),
            sp.GetRequiredService<ILogger<NumbersNavCommands>>()));
        collection.AddSingleton<SettingsCommands>();
        collection.AddSingleton<TestCommands>();

        _services = collection.BuildServiceProvider();
        _logger = _services.GetRequiredService<ILogger<CustomHUD>>();

        _prefs = _services.GetRequiredService<PlayerPreferences>();

        _renderer = _services.GetRequiredService<PanoramaMenuRenderer>();
        _integrations = _services.GetRequiredService<IntegrationHost>();
        _input = _services.GetRequiredService<MenuInputBridge>();
        _steamIds = _services.GetRequiredService<SteamIdCache>();
        _steamIds.SeedConnected();
        _navModes = _services.GetRequiredService<NavModeResolver>();

        // A preference change only needs the open session invalidated; the next
        // compose picks up the new layout and the apply path respawns the
        // entity, since StrLayout is fixed once an entity has spawned.
        _prefs.NavModeChanged += OnPreferenceChanged;
        _prefs.ScaleChanged += OnPreferenceChanged;

        _rendererRegistration = Core.Menu.Renderers.Register(_renderer);
        _componentRendererRegistration = Core.Menu.Renderers.RegisterComponentRenderer(
            new PanoramaComponentRenderer(configProvider, _services.GetRequiredService<CoreTranslations>(),
                _services.GetRequiredService<HudTranslations>()));

        _input.Subscribe();

        _hudSurface = _services.GetRequiredService<HudSurface>();
        _nativeText = _services.GetRequiredService<NativeTextCapture>();
        _nativeText.Sync();

        // Expiry is swept rather than scheduled per message: a per-message
        // timer would have to be cancelled every time a newer message replaced
        // the one it was counting down. Panels with a duration share the sweep.
        _panelSurface = _services.GetRequiredService<PanelSurface>();
        _panels.Attach(Core, configProvider, _steamIds, _panelSurface);
        _nativeTextTimer = Core.Scheduler.RepeatBySeconds(0.25f, () =>
        {
            _hudSurface?.ExpireDue();
            _panelSurface?.ExpireDue();
        });

        // A despawn that failed has no other trigger: its only retry points
        // would be the next unrelated despawn and shutdown, so a released but
        // undespawned entity -- which is unrestricted, and therefore visible to
        // every player -- could sit on screen indefinitely.
        //
        // The host is captured directly rather than resolved per tick: the
        // service provider is disposed on unload, and the token below is what
        // stops the timer before that can be observed.
        var entityHost = _services.GetRequiredService<MenuEntityHost>();
        _retryTimer = Core.Scheduler.RepeatBySeconds(5f, entityHost.DrainRetries);

        var numbers = _services.GetRequiredService<NumbersNavCommands>();
        numbers.RegisterAliases();
        Core.Registrator.Register(numbers);
        Core.Registrator.Register(_services.GetRequiredService<SettingsCommands>());
        Core.Registrator.Register(_services.GetRequiredService<TestCommands>());

        // A transmit deny only covers clients connected when it was issued, and
        // a reconnect rebuilds the player object holding it -- so every live
        // menu entity has to be re-denied against each joining client or a
        // spectator who connects mid-menu starts receiving somebody else's menu.
        //
        // The same hook keeps the SteamID cache warm: the renderer runs off the
        // game thread and cannot read IPlayer.SteamID itself.
        _onPutInServer = e =>
        {
            _steamIds?.Refresh(e.PlayerId);
            _prefs?.RequestLoad(_steamIds?.Get(e.PlayerId) ?? 0);
            _renderer?.DenyForJoiningClient(e.PlayerId);
            _hudSurface?.DenyForJoiningClient(e.PlayerId);
            _panelSurface?.DenyForJoiningClient(e.PlayerId);
        };
        Core.Event.OnClientPutInServer += _onPutInServer;

        _onSteamAuthorize = e =>
        {
            _steamIds?.Refresh(e.PlayerId);
            _prefs?.RequestLoad(_steamIds?.Get(e.PlayerId) ?? 0);
            // A menu opened before authorization composed against SteamID 0 and
            // therefore default preferences. Nothing else would redraw it, so a
            // static menu would keep them for its whole lifetime.
            _renderer?.InvalidateSlot(e.PlayerId);
        };
        Core.Event.OnClientSteamAuthorize += _onSteamAuthorize;

        // Core does NOT call IMenuRenderer.Clear when a player disconnects
        // mid-menu -- it drops the session instead. Without this the entity
        // survives with nothing left to despawn it, and since
        // DenyForJoiningClient skips the entity's own slot, the next player to
        // take that slot would start receiving it.
        _onDisconnected = e =>
        {
            var steamId = _steamIds?.Get(e.PlayerId) ?? 0;
            _prefs?.Forget(steamId);
            _renderer?.OnPlayerDisconnected(e.PlayerId);
            _hudSurface?.Drop(e.PlayerId);
            _panelSurface?.Drop(e.PlayerId);
            _navModes?.ClearSlot(e.PlayerId);
            _steamIds?.Forget(e.PlayerId);
        };
        Core.Event.OnClientDisconnected += _onDisconnected;

        _configReload = ChangeToken.OnChange(
            () => Core.Configuration.Manager.GetReloadToken(),
            () =>
            {
                var previous = configProvider.Current;
                if (ReadConfig() is not { } next) return;
                configProvider.Replace(next);
                _logger?.LogInformation("[CustomHUD] Config reloaded.");
                ReportProblems(next);
                if (previous.OwnMenuSettings() != next.OwnMenuSettings()
                    || next.OwnMenuSettings() && (previous.DefaultNavMode != next.DefaultNavMode || previous.MaxVisibleLines != next.MaxVisibleLines))
                    ReportMenuSettings(next);
                // The capture hook is installed and removed on the game thread, as at load.
                Core.Scheduler.NextTick(() => _nativeText?.Sync());
            });

        _logger.LogInformation(
            "[CustomHUD] Renderer '{Id}' registered. Select it with \"Renderer\": \"{Id}\" in configs/menus/menus.jsonc.",
            configProvider.Current.RendererId, configProvider.Current.RendererId);

        ReportMenuSettings(configProvider.Current);
        ReportRetiredKeys();
        ReportProblems(configProvider.Current);
    }

    // The profiles before layout names, and Design, which a name's _v2 replaced. SW2 never removes a
    // key it once wrote, and a key that is still in the file but does nothing would look like a broken setting.
    private static readonly string[] RetiredKeys =
        ["Default", "Wasd", "Buttons", "Numbers", "Mouse", "Overrides", "MouseLayouts", "MouseOverrides", "NativeText", "Design"];

    private void ReportRetiredKeys()
    {
        var section = Core.Configuration.Manager.GetSection("CustomHUD");
        var retired = section.GetChildren().Select(child => child.Key)
            .Where(key => RetiredKeys.Contains(key, StringComparer.OrdinalIgnoreCase)).ToList();
        // Panels held whole layouts before it held names; SW2 cannot add the names beside them.
        if (section.GetSection("Panels").GetChildren().Any(panel => panel.GetChildren().Any())) retired.Add("Panels");
        if (retired.Count == 0) return;

        _logger?.LogWarning(
            "[CustomHUD] config.jsonc still has {Keys} from an older CustomHUD, which this version does not read. Layouts are now picked by name in Menus, Messages and Panels: delete config.jsonc and restart for a fresh one, then set again what you had changed.",
            string.Join(", ", retired));
        if (bool.TryParse(section["NativeText:Enabled"], out var captured) && captured)
            _logger?.LogWarning(
                "[CustomHUD] NativeText captured HUD messages; it is now set per kind in Messages, such as \"Center\": \"hud_text\", and stays off until you set it.");
    }

    private void ReportProblems(CustomHudConfig config)
    {
        foreach (var problem in config.Problems().Concat(ShapeProblems()))
            _logger?.LogWarning("[CustomHUD] config.jsonc: {Problem}", problem);
    }

    // The binder skips a value of the wrong shape without a word, so these are read from the file: one
    // name where Menus wants a name per mode, and a whole layout where Messages wants a name.
    private IEnumerable<string> ShapeProblems()
    {
        var section = Core.Configuration.Manager.GetSection("CustomHUD");
        foreach (var group in new[] { "Menus", "MenuOverrides" })
            foreach (var entry in section.GetSection(group).GetChildren())
                if (!string.IsNullOrEmpty(entry.Value))
                    yield return $"{group}.{entry.Key} \"{entry.Value}\" needs a name per mode, {{ \"Mouse\": \"{entry.Value}\", \"Keyboard\": \"{entry.Value}\" }}, so it is ignored.";
        foreach (var entry in section.GetSection("Messages").GetChildren())
            if (entry.GetChildren().Any())
                yield return $"Messages.{entry.Key} needs a layout name such as \"hud_text\", so it is ignored; a layout of your own goes in MessageLayouts.";
        // A Base entry without one would draw its base's profile in the class default, menu.vxml_c.
        foreach (var entry in section.GetSection("MenuLayouts").GetChildren())
            if (!string.IsNullOrEmpty(entry["Base"]) && string.IsNullOrEmpty(entry["File"]))
                yield return $"MenuLayouts.{entry.Key} has a Base but no File, the .vxml_c of your restyled copy.";
    }

    // Where players' starting mode and the page size come from, at start and whenever a reload moves them.
    private void ReportMenuSettings(CustomHudConfig config)
    {
        if (_services?.GetService<CoreMenuSettings>() is not { } menus) return;
        if (!config.OwnMenuSettings())
        {
            menus.Report();
            return;
        }
        _logger?.LogInformation(
            "[CustomHUD] Players who have not run !setnav navigate in {Mode} mode, and menu pages hold up to {Lines} lines (\"MenuConfig\": \"customhud\" in config.jsonc).",
            NavModeResolver.Name(config.StartMode(menus) ?? NavMode.Buttons), config.PageLines(menus));
    }

    public override void Unload()
    {
        // Stop the timers before anything they touch goes away -- they outlive
        // the plugin otherwise and fire against a disposed provider.
        _retryTimer?.Cancel();
        _retryTimer?.Dispose();
        _retryTimer = null;
        _nativeTextTimer?.Cancel();
        _nativeTextTimer?.Dispose();
        _nativeTextTimer = null;

        // Unhook the capture before anything else: while it is installed a
        // message can still arrive, and a Stop returned after teardown would
        // swallow somebody's text with nothing left to draw it.
        _nativeText?.Unsubscribe();
        _hudSurface?.Shutdown();
        _panels.Detach();
        _panelSurface?.Shutdown();

        // Shut the renderer down FIRST. Consumer sessions still hold a direct
        // reference to it, so disposing the registry handle neither revokes
        // those nor stops an in-flight background render -- and a render that
        // lands after cleanup would respawn an entity nothing will ever
        // despawn. The flag inside Shutdown is what makes that impossible.
        _renderer?.Shutdown();

        // Integrations subscribe to provider events; a handler left behind outlives this assembly.
        _integrations?.Dispose();

        if (_onPutInServer is not null) { Core.Event.OnClientPutInServer -= _onPutInServer; _onPutInServer = null; }
        if (_onSteamAuthorize is not null) { Core.Event.OnClientSteamAuthorize -= _onSteamAuthorize; _onSteamAuthorize = null; }
        if (_onDisconnected is not null) { Core.Event.OnClientDisconnected -= _onDisconnected; _onDisconnected = null; }

        _input?.Unsubscribe();

        if (_prefs is not null)
        {
            _prefs.NavModeChanged -= OnPreferenceChanged;
            _prefs.ScaleChanged -= OnPreferenceChanged;
            _prefs.Detach();
        }

        _componentRendererRegistration?.Dispose();
        _rendererRegistration?.Dispose();
        _configReload?.Dispose();
        _services?.Dispose();
    }

    private void OnPreferenceChanged(ulong steamId)
    {
        _renderer?.OnPreferenceChanged(steamId);
    }

    private void TryAttachCookies(IInterfaceManager interfaceManager)
    {
        lock (_cookiesGate)
        {
            if (_prefs is null) return;

            var backend = ResolveCookies(interfaceManager);
            if (backend is null)
            {
                var wasAttached = _cookiesAttached == true;
                _prefs.Detach();
                _cookiesAttached = false;
                if (wasAttached)
                    _logger?.LogWarning("[CustomHUD] Cookies was unloaded; player preferences are now in-memory only.");
                return;
            }

            var newlyAttached = _prefs.Attach(backend);
            var wasPreviouslyAttached = _cookiesAttached == true;
            _cookiesAttached = true;
            if (!wasPreviouslyAttached)
                _logger?.LogInformation("[CustomHUD] Persisting player preferences through Cookies.");

            if (newlyAttached)
                for (var slot = 0; slot < 64; slot++)
                    _prefs.RequestLoad(_steamIds?.Get(slot) ?? 0);
        }
    }

    private IPreferenceBackend? ResolveCookies(IInterfaceManager interfaceManager)
    {
        if (!interfaceManager.HasSharedInterface(CookiesInterfaceKey)) return null;

        try
        {
            return CookiesPreferenceBackend.TryCreate(interfaceManager, CookiesInterfaceKey);
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or TypeLoadException or BadImageFormatException)
        {
            _logger?.LogWarning(ex, "[CustomHUD] Cookies is loaded but its contract assembly is unavailable; restart the server.");
            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[CustomHUD] Could not connect to the Cookies preference interface.");
            return null;
        }
    }

    // A value the binder cannot convert ("MaxVisibleLines": "five") throws. It must neither stop the
    // load nor end a reload half done: the defaults apply at load, and the last good config after it.
    private CustomHudConfig? ReadConfig()
    {
        try
        {
            return Core.Configuration.Manager.GetSection("CustomHUD").Get<CustomHudConfig>() ?? new CustomHudConfig();
        }
        catch (InvalidOperationException ex)
        {
            (_logger ?? Core.Logger).LogError(
                "[CustomHUD] config.jsonc could not be read, so {Kept} stay in use: {Error}",
                _logger is null ? "the defaults" : "the settings it held before", ex.Message);
            return null;
        }
    }
}
