using System.Globalization;
using System.Runtime.CompilerServices;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Menu;
using SwiftlyS2.Shared.Menu.Components;
using WeaponSkins;
using WeaponSkins.Shared;

namespace CustomHUD.Integrations.SkinPages;

// WeaponSkins' locker: each row's render, its caption and a mark on what the player has on,
// all read from IWeaponSkinAPI. A row says what it stands for through its Tag: a weapon type's
// definition index (ushort), a finish's paint kit index (int), an agent / music kit / sticker /
// capsule / charm / graffiti / tint / patch / patch pack / pin definition, a charm collection's name
// or a weapon category's key, a graffiti tournament's event id or a sticker or patch slot (int). A
// page under a weapon (its finishes, its properties, its stickers) gets that weapon from the row it
// was opened from (Openers).
internal sealed class WeaponSkinsIntegration(IWeaponSkinAPI api) : IPageIntegration
{
    private Dictionary<int, string>? _weaponByIndex;

    // Newer than the members TryCreate requires. An older WeaponSkins.Shared lacks their Tag types
    // as well, so their pages are drawn only through NoInlining methods, and only when present.
    private readonly bool _graffiti = typeof(IWeaponSkinAPI).GetMember(nameof(IWeaponSkinAPI.TryGetGraffiti)).Length > 0;
    private readonly bool _patches = typeof(IWeaponSkinAPI).GetMember(nameof(IWeaponSkinAPI.TryGetAgentPatches)).Length > 0;
    private readonly bool _pins = typeof(IWeaponSkinAPI).GetMember(nameof(IWeaponSkinAPI.TryGetPin)).Length > 0;

    public string Provider => "WeaponSkins";

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IPageIntegration? TryCreate(IInterfaceManager interfaces)
    {
        ApiProbe.Require(typeof(IWeaponSkinAPI), "the WeaponSkins build with agents, music kits and image keys in its API",
            nameof(IWeaponSkinAPI.Agents), nameof(IWeaponSkinAPI.MusicKits), nameof(IWeaponSkinAPI.GetImageKey), nameof(IWeaponSkinAPI.TryGetMusicKit));
        return interfaces.TryGetSharedInterface<IWeaponSkinAPI>("WeaponSkins.API", out var api) && api is not null
            ? new WeaponSkinsIntegration(api)
            : null;
    }

    public bool Owns(IMenu menu) => menu.Id.StartsWith("weaponskins.", StringComparison.Ordinal);

    public string? RegionOf(IMenu menu, IMenuComponent component) => null;

    public void Draw(PageDraw page)
    {
        var id = page.Menu.Id;
        switch (id)
        {
            case "weaponskins.weapons":
                DrawCategories(page);
                // A WeaponSkins older than its categories lists the guns here.
                DrawTypes(page, id);
                GunChips(page, null);
                return;
            case "weaponskins.knifes":
            case "weaponskins.gloves":
                DrawTypes(page, id);
                return;
            case "weaponskins.weapons.paintkits":
            case "weaponskins.knifes.paintkits":
            case "weaponskins.gloves.paintkits":
                DrawFinishes(page, id[..id.LastIndexOf('.')]);
                return;
            case "weaponskins.agents":
                DrawAgents(page);
                return;
            case "weaponskins.musickits":
                DrawMusicKits(page);
                return;
            case "weaponskins.weaponproperties":
                DrawPropertiesList(page);
                return;
            case "weaponskins.skinproperties":
            case "weaponskins.knifeproperties":
            case "weaponskins.gloveproperties":
                DrawProperties(page, id);
                return;
            case "weaponskins.stickers":
                DrawStickerSlots(page);
                return;
            case "weaponskins.keychains":
            case "weaponskins.keychains.collection":
                DrawCharms(page);
                return;
            case "weaponskins.graffiti":
            case "weaponskins.graffiti.tournament":
                if (_graffiti) DrawGraffiti(page);
                return;
            case "weaponskins.graffiti.tints":
                if (_graffiti) DrawGraffitiTints(page);
                return;
            case "weaponskins.agentproperties":
                if (_patches) DrawPatchSlots(page);
                return;
            case "weaponskins.pins":
                if (_pins) DrawPins(page);
                return;
        }

        if (id.StartsWith("weaponskins.stickers.slot", StringComparison.Ordinal)) DrawStickers(page);
        else if (_patches && id.StartsWith("weaponskins.patches.slot", StringComparison.Ordinal)) DrawPatches(page);
        else if (id.StartsWith("weaponskins.weapons.", StringComparison.Ordinal))
        {
            DrawTypes(page, "weaponskins.weapons");
            GunChips(page, id["weaponskins.weapons.".Length..]);
        }
    }

