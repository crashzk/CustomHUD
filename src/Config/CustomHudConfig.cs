using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.Json.Serialization;
using CustomHUD.Navigation;

namespace CustomHUD.Config;

// Bound to the "CustomHUD" section of config.jsonc and reloaded live.
//
// SW2 writes every serialised property into config.jsonc on first start, its [Description] as a
// comment above it, and on later starts adds a property a release introduces, though never a
// dictionary key. So the file holds only names and numbers an operator picks from, and the layouts
// the names stand for live in code (BuiltInLayouts), where a release can improve them.
//
// [JsonIgnore] keeps a setting out of the file while the binder still reads it when an operator adds
// it by hand: the hooks for Panorama layouts of their own, and the rarely changed rest.
public sealed class CustomHudConfig
{
    public const string SwiftlyS2MenuConfig = "swiftlys2";
    public const string CustomHudMenuConfig = "customhud";

    [Description("Where players' starting navigation mode and the menu page size come from:\n"
        + "\"swiftlys2\" = Menus.InputMethod and MenuLayout.MaxVisibleLines in configs/menus/menus.jsonc,\n"
        + "\"customhud\" = DefaultNavMode and MaxVisibleLines below.")]
    public string MenuConfig { get; set; } = SwiftlyS2MenuConfig;

    [Description("The mode players start in until they run !setnav: \"buttons\", \"wasd\", \"numbers\" or \"mouse\".\n"
        + "Used with \"MenuConfig\": \"customhud\".")]
    public string DefaultNavMode { get; set; } = "buttons";

    // Core's own default and range (SwiftlyS2.Core.Menu.Config.MenuLayoutOptions).
    [Description("Lines on one page of a keyboard menu, 1-32. Used with \"MenuConfig\": \"customhud\".")]
    public int MaxVisibleLines { get; set; } = 5;

    [Description("The layout each plugin's menus draw in, for mouse players and for keyboard players (buttons, wasd, numbers).\n"
        + "Names ending in _v2 are CS2's store look, the rest the first designs. Every layout: https://github.com/shmitzas/CustomHUD/wiki/Layouts")]
    public MenuChoices Menus { get; set; } = new();

    [Description("The game's own HUD messages: \"game\" leaves a kind to CS2, \"hud_text\" or \"hud_alert\" draws it in that CustomHUD layout.\n"
        + "\"hud_text_v2\" and \"hud_alert_v2\" are the same in CS2's store look.")]
    public MessageChoices Messages { get; set; } = new();

    [Description("Layout of each panel other plugins show through CustomHUD.")]
    public PanelChoices Panels { get; set; } = new();

    // Off by default -- prod servers stay quiet.
    [Description("Log every menu frame drawn and every message captured.")]
    public bool DetailedLogging { get; set; } = false;

    // Id this plugin registers its renderer under, and the value server owners
    // put in menus.jsonc. Configurable so two builds can coexist on one server
    // during a layout migration.
    [JsonIgnore]
    public string RendererId { get; set; } = DefaultRendererId;

    // Consumers hardcode this id in their raw nodes, so it stays accepted even
    // after RendererId is renamed.
    public const string DefaultRendererId = "customhud.panorama";

    // Prefix every layout File is resolved against, unless the File value
    // already contains a '/'. Keeps the common case to a bare filename. CS2
    // refuses addon layouts outside panorama/layout/custom_game/.
    [JsonIgnore]
    public string LayoutRoot { get; set; } = "panorama/layout/custom_game/customhud/";

    // Footer hint overrides per nav mode, plus "chat" for menus whose input method
    // is Chat. Replaces core's footer outright. {up} {down} {select} {close} expand
    // to the menu's keymap. An entry applies in every language; unset keys come
    // from the player's translation file (customhud.hint.<key>).
    [JsonIgnore]
    public Dictionary<string, string> Hints { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // The English hints, used when no translation file has a key. Rewording one turns
    // every config.jsonc still holding the old text into an override (FrameComposer.Override).
    public static Dictionary<string, string> DefaultHints() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["wasd"] = "W/S move · E select · R close",
        ["buttons"] = "{up}/{down} move · {select} select · {close} close",
        ["numbers"] = "1-6 select · 7/8 page · 9 close",
        ["mouse"] = "Click to select",
        ["chat"] = "!N select · !0 close",
        // For a menu with nothing to select, such as a vote tally kept open after voting.
        ["wasd.view"] = "R close",
        ["buttons.view"] = "{close} close",
        ["numbers.view"] = "9 close",
        ["mouse.view"] = "Click {close} to close",
        ["chat.view"] = "!0 close",
    };

