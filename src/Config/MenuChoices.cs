using System.ComponentModel;

namespace CustomHUD.Config;

// The layout each plugin's menus draw in, by name (BuiltInLayouts, or the operator's MenuLayouts).
// A class rather than a dictionary: SW2 back-fills a property a later release adds, never a key.
public sealed class MenuChoices
{
    public MenuChoice WeaponSkins { get; set; } = new() { Mouse = "menu_skins_mouse_v2", Keyboard = "menu_list_skins_v2" };
    public MenuChoice ShopCore { get; set; } = MenuChoice.Locker();
    public MenuChoice MapChooser { get; set; } = MenuChoice.Locker();
    // Ranks_WeaponStats, Ranks_CustomCombos and Ranks_Prestige follow it: they open under its Statistics.
    public MenuChoice Ranks { get; set; } = MenuChoice.Locker();
    public MenuChoice VIPCore { get; set; } = MenuChoice.Locker();
    public MenuChoice Admins { get; set; } = MenuChoice.Locker();
    public MenuChoice MixScrims { get; set; } = MenuChoice.Locker();

    [Description("Every other plugin.")]
    public MenuChoice Other { get; set; } = new() { Mouse = "menu_list_mouse_v2", Keyboard = "menu_list_v2" };

    public MenuChoice For(string plugin) => plugin switch
    {
        nameof(WeaponSkins) => WeaponSkins,
        nameof(ShopCore) => ShopCore,
        nameof(MapChooser) => MapChooser,
        nameof(Ranks) => Ranks,
        nameof(VIPCore) => VIPCore,
        nameof(Admins) => Admins,
        nameof(MixScrims) => MixScrims,
        _ => Other,
    };
}

// Keyboard is every keyboard mode: buttons, wasd and numbers. An empty name passes the mode on: a
// MenuOverrides entry's to its plugin's choice, a plugin's to its built-in default.
public sealed class MenuChoice
{
    public string Mouse { get; set; } = "";
    public string Keyboard { get; set; } = "";

    public static MenuChoice Locker() => new() { Mouse = "menu_locker_v2", Keyboard = "menu_list_v2" };

    public string? For(bool mouse) => (mouse ? Mouse : Keyboard)?.Trim() is { Length: > 0 } name ? name : null;
}