    // Weapon Skins' categories, each shown by the player's own finish where it has one.
    private void DrawCategories(PageDraw page)
    {
        var (steamId, team) = Who(page);
        var guns = GunsByCategory();
        for (var k = 0; k < page.Components.Count; k++)
        {
            if (page.Components[k] is not MenuComponentBase { Tag: string category }
                || !guns.TryGetValue(category, out var list) || list.Count == 0)
                continue;

            var worn = list.Select(weapon => (weapon, finish: WornFinish(steamId, team, weapon))).ToList();
            var count = worn.Count(w => w.finish is not null);
            CategoryCovers.TryGetValue(category, out var usual);
            var cover = worn.FirstOrDefault(w => w.weapon == usual && w.finish is not null);
            if (cover.weapon is null) cover = worn.FirstOrDefault(w => w.finish is not null);
            if (cover.weapon is null) cover = (usual is not null && list.Contains(usual) ? usual : list[0], null);

            var name = ItemName(page, cover.weapon);
            var payload = cover.finish is { } finish
                ? Payload(FinishImage(cover.weapon, finish), $"{name} | {LocalName(page, finish.LocalizedNames, finish.Name)}", false, Rarity(finish))
                : Payload(BaseImage(cover.weapon), name, false);
            page.Decorate(k, payload + ";text=\t" + Counts(page, list.Count, count));
        }
    }

    // Keyed by WeaponSkins' category Tags (WeaponCategory, lower case).
    private static readonly Dictionary<string, string> CategoryCovers = new(StringComparer.Ordinal)
    {
        ["rifles"] = "weapon_ak47", ["pistols"] = "weapon_deagle", ["smgs"] = "weapon_p90", ["shotguns"] = "weapon_xm1014",
        ["snipers"] = "weapon_awp", ["heavy"] = "weapon_negev", ["other"] = "weapon_taser",
    };

    // Weapon, knife and glove types, each in the finish the player has on it. The knife or glove
    // worn is marked; a gun is not, since every skinned gun is worn.
    private void DrawTypes(PageDraw page, string id)
    {
        var (steamId, team) = Who(page);
        (int Index, int Paintkit)? equipped = id switch
        {
            "weaponskins.knifes" when api.TryGetKnifeSkin(steamId, team, out var knife) => (knife.DefinitionIndex, knife.Paintkit),
            "weaponskins.gloves" when api.TryGetGloveSkin(steamId, team, out var glove) => (glove.DefinitionIndex, glove.Paintkit),
            _ => null,
        };

        for (var k = 0; k < page.Components.Count; k++)
        {
            if (page.Components[k] is not MenuComponentBase { Tag: ushort index } || Weapon(index) is not { } weapon) continue;

            var finish = id == "weaponskins.weapons" ? WornFinish(steamId, team, weapon)
                : equipped is { } on && on.Index == index ? Finish(weapon, on.Paintkit) : null;
            if (finish is not null)
            {
                page.Decorate(k, Payload(FinishImage(weapon, finish), LocalName(page, finish.LocalizedNames, finish.Name),
                    equipped?.Index == index, Rarity(finish)));
                continue;
            }

            // CS2 ships no render of a bare glove type, so a glove borrows its rarest finish's.
            var image = id == "weaponskins.gloves"
                ? api.WeaponToPaintkits.TryGetValue(weapon, out var finishes) && finishes.MaxBy(p => p.Rarity.Id) is { } preview
                    ? FinishImage(weapon, preview)
                    : null
                : BaseImage(weapon);
            page.Decorate(k, Payload(image, null, equipped?.Index == index));
        }
    }

