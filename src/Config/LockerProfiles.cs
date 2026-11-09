namespace CustomHUD.Config;

// The profiles behind menu_locker.vxml, one per plugin it is built in for. All
// share one layout and differ only in sidebar icons, view rules and a few page
// options, so a plugin without a rule of its own can point at Generic().
// scripts/generate-locker-layout.ps1 declares exactly these panels.
public static class LockerProfiles
{
    public const string File = "menu_locker.vxml_c";
    public const int MainSlots = 48;
    public const string MainParts = "icon thumb logo mark swatch glyph rating tags tagglyph note value vunit switch chev meter meter2 inline";

    // Name, slot count, and the parts its slots carry; generate-locker-layout.ps1 holds the same table.
    public static readonly (string Name, int Slots, string Parts)[] Regions =
    [
        ("banner", 5, "icon thumb logo mark glyph rating tags note value meter pips"),   // a hero, a membership card, a podium, steps
        ("detail", 28, "icon thumb mark glyph tags note value switch chev meter inline"), // a selected row's page, nominations, a summary
        ("detailbar", 3, "glyph value"),                                                 // the detail column's buttons
        ("foot", 4, "thumb mark glyph tags note value meter pips inline"),               // turnout, a loadout bar, page buttons
        ("aside", 8, "icon mark glyph rating tags note value"),                          // a column left of the list: a team's roster
        ("strip", 10, "mark glyph note value"),                                          // a row of small slots over the page: a pick order
    ];

    // Stock maps with a 360p screenshot and a logo in the game (locker_art.vcss).
    public static readonly string[] StockMaps =
    [
        "ar_baggage", "ar_pool_day", "ar_shoots", "ar_shoots_night", "cs_italy", "cs_office", "cs_shelter",
        "de_ancient", "de_ancient_night", "de_anubis", "de_boulder", "de_cache", "de_debris", "de_dust", "de_dust2",
        "de_eldorado", "de_fachwerk", "de_inferno", "de_mirage", "de_nuke", "de_overpass", "de_poseidon", "de_train",
        "de_vertigo",
    ];

    // Swatch, switch, meter and pips are drawn from classes alone, so only the
    // parts that name a panel or a variable change the spec.
    public static LayoutRowSpec Row(string prefix, string parts)
    {
        var has = parts.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        string? If(string part, string name) => has.Contains(part) ? prefix + name : null;
        return new()
        {
            PanelId = prefix + "{0}",
            LabelVar = prefix + "{0}",
            ValueVar = If("value", "{0}_value"),
            KeyVar = null,
            IconPanel = If("icon", "{0}_icon"),
            ThumbPanel = If("thumb", "{0}_thumb"),
            TypeVar = prefix + "{0}_type",
            HitSuffix = "_hit",
            InlineSuffix = has.Contains("inline") ? "_inline" : null,
            SubVar = prefix + "{0}_sub",
            UnitVar = prefix + "{0}_unit",
            BadgeVar = prefix + "{0}_badge",
            TagVar = If("tags", "{0}_tag"),
            Tag2Var = If("tags", "{0}_tag2"),
            NoteVar = If("note", "{0}_note"),
            MarkVar = If("mark", "{0}_mark"),
            GlyphPanel = If("glyph", "{0}_glyph"),
            ValueGlyphPanel = has.Contains("glyph") && has.Contains("value") ? prefix + "{0}_vglyph" : null,
            LogoPanel = If("logo", "{0}_logo"),
            TagGlyphPanel = has.Contains("tags") ? If("tagglyph", "{0}_tagglyph") : null,
            ValueUnitVar = has.Contains("value") ? If("vunit", "{0}_vunit") : null,
            RatingVar = If("rating", "{0}_rating"),
            RatingMinorVar = If("rating", "{0}_rating_minor"),
        };
    }

    // Any plugin: a sidebar mirroring the root, tabs from the second level, the
    // list as rows. Views come from `view=` alone.
    public static MenuLayoutProfile Generic() => new()
    {
        File = File,
        RowSlots = MainSlots,
        ShowAllRows = true,
        Panels = new()
        {
            Shell = "shell", Body = "options", Title = "title", Footer = "footer",
            Back = "nav_back", Close = "nav_close", Previous = "nav_previous", Next = "nav_next",
        },
        Variables = new() { PagePanel = "page", PageVar = "page" },
        Row = Row("option_", MainParts),
        Sidebar = new() { Slots = 12, RootButton = "", AutoOpen = 0, CountVar = "category_count" },
        Page = new()
        {
            DefaultView = "list",
            Regions = [.. Regions.Select(r => new MenuRegionProfile { Name = r.Name, Slots = r.Slots, Row = Row(r.Name + "_", r.Parts) })],
        },
        ColorIcons = new(StringComparer.OrdinalIgnoreCase)
        {
            ["#b0c3d9"] = "1", ["#5e98d9"] = "2", ["#4b69ff"] = "3",
            ["#8847ff"] = "4", ["#d32ce6"] = "5", ["#eb4b4b"] = "6",
            ["#e4ae39"] = "7", ["#cf6a32"] = "8", ["#ffd700"] = "9",
        },
    };