    // Which design a page draws as, by menu id or trailing-* prefix
    // ("mapchooser.eof-vote*": "maps-rows"). Wins over a page's `view=` and its layout's own Views.
    [JsonIgnore]
    public Dictionary<string, string> PageViews { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // One menu, by id or trailing-* prefix, in other layouts than its plugin's. Wins over Menus.
    [JsonIgnore]
    public Dictionary<string, MenuChoice> MenuOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Layouts of the operator's own, by the name Menus, MenuOverrides, Messages or Panels then pick;
    // one named like a built-in replaces it. Merged with nothing: a field left out takes its class
    // default, unless a menu layout names a Base (MenuLayoutProfile.Base).
    [JsonIgnore]
    public Dictionary<string, MenuLayoutProfile> MenuLayouts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public Dictionary<string, NativeTextProfile> MessageLayouts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public Dictionary<string, PanelProfile> PanelLayouts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Methods from here on: SW2 would write a getter's value into config.jsonc.

    // Anything but "customhud" reads menus.jsonc, as core itself does.
    public bool OwnMenuSettings() => string.Equals(MenuConfig?.Trim(), CustomHudMenuConfig, StringComparison.OrdinalIgnoreCase);

    // Where a player starts until they run !setnav, and the lines a keyboard page holds.
    internal NavMode? StartMode(CoreMenuSettings menus)
        => OwnMenuSettings() ? NavModeResolver.Parse(DefaultNavMode?.Trim()) : menus.DefaultNavMode;

    internal int PageLines(CoreMenuSettings menus)
        => OwnMenuSettings() ? Math.Clamp(MaxVisibleLines, 1, 32) : menus.MaxVisibleLines;

    // MenuOverrides, then the plugin's entry in Menus, then its built-in default. A name that is no
    // layout for this plugin in this mode passes to the next.
    public MenuLayoutProfile Resolve(string menuId, NavMode mode)
    {
        var mouse = mode == NavMode.Mouse;
        var plugin = BuiltInLayouts.PluginOf(menuId);
        return MenuLayout(Match(MenuOverrides, menuId)?.For(mouse), plugin, menuId, mouse)
            ?? MenuLayout(Menus.For(plugin).For(mouse), plugin, menuId, mouse)
            ?? BuiltInLayouts.Menu(BuiltInLayouts.DefaultMenu(plugin, mouse), plugin, menuId, mouse)!;
    }

    private MenuLayoutProfile? MenuLayout(string? name, string plugin, string menuId, bool mouse)
    {
        if (name is null) return null;
        if ((MenuLayouts.GetValueOrDefault(name) ?? MenuLayouts.GetValueOrDefault(BuiltInLayouts.Bare(name))) is not { } own)
            return BuiltInLayouts.Menu(name, plugin, menuId, mouse);
        if (own.Base.Trim().Length == 0) return own;
        return BuiltInLayouts.Menu(own.Base.Trim(), plugin, menuId, mouse) is { } shipped
            ? _variations.GetOrAdd((shipped, own.File), key => key.Shipped.WithFile(key.File))
            : null;
    }

    // A Base entry's profile per shipped profile, built once: FrameComposer fingerprints a profile per instance.
    private readonly ConcurrentDictionary<(MenuLayoutProfile Shipped, string File), MenuLayoutProfile> _variations = new();

    // Null while the game draws that kind itself, as it does for a name no layout has.
    public NativeTextProfile? ResolveMessage(NativeTextKind kind)
        => Messages.For(kind) is { } name && !name.Equals(BuiltInLayouts.Game, StringComparison.OrdinalIgnoreCase)
            ? MessageLayouts.GetValueOrDefault(name) ?? BuiltInLayouts.Messages.GetValueOrDefault(BuiltInLayouts.Bare(name))
            : null;

    public bool CapturesMessages() => Enum.GetValues<NativeTextKind>().Any(kind => ResolveMessage(kind) is not null);

    // The layout Panels names for a panel, else its default. Null for a panel neither the shipped
    // ones nor PanelLayouts define.
    public PanelProfile? ResolvePanel(string panel) => PanelLayout(Panels.For(panel)) ?? PanelLayout(PanelChoices.Default(panel));

    private PanelProfile? PanelLayout(string name)
        => PanelLayouts.GetValueOrDefault(name) ?? BuiltInLayouts.Panels.GetValueOrDefault(BuiltInLayouts.Bare(name));

    // The panel names a plugin can show.
    public IEnumerable<string> PanelNames() => PanelChoices.Shipped.Union(PanelLayouts.Keys, StringComparer.OrdinalIgnoreCase);

    public string ResolveLayoutPath(MenuLayoutProfile profile) => LayoutPath(profile.File);
    public string ResolveMessagePath(NativeTextProfile profile) => LayoutPath(profile.File);
    public string ResolvePanelPath(PanelProfile profile) => LayoutPath(profile.File);

    private string LayoutPath(string file) => file.Contains('/') ? file : LayoutRoot + file;

    // The scale steps !hudscale offers: those of the keyboard layout every other plugin draws in.
    // A method, not a property: the binder would append config.jsonc's values onto a getter's list.
    public List<int> ScaleSteps() => Resolve("", NavMode.Buttons).Classes.ScaleSteps;

    // Exact id first, then the longest trailing-* prefix.
    internal static T? Match<T>(IReadOnlyDictionary<string, T> rules, string menuId) where T : class
    {
        if (rules.TryGetValue(menuId, out var exact)) return exact;
        T? match = null;
        var longest = -1;
        foreach (var (pattern, value) in rules)
            if (pattern.EndsWith('*') && pattern.Length > longest
                && menuId.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase))
            {
                match = value;
                longest = pattern.Length;
            }
        return match;
    }