    // One weapon's finishes, the applied one marked; the provider's Reset as the weapon it restores,
    // and the finish worn now as a spotlight over the list.
    private void DrawFinishes(PageDraw page, string types)
    {
        if (!page.TryOpener<ushort>(out var index) || Weapon(index) is not { } weapon
            || !api.WeaponToPaintkits.TryGetValue(weapon, out var finishes))
            return;

        var (steamId, team) = Who(page);
        var worn = Worn(types, steamId, team, index);
        var glove = types == "weaponskins.gloves";
        var caption = ItemName(page, weapon);
        // A glove has no bare render, so its Reset stays a plain action.
        var stock = glove ? null : BaseImage(weapon);

        for (var k = 0; k < page.Components.Count; k++)
        {
            switch (page.Components[k])
            {
                case MenuComponentBase { Tag: int paintkit } when finishes.FirstOrDefault(p => p.Index == paintkit) is { } finish:
                    page.Decorate(k, Payload(FinishImage(weapon, finish), caption, worn?.Paintkit == paintkit));
                    break;
                case ButtonComponent { Tag: null } when stock is not null:
                    page.Decorate(k, Payload(stock, caption, worn is not { Paintkit: not 0 }));
                    break;
            }
        }

        page.AddHeader(T(page, finishes.Count == 1 ? "finishes.one" : "finishes.other", finishes.Count), "chip;glyph=" + (glove ? "gloves" : "tiles"));
        // Gun properties list every gun with a record; knife and glove properties edit the one worn.
        var link = worn is null ? string.Empty : types switch
        {
            "weaponskins.weapons" => Link(page, "weaponskins.weaponproperties", index.ToString(CultureInfo.InvariantCulture)),
            "weaponskins.knifes" => Link(page, "weaponskins.knifeproperties"),
            _ => Link(page, "weaponskins.gloveproperties"),
        };
        var applied = worn is { } on ? Finish(weapon, on.Paintkit) : null;
        if (applied is not null) page.AddHeader(LocalName(page, applied.LocalizedNames, applied.Name), Spot(page, weapon, applied, worn!.Value, team, glove) + link);
        else if (stock is not null && worn is not { Paintkit: not 0 }) page.AddHeader(caption, $"spot;image={stock};type={T(page, "stock")}" + link);
    }

    // A spotlight's Properties button, only where the player may open the root's entry for them.
    private static string Link(PageDraw page, string entry, string pick = "")
    {
        var root = page.Menu;
        var seen = new HashSet<IMenu> { root };
        while (root.Parent is { } parent && seen.Add(parent)) root = parent;
        var offered = root.GetComponents(MenuRegion.Body).Any(c => c is SubmenuComponent && c.Id == entry
            && c.IsVisible(page.Session) && c.IsEnabled(page.Session));
        return offered ? $";link={entry}|{pick}|{T(page, "properties")}" : string.Empty;
    }

    // What the player has on one weapon, knife or glove type, by the page family listing its finishes.
    private readonly record struct WornItem(int Paintkit, float Wear, int Seed, int? StatTrak, string? Nametag);

    private WornItem? Worn(string types, ulong steamId, SwiftlyS2.Shared.Players.Team team, ushort index) => types switch
    {
        "weaponskins.weapons" when api.TryGetWeaponSkin(steamId, team, index, out var skin)
            => new WornItem(skin.Paintkit, skin.PaintkitWear, skin.PaintkitSeed,
                skin.Quality == EconItemQuality.StatTrak ? skin.StattrakCount : null, skin.Nametag),
        "weaponskins.knifes" when api.TryGetKnifeSkin(steamId, team, out var knife) && knife.DefinitionIndex == index
            => new WornItem(knife.Paintkit, knife.PaintkitWear, knife.PaintkitSeed,
                knife.Quality == EconItemQuality.StatTrak ? knife.StattrakCount : null, knife.Nametag),
        "weaponskins.gloves" when api.TryGetGloveSkin(steamId, team, out var glove) && glove.DefinitionIndex == index
            => new WornItem(glove.Paintkit, glove.PaintkitWear, glove.PaintkitSeed, null, null),
        _ => null,
    };

