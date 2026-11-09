namespace CustomHUD.Config;

// Bound to the "CustomHUD" section of config.jsonc and reloaded live.
//
// Everything a layout can be addressed by is a value in here. A server owner
// swaps in their own Panorama layout -- different art, same function -- by
// pointing a profile at another file and naming its panels, variables and
// classes. Nothing about panel naming is hardcoded; the defaults below merely
// reproduce the layouts this plugin ships.
public sealed class CustomHudConfig
{
    // Id this plugin registers its renderer under, and the value server owners
    // put in menus.jsonc. Configurable so two builds can coexist on one server
    // during a layout migration.
    public string RendererId { get; set; } = DefaultRendererId;

    // Consumers hardcode this id in their raw nodes, so it stays accepted even
    // after RendererId is renamed.
    public const string DefaultRendererId = "customhud.panorama";

    // Logs every compose / diff / apply / input decision at Information with a
    // [CustomHUD] prefix. Off by default -- prod servers stay quiet.
    public bool DetailedLogging { get; set; } = false;

    // Nav mode for players who have never chosen one, when Menus.InputMethod in menus.jsonc names
    // none. "wasd" | "buttons" | "numbers" | "mouse"; anything else falls back to "buttons".
    public string DefaultNavMode { get; set; } = "buttons";

    // Footer hint overrides per nav mode, plus "chat" for menus whose input method
    // is Chat. Replaces core's footer outright. {up} {down} {select} {close} expand
    // to the menu's keymap. An entry applies in every language; unset keys come
    // from the player's translation file (customhud.hint.<key>). Empty for the
    // same reason as MouseLayouts: a shipped entry would pin this release's text.
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

    // Prefix every profile's File is resolved against, unless the File value
    // already contains a '/'. Keeps the common case to a bare filename. CS2
    // refuses addon layouts outside panorama/layout/custom_game/.
    public string LayoutRoot { get; set; } = "panorama/layout/custom_game/customhud/";

    // Layout profile per nav mode. A missing mode falls back to Default; with
    // Default unset too, a menu draws in the built-in list (ListProfiles), which a
    // release can improve because nothing of it is written into config.jsonc.
    // Set Default to `{}` for the classic menu.vxml list.
    public MenuLayoutProfile? Default { get; set; }
    public MenuLayoutProfile? Wasd { get; set; }
    public MenuLayoutProfile? Buttons { get; set; }
    public MenuLayoutProfile? Numbers { get; set; }
    // Unset, Mouse players get the built-in mouse list: a keyboard layout would
    // leave them no visible way out, since their Tab is filtered. The tile grid
    // is MenuLayoutProfile.MouseGrid(), menu_mouse.vxml.
    public MenuLayoutProfile? Mouse { get; set; }

    // Per-menu-id overrides, keyed by the id a plugin passed to CreateMenu
    // ("mapchooser.vote"). Lets one menu use a wider layout without affecting
    // the rest. Matched case-insensitively; falls through to the nav-mode
    // profile when absent.
    public Dictionary<string, MenuLayoutProfile> Overrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Mouse-only layouts an operator adds; they win over BuiltInMouseLayouts and
    // Overrides. A trailing * matches a menu-id prefix, the longest pattern winning.
    // Empty by default: SW2 never back-fills into a dictionary, so a shipped rule
    // written here would pin that release's layout forever.
    public Dictionary<string, MenuLayoutProfile> MouseLayouts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Which design a page-locker menu draws as, by menu id or trailing-* prefix
    // ("mapchooser.eof-vote*": "maps-rows"). Wins over a profile's own Views, so a
    // shipped variant is one line here instead of a copied MouseLayouts rule.
    public Dictionary<string, string> PageViews { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Static, so neither the config binder nor SW2's writer ever sees it.
    public static IReadOnlyDictionary<string, MenuLayoutProfile> BuiltInMouseLayouts { get; } =
        new Dictionary<string, MenuLayoutProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["weaponskins.*"] = MenuLayoutProfile.WeaponSkinsLocker(),
            // menu_locker.vxml: one layout, a profile per plugin (LockerProfiles).
            ["shopcore.*"] = LockerProfiles.Shop(),
            ["mapchooser.*"] = LockerProfiles.Maps(),
            ["ranks.*"] = LockerProfiles.Ranks(),
            // Ranks' modules open under its Statistics with ids of their own; without these
            // the page leaves the locker for the mouse list, and back again, mid-navigation.
            ["weaponstats.*"] = LockerProfiles.Ranks(),
            ["customcombos.*"] = LockerProfiles.Ranks(),
            ["prestige.*"] = LockerProfiles.Ranks(),
            ["vipcore.*"] = LockerProfiles.Vip(),
            ["admins.*"] = LockerProfiles.Admins(),
            ["admins.vote"] = LockerProfiles.AdminsBallot(),
            ["mixscrims.*"] = LockerProfiles.Mix(),
        };

