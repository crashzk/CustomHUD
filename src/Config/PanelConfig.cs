namespace CustomHUD.Config;

// The layout each shipped panel draws in, by name: a shipped layout, or the operator's PanelLayouts.
// Plugins name panels, never layouts, so "versus" stays the panel a caller asks for.
public sealed class PanelChoices
{
    public string Versus { get; set; } = "panel_versus";
    public string Ready { get; set; } = "panel_ready";
    public string Timeout { get; set; } = "panel_timeout";

    public static readonly string[] Shipped = ["versus", "ready", "timeout"];

    // A panel without an entry here draws in the layout of its own name, as a PanelLayouts one does.
    public static string Default(string panel)
        => Shipped.Contains(panel, StringComparer.OrdinalIgnoreCase) ? "panel_" + panel.ToLowerInvariant() : panel;

    public string For(string panel) => Named(panel.ToLowerInvariant() switch
    {
        "versus" => Versus,
        "ready" => Ready,
        "timeout" => Timeout,
        _ => null,
    }, panel);

    public IEnumerable<(string Panel, string Layout)> All()
        => [(nameof(Versus), For("versus")), (nameof(Ready), For("ready")), (nameof(Timeout), For("timeout"))];

    private static string Named(string? layout, string panel) => layout?.Trim() is { Length: > 0 } name ? name : Default(panel);
}

// A standalone HUD panel other plugins draw through the CustomHUD.Panels.v1
// shared interface (scoreboard strip, ready counter, timeout banner ...).
// Unlike a menu it has no rows, focus or input: just variables and classes.
public sealed class PanelProfile
{
    public string File { get; set; } = string.Empty;

    public string ShellPanel { get; set; } = "shell";
    public string HiddenClass { get; set; } = "hidden";

    // Panel the dialog variables are set on. Panorama variables inherit down
    // the tree, so the shell reaches every Label in the shipped layouts.
    public string VariablePanel { get; set; } = "shell";

    // Used when the caller passes no duration. <= 0 stays up until hidden.
    public float DurationSeconds { get; set; } = 0f;

    // Set to "" on spawn: Panorama renders an unset {s:var} literally.
    public List<string> BlankVariables { get; set; } = [];

    public static Dictionary<string, PanelProfile> Defaults() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["panel_versus"] = new()
        {
            File = "panel_versus.vxml_c",
            BlankVariables = ["team1", "team2", "score1", "score2", "status", "detail"],
        },
        ["panel_ready"] = new()
        {
            File = "panel_ready.vxml_c",
            BlankVariables = ["title", "count", "state", "hint"],
        },
        ["panel_timeout"] = new()
        {
            File = "panel_timeout.vxml_c",
            BlankVariables = ["title", "team", "timer", "detail"],
        },
    };
}
