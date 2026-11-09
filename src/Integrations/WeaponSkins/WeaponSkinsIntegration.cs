using System.Globalization;
using System.Runtime.CompilerServices;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Menu;
using SwiftlyS2.Shared.Menu.Components;
using WeaponSkins.Shared;

namespace CustomHUD.Integrations.SkinPages;

// WeaponSkins' locker: each row's render, its caption and an outline on what the player has on,
// all read from IWeaponSkinAPI. A row says what it stands for through its Tag: a weapon type's
// definition index (ushort), a finish's paint kit index (int), an agent / music kit / sticker /
// capsule / charm / graffiti / tint / patch / patch pack / pin definition, a charm collection's name, a
// graffiti tournament's event id or a sticker or patch slot (int). A page under a weapon (its
// finishes, its properties, its stickers) gets that weapon from the row it was opened from (Openers).
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
    }

    // Weapon, knife and glove types. A knife or glove the player has on is outlined.
    private void DrawTypes(PageDraw page, string id)
    {
        var (steamId, team) = Who(page);
        int? equipped = id switch
        {
            "weaponskins.knifes" when api.TryGetKnifeSkin(steamId, team, out var knife) => knife.DefinitionIndex,
            "weaponskins.gloves" when api.TryGetGloveSkin(steamId, team, out var glove) => glove.DefinitionIndex,
            _ => null,
        };

        for (var k = 0; k < page.Components.Count; k++)
        {
            if (page.Components[k] is not MenuComponentBase { Tag: ushort index } || Weapon(index) is not { } weapon) continue;

            // CS2 ships no render of a bare glove type, so a glove borrows its rarest finish's.
            var image = id == "weaponskins.gloves"
                ? api.WeaponToPaintkits.TryGetValue(weapon, out var finishes) && finishes.MaxBy(p => p.Rarity.Id) is { } preview
                    ? FinishImage(weapon, preview)
                    : null
                : BaseImage(weapon);
            page.Decorate(k, Payload(image, null, equipped == index));
        }
    }

    // One weapon's finishes, under its type's row. Captioned with the weapon, outlined when applied.
    private void DrawFinishes(PageDraw page, string types)
    {
        if (!page.TryOpener<ushort>(out var index) || Weapon(index) is not { } weapon
            || !api.WeaponToPaintkits.TryGetValue(weapon, out var finishes))
            return;

        var (steamId, team) = Who(page);
        var applied = types switch
        {
            "weaponskins.weapons" => api.TryGetWeaponSkin(steamId, team, index, out var skin) ? skin.Paintkit : (int?)null,
            "weaponskins.knifes" => api.TryGetKnifeSkin(steamId, team, out var knife) && knife.DefinitionIndex == index ? knife.Paintkit : null,
            _ => api.TryGetGloveSkin(steamId, team, out var glove) && glove.DefinitionIndex == index ? glove.Paintkit : null,
        };
        var caption = ItemName(page, weapon);

        for (var k = 0; k < page.Components.Count; k++)
        {
            if (page.Components[k] is not MenuComponentBase { Tag: int paintkit }
                || finishes.FirstOrDefault(p => p.Index == paintkit) is not { } finish)
                continue;
            page.Decorate(k, Payload(FinishImage(weapon, finish), caption, applied == paintkit));
        }
    }

    private void DrawAgents(PageDraw page)
    {
        var (steamId, team) = Who(page);
        var current = api.TryGetAgentSkin(steamId, team, out var index) ? index : (int?)null;
        var caption = T(page, (int)team == 3 ? "ct_agent" : "t_agent");

        for (var k = 0; k < page.Components.Count; k++)
            if (page.Components[k] is MenuComponentBase { Tag: AgentDefinition agent })
                page.Decorate(k, Payload(api.GetImageKey("agent", agent.Name), caption, current == agent.Index));
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

        // Gloves read Extraordinary in game whatever their finish's rarity.
        var rarity = T(page, "rarity." + (glove ? "extraordinary" : finish.Rarity.Name.ToLowerInvariant()));
        return "hero;image=" + FinishImage(weapon, finish) + (rarity.Length > 0 ? ";type=" + rarity : "");
    }

    // A slot shows the sticker on it.
    private void DrawStickerSlots(PageDraw page)
    {
        var (steamId, team) = Who(page);
        if (!page.TryOpener<ushort>(out var index) || !api.TryGetWeaponSkin(steamId, team, index, out var skin)) return;

        for (var k = 0; k < page.Components.Count; k++)
            if (page.Components[k] is MenuComponentBase { Tag: int slot } && skin.GetSticker(slot) is { Id: not 0 } sticker
                && Sticker(sticker.Id) is { } definition && api.GetImageKey("sticker", definition.Name) is { } image)
                page.Decorate(k, "image=" + image);
    }

    // A slot's capsules, and a capsule's stickers.
    private void DrawStickers(PageDraw page)
    {
        var caption = T(page, "sticker");
        for (var k = 0; k < page.Components.Count; k++)
        {
            switch (page.Components[k])
            {
                case MenuComponentBase { Tag: StickerCollectionDefinition capsule }:
                    page.Decorate(k, Payload(api.GetImageKey("capsule", capsule.Name), null, false));
                    break;
                case MenuComponentBase { Tag: StickerDefinition sticker }:
                    page.Decorate(k, Payload(api.GetImageKey("sticker", sticker.Name), caption, false));
                    break;
            }
        }
    }

    private void DrawCharms(PageDraw page)
    {
        var caption = T(page, "charm");
        for (var k = 0; k < page.Components.Count; k++)
        {
            switch (page.Components[k])
            {
                case MenuComponentBase { Tag: string collection }:
                    page.Decorate(k, Payload(api.GetImageKey("keychaincollection", collection), null, false));
                    break;
                case MenuComponentBase { Tag: KeychainDefinition charm }:
                    page.Decorate(k, Payload(api.GetImageKey("keychain", charm.Name), caption, false));
                    break;
            }
        }
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

    private static string Payload(string? image, string? caption, bool chosen)
    {
        var parts = new List<string>(3);
        if (image is not null) parts.Add("image=" + image);
        if (!string.IsNullOrEmpty(caption)) parts.Add("type=" + caption);
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

    private StickerDefinition? Sticker(int index) =>
        api.StickerCollections.Values.SelectMany(c => c.Stickers).FirstOrDefault(s => s.Index == index);

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

    private static string T(PageDraw page, string key) => page.Text("customhud.weaponskins." + key);
}