    private string Spot(PageDraw page, string weapon, PaintkitDefinition finish, WornItem worn, SwiftlyS2.Shared.Players.Team team, bool glove)
    {
        var parts = new List<string>(10)
        {
            "spot", "chosen", "image=" + FinishImage(weapon, finish), "sub=" + ItemName(page, weapon),
            "type=" + RarityName(page, finish, glove),
        };
        if (Rarity(finish.Rarity) is { } rarity) parts.Add("icon=" + rarity);
        parts.Add("note=" + T(page, "equipped") + Side(team));
        Fact(parts, T(page, "fact.exterior"), $"{T(page, "wear." + WearKey(worn.Wear))} ({worn.Wear.ToString("0.00#", CultureInfo.InvariantCulture)})");
        Fact(parts, T(page, "fact.pattern"), worn.Seed.ToString(CultureInfo.InvariantCulture));
        if (worn.StatTrak is { } kills) Fact(parts, T(page, "fact.stattrak"), T(page, kills == 1 ? "kills.one" : "kills.other", Directive.Number(kills)));
        if (!string.IsNullOrWhiteSpace(worn.Nametag)) Fact(parts, T(page, "fact.nametag"), $"“{Directive.Clean(worn.Nametag)}”");
        return string.Join(';', parts);
    }

    private static void Fact(List<string> parts, string label, string value)
    {
        if (label.Length > 0) parts.Add($"fact={label}|{value}");
    }

    private static string Side(SwiftlyS2.Shared.Players.Team team) => (int)team switch { 3 => " · CT", 2 => " · T", _ => string.Empty };

    // CS2's exteriors, by the float's upper bounds.
    private static string WearKey(float wear) => wear switch
    {
        < 0.07f => "fn",
        < 0.15f => "mw",
        < 0.38f => "ft",
        < 0.45f => "ww",
        _ => "bs",
    };

    // Gloves read Extraordinary in game whatever their finish's rarity.
    private static string RarityName(PageDraw page, PaintkitDefinition finish, bool glove) =>
        T(page, "rarity." + (glove ? "extraordinary" : finish.Rarity.Name.ToLowerInvariant()));

    private static string? Rarity(PaintkitDefinition finish) => Rarity(finish.Rarity);

    private static string? Rarity(RarityDefinition rarity) =>
        rarity.Id is >= 1 and <= 9 ? rarity.Id.ToString(CultureInfo.InvariantCulture) : null;

    // The finish the player has on a gun, by its Items key; null for none or one this build lacks.
    private PaintkitDefinition? WornFinish(ulong steamId, SwiftlyS2.Shared.Players.Team team, string weapon) =>
        api.Items.TryGetValue(weapon, out var item) && api.TryGetWeaponSkin(steamId, team, (ushort)item.Index, out var skin)
        && skin.Paintkit != 0
            ? Finish(weapon, skin.Paintkit)
            : null;

    // The guns WeaponSkins lists under each category, in its order. Mirrors its Utilities
    // (IsWeaponDefinitionIndex, GetWeaponCategory), which the API does not expose.
    private Dictionary<string, List<string>> GunsByCategory()
    {
        if (_gunsByCategory is { } cached) return cached;
        var guns = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var weapon in api.WeaponToPaintkits.Keys)
        {
            if (!api.Items.TryGetValue(weapon, out var item) || item.Index is 42 or 59 or (>= 500 and < 600) || item.Index > 5000 || item.Index >= 100)
                continue;
            var category = CategoryOf(weapon);
            if (!guns.TryGetValue(category, out var list)) guns[category] = list = [];
            list.Add(weapon);
        }
        return _gunsByCategory = guns;
    }

    private Dictionary<string, List<string>>? _gunsByCategory;

    private static string CategoryOf(string weapon) => weapon.ToLowerInvariant() switch
    {
        "weapon_ak47" or "weapon_aug" or "weapon_famas" or "weapon_galilar" or "weapon_m4a1"
            or "weapon_m4a1_silencer" or "weapon_sg556" => "rifles",
        "weapon_cz75a" or "weapon_deagle" or "weapon_elite" or "weapon_fiveseven" or "weapon_glock"
            or "weapon_hkp2000" or "weapon_p250" or "weapon_revolver" or "weapon_tec9" or "weapon_usp_silencer" => "pistols",
        "weapon_bizon" or "weapon_mac10" or "weapon_mp5sd" or "weapon_mp7" or "weapon_mp9" or "weapon_p90" or "weapon_ump45" => "smgs",
        "weapon_mag7" or "weapon_nova" or "weapon_sawedoff" or "weapon_xm1014" => "shotguns",
        "weapon_awp" or "weapon_g3sg1" or "weapon_scar20" or "weapon_ssg08" => "snipers",
        "weapon_m249" or "weapon_negev" => "heavy",
        _ => "other",
    };

