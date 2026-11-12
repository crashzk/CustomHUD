namespace CustomHUD.Config;

// The layouts config.jsonc names, each by its file without .vxml_c, and the profile each draws with
// for each plugin. Every profile is built once: FrameComposer fingerprints a profile per instance,
// and none is edited after. A name serves one mode: a keyboard layout would leave a mouse player no
// visible way out, since their Tab is filtered.
public static class BuiltInLayouts
{
    // A message kind left to the game's own HUD.
    public const string Game = "game";
    public const string Other = "Other";

    // Supported plugins by menu id. Ranks' modules keep ids of their own but open under its
    // Statistics; without them the page would leave the locker for the mouse list, and back again.
    private static readonly (string Plugin, string[] Prefixes)[] Plugins =
    [
        ("WeaponSkins", ["weaponskins."]),
        ("ShopCore", ["shopcore."]),
        ("MapChooser", ["mapchooser."]),
        ("Ranks", ["ranks.", "weaponstats.", "customcombos.", "prestige."]),
        ("VIPCore", ["vipcore."]),
        ("Admins", ["admins."]),
        ("MixScrims", ["mixscrims."]),
    ];

    public static IEnumerable<string> PluginNames() => Plugins.Select(p => p.Plugin).Append(Other);

    public static string PluginOf(string menuId)
    {
        foreach (var (plugin, prefixes) in Plugins)
            if (prefixes.Any(prefix => menuId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                return plugin;
        return Other;
    }

    // The same profile for every plugin.
    private static readonly Dictionary<string, MenuLayoutProfile> MouseLayouts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["menu_list_mouse_v2"] = Named(ListProfiles.Mouse(), "menu_list_mouse_v2"),
        ["menu_list_mouse"] = Named(ListProfiles.Mouse(), "menu_list_mouse"),
        ["menu_mouse"] = MenuLayoutProfile.MouseGrid(),
    };

    // menu.vxml, and its variants that add a stylesheet of their own.
    private static readonly Dictionary<string, MenuLayoutProfile> KeyboardLayouts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["menu"] = new(),
        ["menu_shop"] = new() { File = "menu_shop.vxml_c" },
        ["menu_skins"] = new() { File = "menu_skins.vxml_c" },
        ["menu_teampick"] = new() { File = "menu_teampick.vxml_c" },
    };

    // The keyboard lists, by plugin: its eyebrow and art (ListProfiles.Plugins) in any of the four
    // files, which declare the same panels. The skins files add sticker art.
    private static readonly Dictionary<string, Dictionary<string, MenuLayoutProfile>> KeyboardLists = new(StringComparer.OrdinalIgnoreCase)
    {
        ["menu_list_v2"] = Lists("menu_list_v2"),
        ["menu_list_skins_v2"] = Lists("menu_list_skins_v2"),
        ["menu_list"] = Lists("menu_list"),
        ["menu_list_skins"] = Lists("menu_list_skins"),
    };

    // The designed pages, by plugin, then by menu id: exact id first, then the longest trailing-* prefix.
    private static readonly Dictionary<string, Dictionary<string, Dictionary<string, MenuLayoutProfile>>> Lockers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["menu_locker_v2"] = PageLockers("menu_locker_v2"),
        ["menu_locker"] = PageLockers("menu_locker"),
        ["menu_skins_mouse_v2"] = new(StringComparer.OrdinalIgnoreCase) { ["WeaponSkins"] = Pages(MenuLayoutProfile.WeaponSkinsLockerV2()) },
        ["menu_skins_mouse"] = new(StringComparer.OrdinalIgnoreCase) { ["WeaponSkins"] = Pages(MenuLayoutProfile.WeaponSkinsLocker()) },
    };

    private static readonly MenuChoices Defaults = new();

    // Null when the name is no built-in layout for this plugin in this mode.
    public static MenuLayoutProfile? Menu(string name, string plugin, string menuId, bool mouse)
    {
        name = Bare(name);
        if (mouse)
            return MouseLayouts.GetValueOrDefault(name)
                ?? (Lockers.TryGetValue(name, out var plugins) && plugins.TryGetValue(plugin, out var pages)
                    ? CustomHudConfig.Match(pages, menuId) : null);
        return KeyboardLayouts.GetValueOrDefault(name)
            ?? (KeyboardLists.TryGetValue(name, out var lists) ? lists.GetValueOrDefault(plugin) : null);
    }

    // What a plugin's menus draw in while config.jsonc names nothing they can draw in.
    public static string DefaultMenu(string plugin, bool mouse) => Defaults.For(plugin).For(mouse)!;

    public static bool IsMenu(string name)
    {
        name = Bare(name);
        return MouseLayouts.ContainsKey(name) || KeyboardLayouts.ContainsKey(name)
            || KeyboardLists.ContainsKey(name) || Lockers.ContainsKey(name);
    }

    // A name copied from a file listing keeps its extension.
    public static string Bare(string name)
    {
        foreach (var extension in new[] { ".vxml_c", ".vxml" })
            if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                return name[..^extension.Length];
        return name;
    }

    public static IReadOnlyDictionary<string, NativeTextProfile> Messages { get; } = NativeTextProfile.Defaults();
    public static IReadOnlyDictionary<string, PanelProfile> Panels { get; } = PanelProfile.Defaults();

    private static Dictionary<string, MenuLayoutProfile> Lists(string name)
    {
        var lists = ListProfiles.Plugins();
        lists[Other] = ListProfiles.Keys();
        foreach (var profile in lists.Values) Named(profile, name);
        return lists;
    }

    // menu_locker_v2.vxml and menu_locker.vxml: one layout, a profile per plugin (LockerProfiles).
    private static Dictionary<string, Dictionary<string, MenuLayoutProfile>> PageLockers(string name) => new(StringComparer.OrdinalIgnoreCase)
    {
        ["ShopCore"] = Pages(Named(LockerProfiles.Shop(), name)),
        ["MapChooser"] = Pages(Named(LockerProfiles.Maps(), name)),
        ["Ranks"] = Pages(Named(LockerProfiles.Ranks(), name)),
        ["VIPCore"] = Pages(Named(LockerProfiles.Vip(), name)),
        ["Admins"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["*"] = Named(LockerProfiles.Admins(), name),
            ["admins.vote"] = Named(LockerProfiles.AdminsBallot(), name),
        },
        ["MixScrims"] = Pages(Named(LockerProfiles.Mix(), name)),
    };

    private static Dictionary<string, MenuLayoutProfile> Pages(MenuLayoutProfile profile)
        => new(StringComparer.OrdinalIgnoreCase) { ["*"] = profile };

    private static MenuLayoutProfile Named(MenuLayoutProfile profile, string name)
    {
        profile.File = name + ".vxml_c";
        return profile;
    }
}
