namespace CustomHUD.Config;

// The list every menu without a page of its own draws in: menu_list.vxml in the
// keyboard modes, menu_list_mouse.vxml in Mouse mode. A page profile with no
// sidebar, tabs or side panes, so its rows read the same directives the page
// lockers do and a plugin that styles its mouse page styles its list too.
// scripts/generate-list-layout.ps1 declares exactly these panels.
public static class ListProfiles
{
    public const string KeysFile = "menu_list.vxml_c";
    public const string MouseFile = "menu_list_mouse.vxml_c";
    // The keyboard list plus econ_stickers.vcss, which no other list loads: sticker art is most of all econ classes.
    public const string SkinsFile = "menu_list_skins.vxml_c";

    // A keyboard page never holds more (core caps MaxVisibleLines at 32, numbers mode
    // at six keys); the mouse list scrolls through a chunk this long and pages past it.
    public const int KeySlots = 20;
    public const int MouseSlots = 24;
    // A click on a slider's track lands on one of these, 5% apart.
    public const int Segments = 21;

    // Every row part (LockerProfiles.Row names them); "key" adds the number, "steps"
    // the ‹ › Buttons beside a slider's or selector's value, "segs" its track's segments.
    public const string KeyParts = "key icon thumb mark glyph rating tags note value";
    public const string MouseParts = "icon thumb mark glyph rating tags note value steps segs";

    // Below the rows: a ballot's turnout and the rule that passes it.
    public const int FootSlots = 3;
    public const string FootParts = "glyph tags note value pips";

    public static LayoutRowSpec Row(string prefix, string parts)
    {
        var spec = LockerProfiles.Row(prefix, parts);
        var has = parts.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        if (has.Contains("key")) spec.KeyVar = prefix + "{0}_key";
        if (has.Contains("steps"))
        {
            spec.DecSuffix = "_dec";
            spec.IncSuffix = "_inc";
        }
        if (has.Contains("segs"))
        {
            spec.SegmentSuffix = "_seg";
            spec.Segments = Segments;
        }
        return spec;
    }

    // Wasd, Buttons and Numbers: pages of core's size, a legend of keys.
    public static MenuLayoutProfile Keys(string eyebrow = "") => new()
    {
        File = KeysFile,
        RowSlots = KeySlots,
        Panels = new() { Back = "", Close = "", Previous = "", Next = "" },
        Variables = new() { PageFormat = "{0}/{1}" },
        Row = Row("option_", KeyParts),
        Page = Page(eyebrow, legend: true),
        ColorIcons = RarityColors(),
    };

    // Mouse: every row of a chunk in a scrolling list, < and × in the bar.
    public static MenuLayoutProfile Mouse() => new()
    {
        File = MouseFile,
        RowSlots = MouseSlots,
        ShowAllRows = true,
        Panels = new() { Back = "nav_back", Close = "nav_close", Previous = "nav_previous", Next = "nav_next" },
        Variables = new() { PageFormat = "{0}/{1}" },
        Row = Row("option_", MouseParts),
        Page = Page("", legend: false),
        ColorIcons = RarityColors(),
    };

    // What a plugin's page profile knows that its keyboard list should too: the
    // eyebrow over a menu that is its own root, and MapChooser's map art and leader.
    public static Dictionary<string, MenuLayoutProfile> Plugins() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["shopcore.*"] = Keys("shop"),
        ["mapchooser.*"] = Maps(),
        ["ranks.*"] = Ranks(),
        ["weaponstats.*"] = Ranks(),
        ["customcombos.*"] = Ranks(),
        ["prestige.*"] = Ranks(),
        ["vipcore.*"] = Keys("vip"),
        ["admins.*"] = Admins(),
        ["mixscrims.*"] = Mix(),
        ["weaponskins.*"] = Skins(),
    };

    public static MenuLayoutProfile Skins()
    {
        var profile = Keys();
        profile.File = SkinsFile;
        return profile;
    }

    // Ranks' pages caption meters only its mouse page draws (the hero's two thresholds, a
    // weapon card's shares); its list view drops the captions and reads units as words.
    public static MenuLayoutProfile Ranks()
    {
        var profile = Keys("ranks");
        profile.Page!.DefaultView = "ranks";
        return profile;
    }

    public static MenuLayoutProfile Maps()
    {
        var profile = Keys("maps");
        var list = profile.Page!;
        var maps = LockerProfiles.Maps().Page!;
        list.ImageFromLabel = maps.ImageFromLabel;
        list.ImageLabels = [.. maps.ImageLabels];
        list.MarkFromLabel = maps.MarkFromLabel;
        list.AutoLead = maps.AutoLead;
        list.ColorTones = new(maps.ColorTones, StringComparer.OrdinalIgnoreCase);
        return profile;
    }

    // MixScrims' map vote tallies shares like MapChooser's, with its stock-map art; its
    // mid-round votes are ballots (Yes and No as the two big rows, the turnout below).
    public static MenuLayoutProfile Mix()
    {
        var profile = Keys("mix");
        var list = profile.Page!;
        var mix = LockerProfiles.Mix().Page!;
        list.ImageFromLabel = mix.ImageFromLabel;
        list.ImageLabels = [.. mix.ImageLabels];
        list.AutoLead = mix.AutoLead;
        foreach (var id in new[] { "mixscrims.timeout-vote", "mixscrims.surrender-vote", "mixscrims.vote-kick" })
            list.Views[id] = "ballot";
        return profile;
    }

    // Admins' vote ballot reads like Mix's; a map vote's answers are maps.
    public static MenuLayoutProfile Admins()
    {
        var profile = Keys("admins");
        var list = profile.Page!;
        var ballot = LockerProfiles.AdminsBallot().Page!;
        list.ImageFromLabel = ballot.ImageFromLabel;
        list.ImageLabels = [.. ballot.ImageLabels];
        list.Views["admins.vote"] = "ballot";
        return profile;
    }

    private static MenuPageProfile Page(string eyebrow, bool legend) => new()
    {
        Eyebrow = eyebrow,
        TabSlots = 0,
        TabsPanel = "",
        HeadSlots = 0,
        DefaultView = "list",
        Regions = [new() { Name = "foot", Slots = FootSlots, Row = Row("foot_", FootParts) }],
        Legend = legend ? new MenuLegendProfile() : null,
    };

    private static Dictionary<string, string> RarityColors() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["#b0c3d9"] = "1", ["#5e98d9"] = "2", ["#4b69ff"] = "3",
        ["#8847ff"] = "4", ["#d32ce6"] = "5", ["#eb4b4b"] = "6",
        ["#e4ae39"] = "7", ["#cf6a32"] = "8", ["#ffd700"] = "9",
    };
}