    // Settings that name nothing, or do nothing as set: one log line each, saying what applies instead.
    public IEnumerable<string> Problems()
    {
        if (!IsOneOf(MenuConfig, SwiftlyS2MenuConfig, CustomHudMenuConfig))
            yield return $"MenuConfig \"{MenuConfig}\" is not \"swiftlys2\" or \"customhud\", so configs/menus/menus.jsonc's settings apply.";
        if (NavModeResolver.Parse(DefaultNavMode?.Trim()) is null)
            yield return $"DefaultNavMode \"{DefaultNavMode}\" is not buttons, wasd, numbers or mouse, so it counts as buttons.";
        if (MaxVisibleLines is < 1 or > 32)
            yield return $"MaxVisibleLines {MaxVisibleLines} is outside 1-32, so it counts as {Math.Clamp(MaxVisibleLines, 1, 32)}.";
        if (!OwnMenuSettings() && (!IsOneOf(DefaultNavMode, "buttons") || MaxVisibleLines != 5))
            yield return "DefaultNavMode and MaxVisibleLines apply only with \"MenuConfig\": \"customhud\"; configs/menus/menus.jsonc's are in use.";
        foreach (var plugin in BuiltInLayouts.PluginNames())
            foreach (var mouse in new[] { true, false })
                if (Menus.For(plugin).For(mouse) is { } name && MenuLayout(name, plugin, "", mouse) is null)
                    yield return $"Menus.{plugin}.{Mode(mouse)} \"{name}\" is no {Mode(mouse).ToLowerInvariant()} layout for {plugin}, so it draws in \"{BuiltInLayouts.DefaultMenu(plugin, mouse)}\".";
        foreach (var (id, choice) in MenuOverrides)
            foreach (var mouse in new[] { true, false })
                if (choice.For(mouse) is { } name && MenuLayout(name, BuiltInLayouts.PluginOf(id.TrimEnd('*')), "", mouse) is null)
                    yield return $"MenuOverrides.{id}.{Mode(mouse)} \"{name}\" is no {Mode(mouse).ToLowerInvariant()} layout there, so Menus applies.";
        foreach (var (name, own) in MenuLayouts)
            if (own.Base.Trim() is { Length: > 0 } shipped && !BuiltInLayouts.IsMenu(shipped))
                yield return $"MenuLayouts.{name}.Base \"{shipped}\" is no shipped layout, so a menu that picks {name} draws in its default.";
        foreach (var kind in Enum.GetValues<NativeTextKind>())
            if (Messages.For(kind) is { } name && !name.Equals(BuiltInLayouts.Game, StringComparison.OrdinalIgnoreCase) && ResolveMessage(kind) is null)
                yield return $"Messages.{kind} \"{name}\" is not game, hud_text, hud_alert, hud_text_v2, hud_alert_v2 or a MessageLayouts name, so the game draws it.";
        foreach (var (panel, name) in Panels.All())
            if (PanelLayout(name) is null)
                yield return $"Panels.{panel} \"{name}\" is no panel layout, so it draws in \"{PanelChoices.Default(panel)}\".";
    }

    private static string Mode(bool mouse) => mouse ? "Mouse" : "Keyboard";

    private static bool IsOneOf(string? value, params string[] allowed)
        => allowed.Any(a => string.Equals(value?.Trim(), a, StringComparison.OrdinalIgnoreCase));
}

// One Panorama layout and the vocabulary needed to address it.
public sealed class MenuLayoutProfile
{
    // Compiled layout Panorama loads at runtime. The `_c` suffix is not
    // optional -- the engine resolves the compiled artifact, not the source.
    public string File { get; set; } = "menu.vxml_c";

    // MenuLayouts only: a shipped layout whose profile, for each plugin it serves, this entry draws
    // with its own File -- a restyled copy that keeps every panel id. Its other fields are ignored.
    public string Base { get; set; } = "";

    public MenuLayoutProfile WithFile(string file)
    {
        var copy = (MenuLayoutProfile)MemberwiseClone();
        copy.File = file;
        copy.Base = "";
        return copy;
    }

    // Row slots the layout declares. The renderer never draws more than this
    // many rows regardless of what the frame contains, so a layout with 12
    // slots cannot be made to overflow by a menu with 30 items.
    //
    // Without ShowAllRows a page also stops at core's limits (the menu's
    // ItemsPerPage, MaxVisibleLines in menus.jsonc, at most 32), and at six
    // rows in numbers mode.
    public int RowSlots { get; set; } = 20;

    // Draw the whole list in RowSlots-sized chunks instead of pages capped by
    // core's limits, so a scrolling layout can show it all.
    public bool ShowAllRows { get; set; }