    private static string Counts(PageDraw page, int guns, int skinned)
    {
        var size = T(page, guns == 1 ? "guns.one" : "guns.other", guns);
        return skinned > 0 ? $"{size} · {T(page, "skinned", skinned)}" : size;
    }

    // Counted from the API rather than the page's rows, which a paged list only shows part of.
    private void GunChips(PageDraw page, string? category)
    {
        var guns = GunsByCategory();
        List<string> list = category is null ? [.. guns.Values.SelectMany(l => l)] : guns.GetValueOrDefault(category) ?? [];
        if (list.Count == 0) return;
        var (steamId, team) = Who(page);
        var skinned = list.Count(weapon => WornFinish(steamId, team, weapon) is not null);
        page.AddHeader(T(page, list.Count == 1 ? "guns.one" : "guns.other", list.Count), "chip;glyph=weapons");
        if (skinned > 0) page.AddHeader(T(page, "skinned", skinned), "chip;glyph=check");
    }

    // The agent worn on the player's side is marked, and spotlit as a finish page's finish is.
    private void DrawAgents(PageDraw page)
    {
        var (steamId, team) = Who(page);
        var current = api.TryGetAgentSkin(steamId, team, out var index) ? index : (int?)null;
        var caption = T(page, (int)team == 3 ? "ct_agent" : "t_agent");

        for (var k = 0; k < page.Components.Count; k++)
            if (page.Components[k] is MenuComponentBase { Tag: AgentDefinition agent })
                page.Decorate(k, Payload(api.GetImageKey("agent", agent.Name), caption, current == agent.Index));

        if (current is { } on && api.Agents.Values.FirstOrDefault(a => a.Index == on) is { } worn)
            page.AddHeader(LocalName(page, worn.LocalizedNames, worn.Name), AgentSpot(page, worn, team, caption));
    }

    private string AgentSpot(PageDraw page, AgentDefinition agent, SwiftlyS2.Shared.Players.Team team, string caption)
    {
        var parts = new List<string>(8) { "spot", "chosen", "sub=" + caption, "type=" + T(page, "agent_rarity." + agent.Rarity.Name.ToLowerInvariant()) };
        if (api.GetImageKey("agent", agent.Name) is { } image) parts.Add("image=" + image);
        if (Rarity(agent.Rarity) is { } rarity) parts.Add("icon=" + rarity);
        parts.Add("note=" + T(page, "equipped") + Side(team));
        var link = Link(page, "weaponskins.agentproperties");
        if (link.Length > 0 && _patches) PatchesFact(page, parts, team);
        return string.Join(';', parts) + link;
    }

    // How many of the agent's patch slots are filled. No record yet reads as none.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void PatchesFact(PageDraw page, List<string> parts, SwiftlyS2.Shared.Players.Team team)
    {
        var patches = api.TryGetAgentPatches(page.Player.SteamID, team, out var stored) ? stored : [];
        Fact(parts, T(page, "fact.patches"), $"{patches.Count(p => p != 0)} / {PatchSlots}");
    }

    private void DrawMusicKits(PageDraw page)
    {
        var current = api.TryGetMusicKit(page.Player.SteamID, out var index) ? index : (int?)null;
        var caption = T(page, "music_kit");

        for (var k = 0; k < page.Components.Count; k++)
            if (page.Components[k] is MenuComponentBase { Tag: MusicKitDefinition kit })
                page.Decorate(k, Payload(api.GetImageKey("music", kit.Name), caption, current == kit.Index));
    }

    // The weapons the player has a finish on, each with that finish's render.
    private void DrawPropertiesList(PageDraw page)
    {
        var (steamId, team) = Who(page);
        var skins = api.TryGetWeaponSkins(steamId, out var all)
            ? all.Where(s => s.Team == team).GroupBy(s => s.DefinitionIndex).ToDictionary(g => g.Key, g => g.First().Paintkit)
            : [];

        for (var k = 0; k < page.Components.Count; k++)
        {
            if (page.Components[k] is not MenuComponentBase { Tag: ushort index } || Weapon(index) is not { } weapon) continue;
            var finish = skins.TryGetValue(index, out var paintkit) ? Finish(weapon, paintkit) : null;
            page.Decorate(k, Payload(finish is null ? BaseImage(weapon) : FinishImage(weapon, finish), null, false));
        }
    }