    // ShopCore. Today's root holds Buy and Inventory, so the categories become
    // tabs; with categories on the root they are the sidebar and their
    // sub-categories the tabs. Entries carry no ids yet, so icons also match
    // the English labels, and an item without art takes its category's icon.
    public static MenuLayoutProfile Shop() => With(Generic(), "shop", new()
    {
        ["shopcore.buy.items"] = "shop-grid",
        ["shopcore.buy.item"] = "shop-product",
        ["shopcore.inventory.*"] = "shop-inventory",
    }, new()
    {
        ["shopcore.buy"] = "shop", ["shopcore.inventory"] = "inventory",
        ["Buy"] = "shop", ["Inventory"] = "inventory", ["Abilities"] = "star", ["Healings"] = "healing",
        ["Movement"] = "movement", ["Permissions"] = "key", ["Sounds"] = "sounds", ["VIP"] = "crown",
        ["Visuals"] = "colors", ["Weapons"] = "weapons", ["Knifes"] = "knives", ["Pistols"] = "guns",
        ["Rifles"] = "guns",
        // The stock modules' sub-categories (Movement/Bhop, Visuals/Tracers ...).
        ["Bhop"] = "bhop", ["Parachute"] = "parachute", ["Flags"] = "key", ["Hit Sounds"] = "sounds",
        ["Kill Screens"] = "kill", ["Player Colors"] = "colors", ["Smoke Colors"] = "colors",
        ["Tracers"] = "crosshair", ["Player Models"] = "agents", ["Memberships"] = "crown",
    });

    // MapChooser. Its hub (mapchooser.hub) is the sidebar root and each page opens as its
    // child; map names become art keys, workshop maps their prefix.
    public static MenuLayoutProfile Maps()
    {
        var profile = With(Generic(), "maps", new()
        {
            ["mapchooser.eof-vote*"] = "maps-board",
            ["mapchooser.votemap"] = "maps-board",
            ["mapchooser.admin-maps-vote"] = "maps-pool",
            ["mapchooser.nominate"] = "maps-pool",
            ["mapchooser.pool"] = "maps-pool",
            ["mapchooser.admin-change-map"] = "maps-pool",
            ["mapchooser.set-next-map"] = "maps-pool",
            ["mapchooser.rtv"] = "maps-rtv",
        }, new()
        {
            ["mapchooser.eof-vote"] = "vote", ["mapchooser.nominate"] = "add", ["mapchooser.rtv"] = "map-vote",
            ["mapchooser.pool"] = "maps", ["mapchooser.admin-change-map"] = "refresh",
            ["mapchooser.set-next-map"] = "clock", ["mapchooser.admin-maps-vote"] = "ticket", ["mapchooser.cycle"] = "timer",
            ["Vote"] = "vote", ["Nominate"] = "add", ["Rock the vote"] = "map-vote", ["Map pool"] = "maps",
            ["Change map"] = "refresh", ["Set next map"] = "clock", ["Start a vote"] = "ticket", ["Map cycle"] = "timer",
        });
        var page = profile.Page!;
        page.ImageFromLabel = "map/{0}";
        page.ImageLabels = [.. StockMaps];
        page.MarkFromLabel = true;
        page.AutoLead = true;
        page.LeadGlyph = "trophy";
        page.ColorTones = new(StringComparer.OrdinalIgnoreCase) { ["#ffa500"] = "extend" };
        return profile;
    }

    // Ranks. Its pages are drawn by the Ranks integration (Integrations/Ranks) from
    // the plugin's API; its modules keep their own ids (WeaponStats, CustomCombos,
    // Prestige) and open as Statistics' tabs.
    public static MenuLayoutProfile Ranks() => With(Generic(), "ranks", new()
    {
        ["ranks.myrank"] = "ranks-rank",
        ["ranks.ladder"] = "ranks-rank",
        ["ranks.stats*"] = "ranks-stats",
        ["ranks.top"] = "ranks-board",
        ["ranks.top.playtime"] = "ranks-board",
        ["ranks.top.player"] = "ranks-stats",
        ["ranks.settings*"] = "ranks-settings",
        ["weaponstats.weapons"] = "ranks-weapons",
        ["weaponstats.*"] = "ranks-stats",
        ["customcombos.*"] = "ranks-stats",
        ["prestige.*"] = "ranks-stats",
    }, new()
    {
        ["ranks.myrank"] = "rank", ["ranks.stats"] = "stats", ["ranks.top"] = "trophy", ["ranks.settings"] = "settings",
        ["My rank"] = "rank", ["Statistics"] = "stats", ["Leaderboard"] = "trophy", ["Settings"] = "settings",
        ["Help"] = "info", ["Admin commands"] = "shield",
    });