    // With ShowAllRows in mouse mode, a list longer than RowSlots pages through
    // a card at each end of the grid instead of footer buttons. Two slots of
    // every chunk are kept for those cards.
    public bool PageTiles { get; set; }

    public LayoutPanels Panels { get; set; } = new();
    public LayoutVariables Variables { get; set; } = new();
    public LayoutRowSpec Row { get; set; } = new();
    public LayoutClasses Classes { get; set; } = new();
    public MenuSidebarProfile? Sidebar { get; set; }
    // Tabs over a row layout's list (menu_skins_mouse). Null for none; a page
    // layout's tabs are part of Page.
    public MenuTabsProfile? Tabs { get; set; }
    // A one-item card drawn instead of the rows while a frame opens with a
    // `hero` entry. Null for layouts without one.
    public MenuCardProfile? Card { get; set; }
    // Chips, crumb, tabs, regions and views (menu_locker). Null for row layouts.
    public MenuPageProfile? Page { get; set; }
    // A row layout's views: menu id (or trailing-* prefix) to the name the shell carries as
    // Classes.View. The operator's PageViews and a header's `view=` win, as on a page.
    public Dictionary<string, string> Views { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    // The item a row layout's page is about, from its header's `spot` node. Null for none.
    public MenuSpotProfile? Spot { get; set; }
    public Dictionary<string, string> ColorIcons { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Variables the layout declares but the renderer has no value for. Panorama
    // renders the literal `{s:name}` token for any dialog variable that is never
    // written, so every optional slot must be blanked explicitly or it shows its
    // own placeholder to the player.
    public List<string> BlankOnOpen { get; set; } = new();

    public static MenuLayoutProfile MouseGrid() => new()
    {
        File = "menu_mouse.vxml_c",
        RowSlots = 60,
        ShowAllRows = true,
        Panels = new()
        {
            Body = "grid",
            Back = "nav_back", Previous = "nav_previous", Next = "nav_next",
        },
        Row = new()
        {
            PanelId = "tile_{0}", LabelVar = "tile_{0}", ValueVar = "tile_{0}_value",
            KeyVar = null, IconPanel = "tile_{0}_icon",
        },
        Classes = new() { RowCount = "hk-tiles-{0}", Rewind = BuiltInRewind },
    };

    // The class the built-in scrolling layouts style for the renderer's scroll rewind (LayoutClasses.Rewind).
    public const string BuiltInRewind = "chud-rewind";

    // The sidebar stands in for the root page and footer paging: the root opens
    // its first entry, and a list past 128 rows pages through PageTiles. 128
    // holds every list but sticker capsules, of which items_game has 27 longer.
    public static MenuLayoutProfile WeaponSkinsLocker() => new()
    {
        File = "menu_skins_mouse.vxml_c",
        RowSlots = 128,
        ShowAllRows = true,
        PageTiles = true,
        Panels = new() { Back = "nav_back", Close = "nav_close", Previous = "", Next = "" },
        Variables = new() { PageVar = "" },
        Row = new() { KeyVar = null, ThumbPanel = "thumb_{0}", TypeVar = "option_{0}_type" },
        Classes = new() { Rewind = BuiltInRewind },
        Sidebar = new()
        {
            Slots = 12, RootButton = "", AutoOpen = 0,
            // WeaponSkins' entry ids are the ids of the pages they open.
            Icons = new(StringComparer.OrdinalIgnoreCase)
            {
                ["weaponskins.weapons"] = "weapons", ["weaponskins.knifes"] = "knives",
                ["weaponskins.gloves"] = "gloves", ["weaponskins.agents"] = "agents",
                ["weaponskins.musickits"] = "music-kits",
                ["weaponskins.graffiti"] = "graffiti", ["weaponskins.pins"] = "pins",
                ["weaponskins.weaponproperties"] = "weapon-props",
                ["weaponskins.knifeproperties"] = "knife-props",
                ["weaponskins.gloveproperties"] = "glove-props",
                ["weaponskins.agentproperties"] = "agent-props",
            },
        },
        // Only Weapon Skins' categories: its knife and glove pages are plain submenus too.
        Tabs = new() { Hosts = ["weaponskins.weapons"] },
        Card = new(),
        ColorIcons = new(StringComparer.OrdinalIgnoreCase)
        {
            ["#b0c3d9"] = "1", ["#5e98d9"] = "2", ["#4b69ff"] = "3",
            ["#8847ff"] = "4", ["#d32ce6"] = "5", ["#eb4b4b"] = "6",
            ["#e4ae39"] = "7", ["#cf6a32"] = "8", ["#ffd700"] = "9",
        },
    };

    // The default WeaponSkins locker, in CS2's own store look. Unlike the first design it keeps
    // the categories page, drawn as a mosaic, rather than opening its first tab in its place.
    public static MenuLayoutProfile WeaponSkinsLockerV2()
    {
        var profile = WeaponSkinsLocker();
        profile.File = "menu_skins_mouse_v2.vxml_c";
        profile.Variables.CrumbPanel = "nav_back";
        profile.Variables.CrumbVar = "crumb";
        profile.Variables.InfoPanel = "info";
        profile.Variables.InfoVar = "info";
        profile.Tabs!.AutoOpen = -1;
        profile.Card!.SlotTiles = 6;
        // Reached from the spotlight of the item they set up, through its Properties link.
        profile.Sidebar!.Omit =
        [
            "weaponskins.weaponproperties", "weaponskins.knifeproperties",
            "weaponskins.gloveproperties", "weaponskins.agentproperties",
        ];
        profile.Spot = new();
        profile.Views["weaponskins.weapons"] = "ws-categories";
        profile.Views["weaponskins.agents"] = "ws-agents";
        profile.Views["weaponskins.knifes.paintkits"] = "ws-knives";
        profile.Views["weaponskins.gloves.paintkits"] = "ws-gloves";
        return profile;
    }
}

// The item a row layout's page is about (a weapon's applied finish), drawn over the list from
// the header node marked `spot`: its text is the name, `type=`, `sub=` and `note=` the lines
// around it, `image=` and `icon=` its art and rarity, each `fact=Label|Value` one line of a
// table. Panel carries Classes.Icon, HasThumb, HasType, HasSubClass, HasNoteClass and Chosen.
public sealed class MenuSpotProfile
{
    public string Panel { get; set; } = "spot";
    public string ThumbPanel { get; set; } = "spot_thumb";
    public string NameVar { get; set; } = "spot_name";
    public string TypeVar { get; set; } = "spot_type";
    public string SubVar { get; set; } = "spot_sub";
    public string NoteVar { get; set; } = "spot_note";
    public int FactSlots { get; set; } = 4;
    public string FactPanel { get; set; } = "spot_fact_{0}";
    public string FactLabelVar { get; set; } = "spot_fact_{0}";
    public string FactValueVar { get; set; } = "spot_fact_{0}_value";
    // On Panels.Shell while a spot is drawn, so the layout can make room for it.
    public string ShownClass { get; set; } = "has-spot";
    public string HasSubClass { get; set; } = "has-sub";
    public string HasNoteClass { get; set; } = "has-note";
    // The Button a `link=entry|pick|label` draws, and its label: it opens that root entry, then the
    // entry `pick` names on its page, which then returns here. Hidden without one.
    public string LinkButton { get; set; } = "spot_link";
    public string LinkVar { get; set; } = "spot_link";

    public string Fact(int i) => string.Format(FactPanel, i);
    public string FactLabel(int i) => string.Format(FactLabelVar, i);
    public string FactValue(int i) => string.Format(FactValueVar, i);
}

// Panels addressed by class toggles rather than by text.
public sealed class LayoutPanels
{
    // Root panel. Carries `hidden` until the first apply, plus tint and scale.
    public string Shell { get; set; } = "shell";
    // Row container. Carries the row-count class that drives panel height.
    public string Body { get; set; } = "options";
    public string Title { get; set; } = "title";
    public string Footer { get; set; } = "footer";
    // Mouse-only buttons; empty means the layout has none and nothing is written.
    public string Back { get; set; } = "nav_back";
    public string Previous { get; set; } = "nav_previous";
    public string Next { get; set; } = "nav_next";
    // Leaves every level of a submenu stack at once; Back only goes up one.
    // With one, Back is hidden wherever it has nowhere to go.
    public string Close { get; set; } = "";
}

public sealed class MenuSidebarProfile
{
    public int Slots { get; set; } = 10;
    // Empty for a layout without a root button.
    public string RootButton { get; set; } = "nav_root";
    public string ButtonPrefix { get; set; } = "category_";
    public string LabelVar { get; set; } = "category";
    // On the slot of a root-menu DividerComponent.
    public string DividerClass { get; set; } = "divider";
    // Entry (dividers not counted) a root menu opens straight into in mouse
    // mode, so the root page is never shown; backing out of that level then
    // closes the menu. -1 shows the root page.
    public int AutoOpen { get; set; } = -1;
    // Panel per slot receiving the entry's icon class, `{0}` = slot. Empty for
    // a layout without icons. The layout draws a default icon there, so an
    // entry missing from Icons still has one.
    public string IconPanel { get; set; } = "category_{0}_icon";
    // `{0}` = an icon token from customhud_icons.vcss ("gloves").
    public string IconClass { get; set; } = "ico-{0}";
    // Entry -> icon token, keyed by the entry's component id, else its label.
    public Dictionary<string, string> Icons { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    // A label ending in a count, "Visuals (23)", splits into the label and CountVar,
    // and the slot gets CountClass. Empty CountVar keeps the label whole.
    public string CountVar { get; set; } = "";
    public string CountClass { get; set; } = "has-count";
    // On the entry whose id a page's header names with `live=` (MapChooser's Vote while a vote runs).
    public string LiveClass { get; set; } = "live";
    // Root entries left out, by component id or label: pages the layout reaches another way (the
    // WeaponSkins locker's properties pages, from its spotlight). A divider left at an end, or beside
    // another, goes with them.
    public List<string> Omit { get; set; } = new();

    public string? Icon(int i) => IconPanel.Length == 0 ? null : string.Format(IconPanel, i);
}

// The second level as tabs on a row layout, Mouse mode only: the static submenus of
// a sidebar entry's page, drawn while the player is on one of them. Unlike Page's
// tabs, only a page Hosts names is a host: a row layout's pages are mostly plain
// submenus (WeaponSkins' knife and glove types), which would all become tabs.
public sealed class MenuTabsProfile
{
    // Menu ids of the hosts; a trailing * matches an id prefix.
    public List<string> Hosts { get; set; } = new();
    public int Slots { get; set; } = 8;
    public string Panel { get; set; } = "tabs";
    public string Prefix { get; set; } = "tab_";
    public string LabelVar { get; set; } = "tab";
    public string CountVar { get; set; } = "tab_count";
    // The tab a host opens in place of its own page, as Sidebar.AutoOpen does for the root; -1 shows the page.
    public int AutoOpen { get; set; } = 0;
    // On Panels.Shell while the strip shows; on a tab whose label ended in a count.
    public string HasTabsClass { get; set; } = "has-tabs";
    public string HasCountClass { get; set; } = "has-count";
}

// The card: a hero (image, rarity line, name) over form fields. Each field
// holds one body entry; an entry marked `inline` joins the field before it as
// its switch, clear button or slider track. Field `{0}` is the field index.
public sealed class MenuCardProfile
{
    // On Panels.Shell while the card is shown; the layout hides its rows with it.
    public string ModeClass { get; set; } = "card-mode";
    public string HeroPanel { get; set; } = "hero";
    public string HeroThumbPanel { get; set; } = "hero_thumb";
    public string HeroNameVar { get; set; } = "hero_name";
    public string HeroTypeVar { get; set; } = "hero_type";
    public int Slots { get; set; } = 8;
    // Receives the classes and variables. Never a click target itself.
    public string FieldPanel { get; set; } = "field_{0}";
    public string LabelVar { get; set; } = "field_{0}";
    public string ValueVar { get; set; } = "field_{0}_value";
    // Click targets: the field body, its inline control, and track segment
    // `{0}{SegmentSuffix}{n}` for n = 0 .. Segments - 1, spread evenly over the
    // slider's range.
    public string HitSuffix { get; set; } = "_hit";
    public string AsideSuffix { get; set; } = "_aside";
    public string SegmentSuffix { get; set; } = "_seg";
    public int Segments { get; set; } = 51;
    // `{0}` = input | toggle | slider | choice | action | text.
    public string KindClass { get; set; } = "kind-{0}";
    public string AsideClass { get; set; } = "aside-{0}";
    public string OnClass { get; set; } = "is-on";
    public string AsideOnClass { get; set; } = "aside-on";
    public string TrackClass { get; set; } = "has-track";
    // `{0}` = slider position, 0 .. 100.
    public string FillClass { get; set; } = "fill-{0}";
    // `{0}` = the `track=` preset a slider asks for ("wear").
    public string PresetClass { get; set; } = "track-{0}";
    // Tiles per field for its row's `slot=` entries (a weapon's sticker slots); 0 for a layout
    // without them. Tile k is the Button "{field}{SlotSuffix}{k}", its picture panel that plus
    // SlotThumbSuffix, and its name the variable of the Button's own id.
    public int SlotTiles { get; set; }
    public string SlotSuffix { get; set; } = "_slot";
    public string SlotThumbSuffix { get; set; } = "_thumb";
    // On a field whose row has slots.
    public string SlotsClass { get; set; } = "has-slots";

    public string Field(int i) => string.Format(FieldPanel, i);
    public string Label(int i) => string.Format(LabelVar, i);
    public string Value(int i) => string.Format(ValueVar, i);
    public string Slot(int field, int tile) => Field(field) + SlotSuffix + tile;

    public string FieldPrefix
    {
        get
        {
            var brace = FieldPanel.IndexOf('{');
            return brace < 0 ? FieldPanel : FieldPanel[..brace];
        }
    }
}

// Dialog variables carrying text. Panel id and variable name are separate
// because `SetDialogVariableString` takes both and layouts are free to use
// one panel with several variables.
public sealed class LayoutVariables
{
    public string TitlePanel { get; set; } = "title";
    public string TitleVar { get; set; } = "title";
    public string FooterPanel { get; set; } = "footer";
    public string FooterVar { get; set; } = "footer";
    // Page counter, written only on a menu with more than one page; the panel
    // carries Classes.Hidden otherwise. An empty PageVar writes nothing.
    public string PagePanel { get; set; } = "page";
    public string PageVar { get; set; } = "page";
    // {0} = current page, {1} = page count.
    public string PageFormat { get; set; } = "{0} / {1}";
    // Row layouts: the parent menu's title (the page Back returns to), and the header's
    // `chip` nodes joined into one line. An empty variable writes nothing.
    public string CrumbPanel { get; set; } = "title";
    public string CrumbVar { get; set; } = "";
    public string InfoPanel { get; set; } = "title";
    public string InfoVar { get; set; } = "";
}

// How one body row is addressed. `{0}` is the row index.
public sealed class LayoutRowSpec
{
    // Panel receiving hidden / selected / disabled classes. Also the id the
    // click event reports, which is how a click maps back to a row index.
    public string PanelId { get; set; } = "option_{0}";
    public string LabelVar { get; set; } = "option_{0}";
    // Right-hand value, fed by text after the first tab. Null-safe: when the
    // layout has no such slot both halves stay on the label rather than the
    // value being silently dropped.
    public string? ValueVar { get; set; } = "option_{0}_right";
    // Number hint. Only meaningful in numbers mode, blank otherwise.
    public string? KeyVar { get; set; } = "option_{0}_key";
    // Panel carrying a `lvl-N` badge class, fed by text after the second tab.
    public string? IconPanel { get; set; } = "icon_{0}";
    // Panel receiving an econ thumbnail class from an `image=` raw directive.
    // Null by default: only layouts that include econ_thumbs.vcss can show one.
    public string? ThumbPanel { get; set; }
    // Small caption fed by a `type=` raw directive ("AK-47", "CT Agent").
    public string? TypeVar { get; set; }

    // Page layouts only (MenuLayoutProfile.Page); null where a layout has no such slot.
    // With HitSuffix the row is a Panel and "{PanelId}{HitSuffix}" the Button a click
    // reports, so an inline entry's Button ("{PanelId}{InlineSuffix}") can sit beside it.
    public string? HitSuffix { get; set; }
    public string? InlineSuffix { get; set; }
    // `sub=`, `unit=`, `badge=`, `tag=`, `tag2=` and `note=` text, and the mark an `avatar` row shows.
    public string? SubVar { get; set; }
    public string? UnitVar { get; set; }
    public string? BadgeVar { get; set; }
    public string? TagVar { get; set; }
    public string? Tag2Var { get; set; }
    public string? NoteVar { get; set; }
    public string? MarkVar { get; set; }
    // Receives the `glyph=` token's icon class (customhud_icons.vcss).
    public string? GlyphPanel { get; set; }
    // Receives the same icon class, inside the value's own group: a price keeps its
    // currency icon beside it however wide the number is. The view shows one of the two.
    public string? ValueGlyphPanel { get; set; }
    // Receives the same econ class as ThumbPanel, so a sheet can draw a second
    // picture for one key (a map's logo beside its screenshot).
    public string? LogoPanel { get; set; }
    // `tagglyph=`: an icon inside the tag's pill (Leading's trophy).
    public string? TagGlyphPanel { get; set; }
    // `vunit=`: a small unit after the value ("45" and "%").
    public string? ValueUnitVar { get; set; }
    // `rating=17450`: "17" and ",450", drawn the way CS2 draws a CS Rating.
    public string? RatingVar { get; set; }
    public string? RatingMinorVar { get; set; }
    // Mouse list: Buttons stepping a slider or selector ({PanelId}{DecSuffix}, {IncSuffix})
    // and Segments Buttons across a slider's track ({PanelId}{SegmentSuffix}{n}) setting it.
    public string? DecSuffix { get; set; }
    public string? IncSuffix { get; set; }
    public string? SegmentSuffix { get; set; }
    public int Segments { get; set; }
    // Page layouts: ActSlots Buttons over the row's right end ({PanelId}{ActSuffix}{n}, its icon
    // "{...}_glyph"), one per `act=`, each sending the menu action it names to the row.
    public string? ActSuffix { get; set; }
    public int ActSlots { get; set; }

    public string Panel(int i) => string.Format(PanelId, i);
    public string Label(int i) => string.Format(LabelVar, i);
    public string? Value(int i) => ValueVar is null ? null : string.Format(ValueVar, i);
    public string? Key(int i) => KeyVar is null ? null : string.Format(KeyVar, i);
    public string? Icon(int i) => IconPanel is null ? null : string.Format(IconPanel, i);
    public string? Thumb(int i) => ThumbPanel is null ? null : string.Format(ThumbPanel, i);
    public string? Type(int i) => TypeVar is null ? null : string.Format(TypeVar, i);
    public string? Sub(int i) => SubVar is null ? null : string.Format(SubVar, i);
    public string? Unit(int i) => UnitVar is null ? null : string.Format(UnitVar, i);
    public string? Badge(int i) => BadgeVar is null ? null : string.Format(BadgeVar, i);
    public string? Tag(int i) => TagVar is null ? null : string.Format(TagVar, i);
    public string? Tag2(int i) => Tag2Var is null ? null : string.Format(Tag2Var, i);
    public string? Note(int i) => NoteVar is null ? null : string.Format(NoteVar, i);
    public string? Mark(int i) => MarkVar is null ? null : string.Format(MarkVar, i);
    public string? Glyph(int i) => GlyphPanel is null ? null : string.Format(GlyphPanel, i);
    public string? ValueGlyph(int i) => ValueGlyphPanel is null ? null : string.Format(ValueGlyphPanel, i);
    public string? Logo(int i) => LogoPanel is null ? null : string.Format(LogoPanel, i);
    public string? TagGlyph(int i) => TagGlyphPanel is null ? null : string.Format(TagGlyphPanel, i);
    public string? ValueUnit(int i) => ValueUnitVar is null ? null : string.Format(ValueUnitVar, i);
    public string? Rating(int i) => RatingVar is null ? null : string.Format(RatingVar, i);
    public string? RatingMinor(int i) => RatingMinorVar is null ? null : string.Format(RatingMinorVar, i);
    public string Hit(int i) => HitSuffix is null ? Panel(i) : Panel(i) + HitSuffix;
    public string? Inline(int i) => InlineSuffix is null ? null : Panel(i) + InlineSuffix;
    public string? Dec(int i) => DecSuffix is null ? null : Panel(i) + DecSuffix;
    public string? Inc(int i) => IncSuffix is null ? null : Panel(i) + IncSuffix;
    public string? Segment(int i, int n) => SegmentSuffix is null ? null : Panel(i) + SegmentSuffix + n;
    public string? Act(int i, int n) => ActSuffix is null ? null : Panel(i) + ActSuffix + n;

    // Text before `{0}`, used to map a clicked panel id back to a row index.
    public string PanelPrefix
    {
        get
        {
            var brace = PanelId.IndexOf('{');
            return brace < 0 ? PanelId : PanelId[..brace];
        }
    }
}

// CSS class names toggled on panels. Composite entries take `{0}`.
public sealed class LayoutClasses
{
    public string Hidden { get; set; } = "hidden";
    public string Selected { get; set; } = "selected";
    public string Disabled { get; set; } = "disabled";
    // Applied when a row's text carries the danger marker.
    public string Danger { get; set; } = "danger";
    // Row-count ladder on the body panel, driving panel height. Only the
    // current count is written; the diff clears the previous one.
    public string RowCount { get; set; } = "hk-rows-{0}";
    // UI scale ladder on the shell, `{0}` = percent.
    public string Scale { get; set; } = "hk-scale-{0}";
    // Marquee applied to a row whose text is a scrolling-overflow component.
    // Panorama animates it in CSS so the server never ticks the animation.
    public string Marquee { get; set; } = "marquee";
    // Badge ladder on the row icon panel, `{0}` = token.
    public string Icon { get; set; } = "lvl-{0}";
    // Tokens the icon ladder accepts. A token outside this set collapses the
    // badge rather than rendering an empty hole.
    public List<string> IconTokens { get; set; } = new()
        { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10" };
    // Econ thumbnail on the thumb panel, `{0}` = the first 12 hex digits of
    // the image key's SHA-256. Must match scripts/generate-econ-thumbs.ps1.
    public string Thumb { get; set; } = "econ-{0}";
    // `tint=<token>` beside an image, on the same panel: a wash for art the game colours at
    // runtime, such as graffiti. `{0}` = the token.
    public string Tint { get; set; } = "tint-{0}";
    // On the row panel while its thumb panel shows an image.
    public string HasThumb { get; set; } = "has-thumb";
    // On the row panel while its type label is non-empty, so a layout can move
    // the name into the type's slot instead of leaving a gap.
    public string HasType { get; set; } = "has-type";
    // On a row slot drawn as a PageTiles card rather than a menu entry.
    public string PageTile { get; set; } = "page-tile";
    // A row carrying `bar=N` (a map's share of a vote): HasBar, plus the step
    // Bar with `{0}` = N, 0 .. 100, for the layout's meter.
    public string HasBar { get; set; } = "has-bar";
    public string Bar { get; set; } = "bar-{0}";
    // A row carrying `chosen`: the player's own pick, such as their vote in a tally.
    public string Chosen { get; set; } = "chosen";
    // On Panels.Shell: a row layout's view (MenuLayoutProfile.Views), `{0}` = its name.
    public string View { get; set; } = "view-{0}";
    // On Panels.Shell for a few ticks when a scrolling layout's list is replaced: the layout's CSS
    // shortens its scroll content so the offset clamps to the top while the new rows already show.
    // Empty, as for a layout that does not style it: an empty list is shown for a tick instead.
    public string Rewind { get; set; } = "";
    // Scale steps offered by !hudscale, and the only values the ladder accepts.
    public List<int> ScaleSteps { get; set; } = new() { 80, 90, 100, 110, 125, 150 };
}

// Preferred menu navigation input for a player.
//
// `Buttons` matches core's own vocabulary -- MenuInputMethod.Buttons, and
// `"InputMethod": "buttons"` in menus.jsonc -- so one word means the same thing
// in both config files. Core's keyboard dispatch serves it; which keys are
// bound is a menus.jsonc concern, not ours.
//
// `Numbers` and `Mouse` have no core equivalent and are driven by this plugin.
public enum NavMode
{
    Wasd,
    Buttons,
    Numbers,
    Mouse,
}