    // The first line is the item itself, drawn as the card's hero. A button, slider or switch that
    // follows a text field acts on that field's value, so it joins its row: name tag and its Unset,
    // wear and its track, StatTrak count and its switch.
    private void DrawProperties(PageDraw page, string id)
    {
        if (page.Components.Count > 0 && page.Components[0] is TextComponent and not InputComponent && Hero(page, id) is { } hero)
            page.Decorate(0, hero);

        for (var k = 1; k < page.Components.Count; k++)
        {
            if (page.Components[k - 1] is not InputComponent) continue;
            if (page.Components[k] is SliderComponent) page.Decorate(k, "inline;track=wear");
            else if (page.Components[k] is ButtonComponent or ToggleComponent) page.Decorate(k, "inline");
        }

        if (id == "weaponskins.skinproperties") DrawSlots(page);
    }

    private string? Hero(PageDraw page, string id)
    {
        var (steamId, team) = Who(page);
        int definition, paintkit;
        switch (id)
        {
            case "weaponskins.skinproperties" when page.TryOpener<ushort>(out var index) && api.TryGetWeaponSkin(steamId, team, index, out var skin):
                (definition, paintkit) = (index, skin.Paintkit);
                break;
            case "weaponskins.knifeproperties" when api.TryGetKnifeSkin(steamId, team, out var knife):
                (definition, paintkit) = (knife.DefinitionIndex, knife.Paintkit);
                break;
            case "weaponskins.gloveproperties" when api.TryGetGloveSkin(steamId, team, out var gloves):
                (definition, paintkit) = (gloves.DefinitionIndex, gloves.Paintkit);
                break;
            default:
                return null;
        }

        if (Weapon(definition) is not { } weapon) return null;
        var glove = id == "weaponskins.gloveproperties";
        if (Finish(weapon, paintkit) is not { } finish)
            return glove ? "hero" : "hero;image=" + BaseImage(weapon);

        var rarity = RarityName(page, finish, glove);
        return "hero;image=" + FinishImage(weapon, finish) + (rarity.Length > 0 ? ";type=" + rarity : "");
    }

    // A slot shows the sticker on it.
    private void DrawStickerSlots(PageDraw page)
    {
        if (WeaponOf(page) is not { } skin) return;

        for (var k = 0; k < page.Components.Count; k++)
            if (page.Components[k] is MenuComponentBase { Tag: int slot } && skin.GetSticker(slot) is { Id: not 0 } sticker
                && Sticker(sticker.Id) is { } definition && api.GetImageKey("sticker", definition.Name) is { } image)
                page.Decorate(k, "image=" + image);
    }

    // A slot's capsules, and a capsule's stickers. The sticker on the slot is marked.
    private void DrawStickers(PageDraw page)
    {
        var caption = T(page, "sticker");
        var applied = page.TryOpener<int>(out var slot) && WeaponOf(page)?.GetSticker(slot) is { Id: not 0 } on ? on.Id : (int?)null;
        for (var k = 0; k < page.Components.Count; k++)
        {
            switch (page.Components[k])
            {
                case MenuComponentBase { Tag: StickerCollectionDefinition capsule }:
                    page.Decorate(k, Payload(api.GetImageKey("capsule", capsule.Name), null, false));
                    break;
                case MenuComponentBase { Tag: StickerDefinition sticker }:
                    page.Decorate(k, Payload(api.GetImageKey("sticker", sticker.Name), caption, applied == sticker.Index));
                    break;
            }
        }
    }

    // A weapon's charm collections, and a collection's charms. The charm on the weapon is marked.
    private void DrawCharms(PageDraw page)
    {
        var caption = T(page, "charm");
        var applied = WeaponOf(page)?.GetKeychain(CharmSlot) is { Id: not 0 } on ? on.Id : (int?)null;
        for (var k = 0; k < page.Components.Count; k++)
        {
            switch (page.Components[k])
            {
                case MenuComponentBase { Tag: string collection }:
                    page.Decorate(k, Payload(api.GetImageKey("keychaincollection", collection), null, false));
                    break;
                case MenuComponentBase { Tag: KeychainDefinition charm }:
                    page.Decorate(k, Payload(api.GetImageKey("keychain", charm.Name), caption, applied == charm.Index));
                    break;
            }
        }
    }