    // The keyboard list with a plugin's eyebrow and art (ListProfiles.Plugins), used
    // while no Wasd / Buttons / Numbers / Default profile is set.
    public static IReadOnlyDictionary<string, MenuLayoutProfile> BuiltInListLayouts { get; } = ListProfiles.Plugins();

    public static MenuLayoutProfile BuiltInList { get; } = ListProfiles.Keys();
    public static MenuLayoutProfile BuiltInMouseList { get; } = ListProfiles.Mouse();

    // Capture of native CS2 HUD text (center / alert / notify ...) so it is
    // drawn by CustomHUD instead of the game's own HUD. Off by default --
    // swallowing another plugin's messages is never a safe default.
    public NativeTextConfig NativeText { get; set; } = new();

    // Panels other plugins draw through the CustomHUD.Panels.v1 shared
    // interface, keyed by the profile name they pass. Config entries merge
    // over the defaults, so a server only lists the ones it restyles.
    public Dictionary<string, PanelProfile> Panels { get; set; } = PanelProfile.Defaults();

    public string ResolvePanelPath(PanelProfile profile)
        => profile.File.Contains('/') ? profile.File : LayoutRoot + profile.File;

    public MenuLayoutProfile Resolve(string menuId, NavMode mode)
    {
        if (mode == NavMode.Mouse)
            return Match(MouseLayouts, menuId) ?? Match(BuiltInMouseLayouts, menuId)
                ?? (Overrides.TryGetValue(menuId, out var mouseById) ? mouseById : null)
                ?? Mouse ?? BuiltInMouseList;
        if (Overrides.TryGetValue(menuId, out var byId)) return byId;
        return (mode switch
        {
            NavMode.Wasd => Wasd,
            NavMode.Buttons => Buttons,
            NavMode.Numbers => Numbers,
            _ => null,
        }) ?? Default ?? Match(BuiltInListLayouts, menuId) ?? BuiltInList;
    }

    // The scale steps !hudscale offers: those of the keyboard profile every menu falls back to.
    // A method, not a property: the binder would append config.jsonc's values onto a getter's list.
    public List<int> ScaleSteps() => (Default ?? BuiltInList).Classes.ScaleSteps;

    private static MenuLayoutProfile? Match(IReadOnlyDictionary<string, MenuLayoutProfile> rules, string menuId)
    {
        if (rules.TryGetValue(menuId, out var exact)) return exact;
        MenuLayoutProfile? match = null;
        var longest = -1;
        foreach (var (pattern, profile) in rules)
            if (pattern.EndsWith('*') && pattern.Length > longest
                && menuId.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase))
            {
                match = profile;
                longest = pattern.Length;
            }
        return match;
    }

    public string ResolveLayoutPath(MenuLayoutProfile profile)
        => profile.File.Contains('/') ? profile.File : LayoutRoot + profile.File;
}

// One Panorama layout and the vocabulary needed to address it.
public sealed class MenuLayoutProfile
{
    // Compiled layout Panorama loads at runtime. The `_c` suffix is not
    // optional -- the engine resolves the compiled artifact, not the source.
    public string File { get; set; } = "menu.vxml_c";

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
        Classes = new() { RowCount = "hk-tiles-{0}" },
    };

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

    public string Field(int i) => string.Format(FieldPanel, i);
    public string Label(int i) => string.Format(LabelVar, i);
    public string Value(int i) => string.Format(ValueVar, i);

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
