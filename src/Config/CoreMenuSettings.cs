using CustomHUD.Navigation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace CustomHUD.Config;

// What CustomHUD takes from core's menus.jsonc. MenuLayout.MaxVisibleLines is internal to core,
// so it is read from the same file with the same parsing: a page cut at any other number would
// disagree with the one core enforces. Menus.InputMethod is where a player's navigation starts.
// Both are in use only under "MenuConfig": "swiftlys2" (CustomHudConfig.StartMode, PageLines),
// and only then does this log them.
internal sealed class CoreMenuSettings : IDisposable
{
    public const int DefaultMaxVisibleLines = 5;
    public const string RelativePath = "menus/menus.jsonc";

    private readonly ILogger _logger;
    private readonly Func<bool> _inUse;
    private readonly PhysicalFileProvider? _files;
    private readonly ConfigurationRoot? _config;
    private readonly IDisposable? _reload;
    private volatile int _maxVisibleLines = DefaultMaxVisibleLines;
    // A NavMode, or -1 while menus.jsonc names none: read on the render thread, written on reload.
    private volatile int _navMode = -1;
    private volatile string? _inputMethod;

    // configRoot is SwiftlyS2's configs directory, the base core resolves the file against.
    public CoreMenuSettings(string? configRoot, Func<bool> inUse, ILogger logger)
    {
        _logger = logger;
        _inUse = inUse;
        if (configRoot is null || !Directory.Exists(configRoot))
        {
            if (inUse())
                logger.LogWarning("[CustomHUD] Configs directory '{Root}' not found; menu pages use core's default of {Lines} lines.",
                    configRoot, DefaultMaxVisibleLines);
            return;
        }

        try
        {
            _files = new PhysicalFileProvider(configRoot);
            var builder = new ConfigurationBuilder();
            builder.Add(new JsonConfigurationSource
            {
                FileProvider = _files,
                Path = RelativePath,
                Optional = true,
                ReloadOnChange = true,
                // A file caught mid-save must not throw on the watcher thread.
                OnLoadException = context => context.Ignore = true,
            });
            _config = (ConfigurationRoot)builder.Build();
            Reload();
            _reload = ChangeToken.OnChange(_config.GetReloadToken, Reload);
        }
        catch (Exception ex)
        {
            if (inUse())
                logger.LogWarning(ex, "[CustomHUD] Could not read {Path}; menu pages use core's default of {Lines} lines.",
                    RelativePath, DefaultMaxVisibleLines);
        }
    }

    public int MaxVisibleLines => _maxVisibleLines;

    // The mode a player is in until they run !setnav, when menus.jsonc names one.
    public NavMode? DefaultNavMode => _navMode < 0 ? null : (NavMode)_navMode;

    // Mirrors SwiftlyS2.Core.Menu.Config.MenuLayoutOptions.
    public static int Parse(string? value)
        => int.TryParse(value, out var parsed) ? Math.Clamp(parsed, 1, 32) : DefaultMaxVisibleLines;

    private void Reload()
    {
        var lines = Parse(_config?["MenuLayout:MaxVisibleLines"]);
        var changed = Interlocked.Exchange(ref _maxVisibleLines, lines) != lines;

        var method = _config?["Menus:InputMethod"]?.Trim();
        var mode = NavModeResolver.FromInputMethod(method);
        var code = mode is { } known ? (int)known : -1;
        changed |= Interlocked.Exchange(ref _navMode, code) != code;
        _inputMethod = method;

        // CustomHUD reports the values in use at start; this reports an edit made while they are.
        if (changed && _reload is not null && _inUse()) Report();
    }

    public void Report()
    {
        _logger.LogInformation("[CustomHUD] Menu pages hold up to {Lines} lines (MenuLayout.MaxVisibleLines in configs/{Path}).",
            MaxVisibleLines, RelativePath);
        if (DefaultNavMode is { } start)
            _logger.LogInformation("[CustomHUD] Players who have not run !setnav navigate in {Mode} mode (Menus.InputMethod in configs/{Path}).",
                NavModeResolver.Name(start), RelativePath);
        else if (!string.IsNullOrEmpty(_inputMethod))
            _logger.LogWarning("[CustomHUD] Menus.InputMethod \"{Method}\" in configs/{Path} is not buttons, chat, wasd, numbers or mouse, so players who have not run !setnav start in buttons mode.",
                _inputMethod, RelativePath);
    }

    public void Dispose()
    {
        _reload?.Dispose();
        _config?.Dispose();
        _files?.Dispose();
    }
}