    // WeaponSkins' sticker page lists six slots, each tagged with its number, and a weapon carries one
    // charm; an agent has five patch slots (AgentPatchDataService.Slots).
    private const int StickerSlots = 6;
    private const int CharmSlot = 0;
    private const int PatchSlots = 5;

    // The card's Stickers and Charms rows as the slots they open: a layout with slot tiles shows what is
    // on each, and a tile opens that slot's page (the Stickers row's slot, the Charms row's own page).
    private void DrawSlots(PageDraw page)
    {
        if (WeaponOf(page) is not { } skin) return;
        for (var k = 0; k < page.Components.Count; k++)
        {
            switch (page.Components[k])
            {
                case SubmenuComponent { Id: "weaponskins.stickers" }:
                    var tiles = new List<string>(StickerSlots);
                    for (var slot = 0; slot < StickerSlots; slot++)
                    {
                        var sticker = skin.GetSticker(slot) is { Id: not 0 } data ? Sticker(data.Id) : null;
                        tiles.Add(Slot(slot.ToString(CultureInfo.InvariantCulture),
                            sticker is null ? null : api.GetImageKey("sticker", sticker.Name),
                            sticker is null ? null : LocalName(page, sticker.LocalizedNames, sticker.Name)));
                    }
                    page.Decorate(k, string.Join(';', tiles));
                    break;
                case SubmenuComponent { Id: "weaponskins.keychains" }:
                    var charm = skin.GetKeychain(CharmSlot) is { Id: not 0 } on ? api.Keychains.Values.FirstOrDefault(c => c.Index == on.Id) : null;
                    page.Decorate(k, Slot(string.Empty, charm is null ? null : api.GetImageKey("keychain", charm.Name),
                        charm is null ? null : LocalName(page, charm.LocalizedNames, charm.Name)));
                    break;
            }
        }

        static string Slot(string pick, string? image, string? name) => $"slot={pick}|{image}|{name}";
    }

    // The skin on the weapon a sticker, charm or properties page belongs to, chosen pages up.
    private WeaponSkinData? WeaponOf(PageDraw page)
    {
        var (steamId, team) = Who(page);
        return page.TryOpener<ushort>(out var index) && api.TryGetWeaponSkin(steamId, team, index, out var skin) ? skin : null;
    }

    // Graffiti and tournaments' logos. The chosen graffiti is outlined, washed in its tint.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DrawGraffiti(PageDraw page)
    {
        var chosen = api.TryGetGraffiti(page.Player.SteamID, out var index, out var tint) ? index : (int?)null;
        var caption = T(page, "graffiti");
        for (var k = 0; k < page.Components.Count; k++)
        {
            switch (page.Components[k])
            {
                case MenuComponentBase { Tag: int eventId }:
                    page.Decorate(k, Payload(api.GetImageKey("graffititournament", eventId.ToString(CultureInfo.InvariantCulture)), null, false));
                    break;
                case MenuComponentBase { Tag: GraffitiDefinition graffiti }:
                    var payload = Payload(api.GetImageKey("graffiti", graffiti.Name), caption, chosen == graffiti.Index);
                    page.Decorate(k, chosen == graffiti.Index && tint > 0 ? payload + ";tint=spray-" + tint : payload);
                    break;
            }
        }
    }

    // One graffiti in each of its colours, captioned with the graffiti.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DrawGraffitiTints(PageDraw page)
    {
        if (!page.TryOpener<GraffitiDefinition>(out var graffiti)) return;

        var image = api.GetImageKey("graffiti", graffiti.Name);
        var caption = LocalName(page, graffiti.LocalizedNames, graffiti.Name);
        var chosen = api.TryGetGraffiti(page.Player.SteamID, out var index, out var tint) && index == graffiti.Index ? tint : (int?)null;
        for (var k = 0; k < page.Components.Count; k++)
            if (page.Components[k] is MenuComponentBase { Tag: GraffitiTintDefinition color })
                page.Decorate(k, Payload(image, caption, chosen == color.Index) + ";tint=spray-" + color.Index);
    }