    // VIPCore. Its hub is the sidebar root, the users pages are tabs of one host, and each perk's
    // card takes its glyph from the feature key the VIP module registers.
    public static MenuLayoutProfile Vip() => With(Generic(), "vip", new()
    {
        ["vipcore.perks"] = "vip-perks",
        ["vipcore.customize"] = "vip-options",
        ["vipcore.gun-menu*"] = "vip-guns",
        ["vipcore.users*"] = "vip-users",
        ["vipcore.grant"] = "vip-grant",
    }, new()
    {
        ["vipcore.perks"] = "star", ["vipcore.customize"] = "sliders", ["vipcore.gun-menu"] = "guns",
        ["vipcore.users"] = "player", ["vipcore.grant"] = "add-player",
        ["Perks"] = "star", ["Customize"] = "sliders", ["Gun menu"] = "guns",
        ["VIP users"] = "player", ["Grant VIP"] = "add-player",
        ["vip.health"] = "health", ["vip.armor"] = "armor", ["vip.money"] = "money", ["vip.bhop"] = "bhop",
        ["vip.speed"] = "movement", ["vip.nofalldamage"] = "parachute", ["vip.antiflash"] = "flash",
        ["vip.zeus"] = "zeus", ["vip.vampirism"] = "healing", ["vip.gun_menu"] = "guns", ["vip.fov"] = "fov",
        ["vip.tag"] = "tag", ["vip.chatcolor"] = "chat", ["vip.doublejump"] = "bhop", ["vip.endurance"] = "shield",
        ["vip.fastdefuse"] = "defuse", ["vip.fastplant"] = "bomb", ["vip.fastreload"] = "refresh",
        ["vip.flags"] = "key", ["vip.items"] = "inventory", ["vip.killscreen"] = "kill",
        ["vip.rainbowmodel"] = "colors", ["vip.smokecolor"] = "colors", ["vip.round_end_abilities"] = "star",
    });

    // Admins. Its ballot (admins.vote) is not here: AdminsBallot is registered for it on its own.
    public static MenuLayoutProfile Admins() => With(Generic(), "admins", new()
    {
        ["admins.players*"] = "admins-players",
        ["admins.players.*"] = "admins-player",
        ["admins.bans.give*"] = "admins-ban",
        ["admins.comms.give*"] = "admins-ban",
        ["admins.kick.*"] = "admins-ban",
        ["admins.bans*"] = "admins-bans",
        ["admins.comms*"] = "admins-bans",
        ["admins.bans.record"] = "admins-record",
        ["admins.comms.record"] = "admins-record",
        ["admins.votes*"] = "admins-votes",
        ["admins.staff*"] = "admins-staff",
    }, new()
    {
        ["admins.players"] = "player", ["admins.bans"] = "ban", ["admins.comms"] = "mute", ["admins.votes"] = "vote",
        ["admins.tools"] = "sliders", ["admins.server"] = "server", ["admins.staff"] = "staff",
        ["Players"] = "player", ["Bans"] = "ban", ["Comms"] = "mute", ["Votes"] = "vote", ["My tools"] = "sliders",
        ["Server"] = "server", ["Staff"] = "staff",
    });

    // Admins' vote ballot, which everyone gets: a small panel docked at the side like Mix's, with
    // no sidebar (an admin interrupted mid-menu would otherwise get theirs around it). Its view is
    // the provider integration's pick by vote; a map vote reads its answers as maps.
    public static MenuLayoutProfile AdminsBallot()
    {
        var profile = With(Generic(), "admins", new() { ["admins.vote"] = "mix-ballot" }, new());
        profile.Sidebar = null;
        var page = profile.Page!;
        page.ImageFromLabel = "map/{0}";
        page.ImageLabels = [.. StockMaps];
        page.AutoLead = true;
        page.LeadGlyph = "trophy";
        return profile;
    }

    // MixScrims. Every menu is its own root: the control page's submenus are tiles, so there
    // is no sidebar to mirror them (it would auto-open the first). The ballots everyone gets
    // mid-round draw as a small panel docked at the side; its map vote is MapChooser's board,
    // with stock-map art but no marks, since a player name is map-shaped too (entry_king).
    public static MenuLayoutProfile Mix()
    {
        var profile = With(Generic(), "mix", new()
        {
            ["mixscrims.captain-pick"] = "mix-captains",
            ["mixscrims.team-pick"] = "mix-draft",
            ["mixscrims.knife-side-pick"] = "mix-sides",
            ["mixscrims.timeout-vote"] = "mix-ballot",
            ["mixscrims.surrender-vote"] = "mix-ballot",
            ["mixscrims.vote-kick"] = "mix-ballot",
            ["mixscrims.control*"] = "mix-control",
            ["mixscrims.map-vote"] = "maps-board",
        }, new());
        profile.Sidebar = null;
        var page = profile.Page!;
        page.ImageFromLabel = "map/{0}";
        page.ImageLabels = [.. StockMaps];
        page.AutoLead = true;
        page.LeadGlyph = "trophy";
        return profile;
    }

    private static MenuLayoutProfile With(
        MenuLayoutProfile profile, string eyebrow, Dictionary<string, string> views, Dictionary<string, string> icons)
    {
        profile.Page!.Eyebrow = eyebrow;
        foreach (var (pattern, view) in views) profile.Page.Views[pattern] = view;
        foreach (var (entry, token) in icons) profile.Sidebar!.Icons[entry] = token;
        return profile;
    }
}