    // A slot shows the patch on it.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DrawPatchSlots(PageDraw page)
    {
        var (steamId, team) = Who(page);
        if (!api.TryGetAgentPatches(steamId, team, out var patches)) return;

        for (var k = 0; k < page.Components.Count; k++)
            if (page.Components[k] is MenuComponentBase { Tag: int slot } && slot >= 0 && slot < patches.Count
                && Patch(patches[slot]) is { } patch && api.GetImageKey("patch", patch.Name) is { } image)
                page.Decorate(k, "image=" + image);
    }

    // A slot's patch packs, and a pack's patches. The patch on the slot is outlined.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DrawPatches(PageDraw page)
    {
        var (steamId, team) = Who(page);
        var current = page.TryOpener<int>(out var slot) && api.TryGetAgentPatches(steamId, team, out var patches)
                      && slot >= 0 && slot < patches.Count
            ? patches[slot]
            : 0;
        var caption = T(page, "patch");
        for (var k = 0; k < page.Components.Count; k++)
        {
            switch (page.Components[k])
            {
                case MenuComponentBase { Tag: PatchCollectionDefinition pack }:
                    page.Decorate(k, Payload(api.GetImageKey("patchcapsule", pack.Name), null, false));
                    break;
                case MenuComponentBase { Tag: PatchDefinition patch }:
                    page.Decorate(k, Payload(api.GetImageKey("patch", patch.Name), caption, current != 0 && current == patch.Index));
                    break;
            }
        }
    }

    // The pin the player wears on the scoreboard is outlined.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DrawPins(PageDraw page)
    {
        var current = api.TryGetPin(page.Player.SteamID, out var index) ? index : (int?)null;
        var caption = T(page, "pin");
        for (var k = 0; k < page.Components.Count; k++)
            if (page.Components[k] is MenuComponentBase { Tag: PinDefinition pin })
                page.Decorate(k, Payload(api.GetImageKey("pin", pin.Name), caption, current == pin.Index));
    }

    private static string Payload(string? image, string? caption, bool chosen, string? rarity = null)
    {
        var parts = new List<string>(4);
        if (image is not null) parts.Add("image=" + image);
        if (!string.IsNullOrEmpty(caption)) parts.Add("type=" + caption);
        if (rarity is not null) parts.Add("icon=" + rarity);
        if (chosen) parts.Add("chosen");
        return string.Join(';', parts);
    }

    // The finish at its cleanest wear, as the game's generated renders name it.
    private static string FinishImage(string weapon, PaintkitDefinition finish) => $"default_generated/{weapon}_{finish.Name}_light";

    private static string BaseImage(string weapon) => $"weapons/base_weapons/{weapon}";

    private PaintkitDefinition? Finish(string weapon, int paintkit) =>
        api.WeaponToPaintkits.TryGetValue(weapon, out var finishes) ? finishes.FirstOrDefault(p => p.Index == paintkit) : null;

    // The Items key (weapon_ak47) is what WeaponToPaintkits and the renders are named by.
    private string? Weapon(int index)
    {
        _weaponByIndex ??= api.Items.GroupBy(item => item.Value.Index).ToDictionary(g => g.Key, g => g.First().Key);
        return _weaponByIndex.GetValueOrDefault(index);
    }

    private StickerDefinition? Sticker(int index)
    {
        _stickerByIndex ??= api.StickerCollections.Values.SelectMany(c => c.Stickers)
            .GroupBy(s => s.Index).ToDictionary(g => g.Key, g => g.First());
        return _stickerByIndex.GetValueOrDefault(index);
    }

    private Dictionary<int, StickerDefinition>? _stickerByIndex;

    private PatchDefinition? Patch(int index) =>
        index == 0 ? null : api.Patches.Values.FirstOrDefault(p => p.Index == index);

    private string ItemName(PageDraw page, string weapon) =>
        api.Items.TryGetValue(weapon, out var item) ? LocalName(page, item.LocalizedNames, item.Name) : string.Empty;

    private static string LocalName(PageDraw page, IReadOnlyDictionary<string, string> names, string fallback) =>
        Directive.Clean(names.TryGetValue(page.Player.PlayerLanguage.Value, out var local) ? local
            : names.TryGetValue("english", out var english) ? english
            : fallback);

    private static (ulong SteamId, SwiftlyS2.Shared.Players.Team Team) Who(PageDraw page) =>
        (page.Player.SteamID, page.Player.Controller.Team);

    private static string T(PageDraw page, string key, params object[] args) => page.Text("customhud.weaponskins." + key, args);
}
