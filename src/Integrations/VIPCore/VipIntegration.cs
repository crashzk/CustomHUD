using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Menu;
using SwiftlyS2.Shared.Menu.Components;
using VIPCore.Contract;

namespace CustomHUD.Integrations.VipPages;

// VIPCore's menus as their designed pages: the membership over the perks, a feature's choices as
// cards, a module's picks (the gun menu) as a loadout, and the admin pages as a table beside the
// selected VIP's grants. Every row VIPCore means to be drawn carries a VIPCore.Contract record as
// its Tag; grants, names and values come from IVipCoreApiV1.
internal sealed class VipIntegration(IVipCoreApiV1 api) : IPageIntegration
{
    private const string FovKey = "vip.fov";
    private const string TagKey = "vip.tag";

    // Each group's look, by weight (game thread). Re-read every 5 s, not per draw: before VIPCore 1.3.1 GetGroupInfos scanned its whole config.
    private Dictionary<string, (string Glyph, string Tone)>? _styles;
    private long _stylesAt;
    private const long StylesMs = 5000;

    public string Provider => "VIPCore";

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IPageIntegration? TryCreate(IInterfaceManager interfaces)
    {
        ApiProbe.Require(typeof(IVipCoreApiV1), "VIPCore 1.2.0", nameof(IVipCoreApiV1.GetFeatureView),
            nameof(IVipCoreApiV1.GetClientVipGrants), nameof(IVipCoreApiV1.GetGroupInfos));
        return interfaces.TryGetSharedInterface<IVipCoreApiV1>("VIPCore.Api.v1", out var api) && api is not null
            ? new VipIntegration(api)
            : null;
    }

    public bool Owns(IMenu menu) => menu.Id.StartsWith("vipcore.", StringComparison.Ordinal);

    // Off the main list, so a long users list chunks by its own rows.
    public string? RegionOf(IMenu menu, IMenuComponent component) => TagOf(component) switch
    {
        VipGrantRow or VipLengthRow { Extends: not null } => "detail",
        VipActionRow { Action: VipAction.MakePermanent or VipAction.Remove } => "detail",
        VipActionRow { Action: VipAction.AddGroup or VipAction.RemoveAll } => "detailbar",
        VipGroupRow or VipScopeRow or VipLengthRow when menu.Id == VipMenuIds.Grant => "detail",
        VipActionRow { Action: VipAction.Summary or VipAction.Submit } => menu.Id == VipMenuIds.Grant ? "detailbar" : "foot",
        _ => null,
    };

    public void Draw(PageDraw page)
    {
        var id = page.Menu.Id;
        if (id == VipMenuIds.Perks) Perks(page);
        else if (id == VipMenuIds.Grant && page.Components.Any(c => TagOf(c) is VipGroupRow)) Grant(page);
        else if (id == VipMenuIds.Grant) GrantPlayers(page);
        else if (id == VipMenuIds.GrantGroup) GrantGroups(page);
        else if (id == VipMenuIds.GrantLength) GrantLengths(page);
        else if (id == VipMenuIds.GrantScope) GrantScopes(page);
        else if (id.StartsWith(VipMenuIds.UsersList, StringComparison.Ordinal)) Users(page);
        else if (id != VipMenuIds.Hub && id != VipMenuIds.Users) Choices(page);
    }

    // ---- Perks: the membership card over a card per perk

    private void Perks(PageDraw page)
    {
        var grants = api.GetClientVipGrants(page.Player);
        // The locker's membership card has a banner of its own; the list says only what is held, and until when.
        var card = page.Regions.Contains("banner");
        if (card) MembershipChips(page, grants);
        page.Hint(T(page, "hint.perks"));

        int first = -1, toggles = 0, on = 0;
        for (var k = 0; k < page.Components.Count; k++)
        {
            if (TagOf(page.Components[k]) is not VipFeatureRow row || api.GetFeatureView(page.Player, row.FeatureKey) is not { } view)
                continue;
            if (first < 0) first = k;

            var payload = "glyph=" + Glyph(page, view.Key) + (view.Description is { Length: > 0 } line ? ";sub=" + Directive.Clean(line) : "");
            if (page.Components[k] is ToggleComponent)
            {
                toggles++;
                if (view.State == FeatureState.Enabled) on++;
                page.Decorate(k, payload);
                continue;
            }
            // A choice shows the one in use at the card's corner.
            var value = view.Value is { Length: > 0 } current ? "\t" + Directive.Clean(current) : "";
            page.Draw(k, Directive.Clean(view.Name) + value, (page.Components[k] is SubmenuComponent ? "submenu;" : "") + payload);
        }

        if (grants.Count > 0 && card) Membership(page, grants);
        else if (grants.Count > 0) GroupLines(page, grants);
        if (first >= 0) page.Add($"{T(page, "perks")}\t{T(page, "perks_on", on, toggles)}", "section", first);
    }

    private void GroupLines(PageDraw page, IReadOnlyList<VipGrant> grants)
    {
        foreach (var grant in grants)
        {
            var (glyph, tone) = Style(grant.Group);
            page.Add(Directive.Clean(grant.Group), grant.Permanent
                ? $"glyph={glyph};tone={tone};sub={T(page, "permanent")}"
                : $"glyph={glyph};tone={tone};sub={T(page, "until", Date(page, grant.ExpiresAt))};note={T(page, "left", DaysLeft(grant))}", 0);
        }
    }

    // The tier, how long it lasts, then every group held: the active one first, the rest in the order they apply.
    private void Membership(PageDraw page, IReadOnlyList<VipGrant> grants)
    {
        var active = grants[0];
        var (glyph, tone) = Style(active.Group);
        var since = active.GrantedAt > 0 ? " · " + T(page, "since", ShortDate(page, active.GrantedAt)) : "";
        page.Add(Directive.Clean(active.Group),
            $"at=banner;glyph={glyph};tone={tone};type={T(page, "tier")};sub={Scope(page, active.Global)}{since}", 0);

        if (active.Permanent)
        {
            page.Add(T(page, "permanent"), $"at=banner;type={T(page, "expires")};note={T(page, "never_ends")}", 0);
        }
        else
        {
            var left = DaysLeft(active);
            var note = active.GrantedAt > 0 ? T(page, "left_of", left, TotalDays(active)) : T(page, "left", left);
            page.Add(Date(page, active.ExpiresAt), $"at=banner;type={T(page, "expires")};bar={Share(active)};note={note}", 0);
        }

        page.Add(T(page, "your_groups"), "at=banner;group=caption", 0);
        foreach (var grant in grants)
        {
            var (g, t) = Style(grant.Group);
            var status = ReferenceEquals(grant, active) ? $"note={T(page, "status.active")};notetone=good;chosen"
                : Outlasts(grant, active) ? $"note={T(page, "status.next")};notetone=faint"
                : $"note={T(page, "status.idle")};notetone=faint";
            page.Add(Directive.Clean(grant.Group), $"at=banner;glyph={g};tone={t};sub={Scope(page, grant.Global)};{status}", 0);
        }
    }

    private void MembershipChips(PageDraw page, IReadOnlyList<VipGrant> grants)
    {
        if (grants.Count == 0) return;
        var active = grants[0];
        var (glyph, tone) = Style(active.Group);
        page.AddHeader($"{Directive.Clean(active.Group)}\t{T(page, "chip.tier")}", (tone == "gold" ? "chip=gold" : "chip") + ";glyph=" + glyph);
        if (!active.Permanent) page.AddHeader($"{DaysLeft(active)}\t{T(page, "chip.days")}", "chip;glyph=hourglass");
    }

    // ---- Customize and a module's own page: a section per feature or group, a card per choice

    private void Choices(PageDraw page)
    {
        var customize = page.Menu.Id == VipMenuIds.Customize;
        if (customize) MembershipChips(page, api.GetClientVipGrants(page.Player));
        if (page.Menu.Tag is VipFeatureLimit limit) LimitChips(page, limit);
        var own = T(page, "hint.page." + page.Menu.Id);
        page.Hint(own.Length > 0 ? own : T(page, customize ? "hint.customize" : "hint.picks"));

        // From the whole menu: a keyboard page holds only some of the choices.
        var all = page.Menu.GetComponents(MenuRegion.Body).Select(TagOf).ToList();
        var picks = all.OfType<VipOptionRow>().Where(o => o.Selected).Select(o => Directive.Clean(o.Option.Label)).ToList();
        for (var k = 0; k < page.Components.Count; k++)
        {
            switch (TagOf(page.Components[k]))
            {
                case VipSectionRow section:
                    var count = all.OfType<VipOptionRow>().Count(o => o.FeatureKey == section.FeatureKey && o.Option.Group == section.Group);
                    var group = section.Group?.ToLowerInvariant() ?? SectionGroup(section.FeatureKey);
                    // Text, not a pick: a focusable heading would otherwise draw as one of the cards.
                    page.Draw(k, $"{Directive.Clean(TextOf(page.Components[k]))}\t{count}",
                        "section;info" + (group is not null && Directive.IsToken(group) ? ";group=" + group : ""));
                    break;
                case VipOptionRow option:
                    Option(page, k, option);
                    break;
                case VipActionRow { Action: VipAction.Summary }:
                    var note = (page.Components[k] as MenuComponentBase)?.Comment is { Length: > 0 } comment
                        ? ";note=" + Directive.Clean(comment)
                        : "";
                    page.Draw(k, picks.Count > 0 ? string.Join(" + ", picks) : T(page, "loadout_none"),
                        $"info;at=foot;type={T(page, "loadout")};glyph=info{note}");
                    break;
                case VipActionRow { Action: VipAction.Submit }:
                    page.Decorate(k, "at=foot;primary");
                    break;
            }
        }
    }

    private void Option(PageDraw page, int k, VipOptionRow row)
    {
        var option = row.Option;
        if (row.FeatureKey == TagKey)
        {
            ChatTag(page, k, row);
            return;
        }
        if (WeaponClass(option.Id) is { } weapon)
        {
            // A pistol's section already says what it is; a primary's card names its class.
            var type = option.Description is { Length: > 0 } kind && !string.Equals(kind, option.Group, StringComparison.OrdinalIgnoreCase)
                ? ";type=" + Directive.Clean(kind)
                : "";
            page.Draw(k, Directive.Clean(option.Label), $"image=weapons/base_weapons/{weapon}{type}{(row.Selected ? ";chosen" : "")}");
            return;
        }

        var parts = new List<string>();
        if (row.FeatureKey == FovKey) parts.Add("glyph=" + Dial(option.Id));
        if (option.Description is { Length: > 0 } line) parts.Add("sub=" + Directive.Clean(line));
        if (row.Selected) parts.AddRange(["chosen", "tag=" + T(page, "in_use"), "tagtone=accent"]);
        if (option.IsDefault) parts.AddRange(["tag2=" + T(page, "default"), "tag2tone=dark"]);
        page.Draw(k, Directive.Clean(option.Label), string.Join(';', parts));
    }

    // The tag as chat shows it: before the player's own name and a line they might type.
    private void ChatTag(PageDraw page, int k, VipOptionRow row)
    {
        var payload = new StringBuilder("sub=").Append(T(page, "tag_sample"));
        var none = row.Option.Id == "0";
        if (!none)
        {
            payload.Append(";type=").Append(Directive.Clean(row.Option.Label));
            if (ToneOf(row.Option.Color) is { } tone) payload.Append(";tone=").Append(tone);
        }
        if (row.Selected) payload.Append(";chosen;note=").Append(T(page, "in_use")).Append(";notetone=accent");
        else if (none) payload.Append(";note=").Append(Directive.Clean(row.Option.Label)).Append(";notetone=faint");
        var name = page.Player.Name is { Length: > 0 } own ? Directive.Clean(own) : T(page, "you");
        page.Draw(k, name, payload.ToString());
    }

    private static void LimitChips(PageDraw page, VipFeatureLimit limit)
    {
        page.AddHeader($"{limit.Left} / {limit.PerRound}\t{T(page, "chip.left")}", "chip;glyph=" + (page.Icon(page.Menu.Id) ?? "refresh"));
        if (limit.Until is { } until && until > DateTimeOffset.UtcNow)
        {
            var left = until - DateTimeOffset.UtcNow;
            page.AddHeader($"{(int)left.TotalMinutes}:{left.Seconds:00}\t{T(page, "chip.closes")}", "chip=warn;glyph=stopwatch");
        }
    }

    // ---- VIP users: a table of VIPs, the selected one's grants beside it

    private void Users(PageDraw page)
    {
        if (page.Menu.Tag is VipUserStats stats) UserChips(page, stats, ending: true);
        page.Hint(T(page, "hint.users"));

        VipUserInfo? selected = null;
        int firstUser = -1, firstGrant = -1, firstStep = -1, firstBar = -1, grants = 0;
        for (var k = 0; k < page.Components.Count; k++)
        {
            switch (TagOf(page.Components[k]))
            {
                case VipUserRow row:
                    if (firstUser < 0) firstUser = k;
                    if (row.Selected) selected = row.User;
                    User(page, k, row);
                    break;
                case VipGrantRow row:
                    if (firstGrant < 0) firstGrant = k;
                    grants++;
                    GrantCard(page, k, row);
                    break;
                case VipLengthRow { Extends: not null } step:
                    if (firstStep < 0) firstStep = k;
                    page.Draw(k, Step(page, step.Length), "at=detail;group=step" + (step.Selected ? ";chosen" : ""));
                    break;
                case VipActionRow { Action: VipAction.MakePermanent }:
                    page.Decorate(k, "at=detail;group=link;tone=gold");
                    break;
                case VipActionRow { Action: VipAction.Remove }:
                    page.Decorate(k, "at=detail;group=link;danger");
                    break;
                case VipActionRow { Action: VipAction.AddGroup }:
                    if (firstBar < 0) firstBar = k;
                    page.Decorate(k, "at=detailbar;glyph=add");
                    break;
                case VipActionRow { Action: VipAction.RemoveAll }:
                    if (firstBar < 0) firstBar = k;
                    page.Decorate(k, "at=detailbar;danger");
                    break;
            }
        }

        if (firstUser >= 0) page.Add($"{T(page, "col.player")}\t{T(page, "col.groups")}\t{T(page, "col.expires")}", "head", firstUser);
        if (selected is not null)
        {
            var at = firstGrant >= 0 ? firstGrant : firstBar >= 0 ? firstBar : page.Components.Count;
            page.Add(Directive.Clean(selected.Name), $"at=detail;avatar;group=head;sub={selected.SteamId};note={Seen(page, selected, full: true)}", at);
            page.Add($"{T(page, "groups")}\t{grants}", "at=detail;section", at);
        }
        if (firstStep >= 0) page.Add(T(page, "extend"), "at=detail;group=caption", firstStep);
    }

    private void User(PageDraw page, int k, VipUserRow row)
    {
        var user = row.User;
        var payload = new StringBuilder("avatar;sub=").Append(Seen(page, user, full: false))
            .Append(";subtone=").Append(user.Online ? "good" : "faint");
        if (user.Grants.Count > 0) payload.Append(";tag=").Append(Directive.Clean(user.Grants[0].Group)).Append(";tagtone=").Append(Style(user.Grants[0].Group).Tone);
        if (user.Grants.Count > 1) payload.Append(";tag2=").Append(Directive.Clean(user.Grants[1].Group)).Append(";tag2tone=").Append(Style(user.Grants[1].Group).Tone);

        var text = Directive.Clean(user.Name);
        if (user.Grants.FirstOrDefault() is { } top)
        {
            if (top.Permanent)
            {
                payload.Append(";note=").Append(T(page, "permanent")).Append(";notetone=gold");
            }
            else
            {
                var days = DaysLeft(top);
                text += "\t" + T(page, "days_left", days);
                payload.Append(";note=").Append(Date(page, top.ExpiresAt)).Append(";bar=").Append(Share(top));
                if (days <= 3) payload.Append(";metertone=bad;tone=bad");
            }
        }
        if (row.Selected) payload.Append(";chosen");
        page.Draw(k, text, payload.ToString());
    }

    private void GrantCard(PageDraw page, int k, VipGrantRow row)
    {
        var grant = row.Grant;
        var (glyph, tone) = Style(grant.Group);
        var value = grant.Permanent ? T(page, "permanent") : T(page, "days_left", DaysLeft(grant));
        var payload = new StringBuilder($"at=detail;group=gcard;glyph={glyph};tone={tone};tag={Scope(page, grant.Global)};sub=");
        payload.Append(grant.Permanent ? T(page, "never_ends") : T(page, "until", Date(page, grant.ExpiresAt)));
        if (!grant.Permanent) payload.Append(";bar=").Append(Share(grant));
        if (row.Expanded) payload.Append(";chosen");
        page.Draw(k, $"{Directive.Clean(grant.Group)}\t{value}", payload.ToString());
    }

    private static void UserChips(PageDraw page, VipUserStats stats, bool ending)
    {
        page.AddHeader($"{stats.Users}\t{T(page, "chip.users")}", "chip;glyph=player");
        if (ending && stats.EndingThisWeek > 0) page.AddHeader($"{stats.EndingThisWeek}\t{T(page, "chip.ending")}", "chip=warn;glyph=hourglass");
    }

    // ---- Grant VIP from VIPCore 1.4.0: a page per step, the player, the group, how long, then where

    private void GrantPlayers(PageDraw page)
    {
        StepHeader(page, "hint.grant");
        var first = -1;
        for (var k = 0; k < page.Components.Count; k++)
        {
            if (TagOf(page.Components[k]) is not VipPlayerRow row) continue;
            if (first < 0) first = k;
            page.Draw(k, Directive.Clean(row.Name), $"avatar;sub={row.SteamId}{Badge(row)}");
        }
        // Every row of this step is a player, and a keyboard page holds only some of them.
        if (first >= 0) page.Add($"{T(page, "player")}\t{T(page, "online", page.RowCount)}", "section", first);
    }

    private void GrantGroups(PageDraw page)
    {
        StepHeader(page, "hint.grant_group");
        var first = -1;
        for (var k = 0; k < page.Components.Count; k++)
        {
            if (TagOf(page.Components[k]) is not VipGroupRow row) continue;
            if (first < 0) first = k;
            var (glyph, tone) = Style(row.Group.Name);
            page.Draw(k, Directive.Clean(row.Group.Name), $"submenu;glyph={glyph};tone={tone};sub={T(page, "perks_count", row.Group.FeatureCount)}");
        }
        if (first >= 0) page.Add(T(page, "group"), "section", first);
    }

    private void GrantLengths(PageDraw page)
    {
        StepHeader(page, "hint.grant_length");
        var first = -1;
        for (var k = 0; k < page.Components.Count; k++)
        {
            if (TagOf(page.Components[k]) is not VipLengthRow row) continue;
            if (first < 0) first = k;
            page.Decorate(k, row.Length is { } span
                ? $"submenu;glyph=hourglass;sub={T(page, "until", Date(page, DateTimeOffset.UtcNow.Add(span).ToUnixTimeSeconds()))}"
                : $"submenu;glyph=hourglass;tone=gold;sub={T(page, "never_ends")}");
        }
        if (first >= 0) page.Add(T(page, "length"), "section", first);
    }

    private void GrantScopes(PageDraw page)
    {
        StepHeader(page, "hint.grant_scope");
        var first = -1;
        for (var k = 0; k < page.Components.Count; k++)
        {
            if (TagOf(page.Components[k]) is not VipScopeRow row) continue;
            if (first < 0) first = k;
            page.Decorate(k, $"submenu;glyph={(row.Global ? "globe" : "server")};sub={T(page, row.Global ? "scope_short.global" : "scope_short.local")}");
        }
        if (first >= 0) page.Add(T(page, "scope"), "section", first);
    }

    private static void StepHeader(PageDraw page, string hint)
    {
        if (page.Menu.Tag is VipUserStats stats) UserChips(page, stats, ending: false);
        page.Hint(T(page, hint));
    }

    // ---- Grant VIP before VIPCore 1.4.0: the player on the left; group, scope and length beside, spelled out before Grant

    // What locker_vip.vcss's grant form fits: two lines of large group cards, three of compact ones.
    private const int RoomyGroups = 6, CompactGroups = 9;

    private void Grant(PageDraw page)
    {
        if (page.Menu.Tag is VipUserStats stats) UserChips(page, stats, ending: false);
        page.Hint(T(page, "hint.grant"));

        VipPlayerRow? who = null;
        VipGroupRow? group = null;
        VipScopeRow? scope = null;
        VipLengthRow? length = null;
        int top = -1, players = 0, groups = 0, firstGroup = -1, firstScope = -1, firstLength = -1, summary = -1;
        for (var k = 0; k < page.Components.Count; k++)
        {
            switch (TagOf(page.Components[k]))
            {
                case VipActionRow { Action: VipAction.Search }:
                    if (top < 0) top = k;
                    var typed = (page.Components[k] as InputComponent)?.GetValue(page.Session);
                    page.Draw(k, typed is { Length: > 0 } ? Directive.Clean(typed) : T(page, "search"), "glyph=search;group=search");
                    break;
                case VipPlayerRow row:
                    if (top < 0) top = k;
                    players++;
                    if (row.Selected) who = row;
                    page.Draw(k, Directive.Clean(row.Name), $"avatar;sub={row.SteamId}{Badge(row)}{(row.Selected ? ";chosen" : "")}");
                    break;
                case VipGroupRow row:
                    if (firstGroup < 0) firstGroup = k;
                    groups++;
                    if (row.Selected) group = row;
                    var (glyph, tone) = Style(row.Group.Name);
                    page.Draw(k, Directive.Clean(row.Group.Name),
                        $"at=detail;group=tier;glyph={glyph};tone={tone};sub={T(page, "perks_count", row.Group.FeatureCount)}{(row.Selected ? ";chosen" : "")}");
                    break;
                case VipScopeRow row:
                    if (firstScope < 0) firstScope = k;
                    if (row.Selected) scope = row;
                    page.Decorate(k, $"at=detail;group=segment;glyph={(row.Global ? "globe" : "server")}{(row.Selected ? ";chosen" : "")}");
                    break;
                case VipLengthRow { Extends: null } row:
                    if (firstLength < 0) firstLength = k;
                    if (row.Selected) length = row;
                    page.Decorate(k, "at=detail;group=pill" + (row.Selected ? ";chosen" : "") + (row.Length is null ? ";tone=gold" : ""));
                    break;
                case VipActionRow { Action: VipAction.Summary }:
                    summary = k;
                    break;
                case VipActionRow { Action: VipAction.Submit }:
                    page.Decorate(k, "at=detailbar;primary");
                    break;
            }
        }

        if (top >= 0) page.Add($"{T(page, "player")}\t{T(page, "online", players)}", "section", top);
        if (groups > RoomyGroups) page.AddHeader(string.Empty, "variant=compact");
        if (groups > CompactGroups) page.AddHeader(string.Empty, "variant=scroll");
        if (firstGroup >= 0) page.Add(T(page, "group"), "at=detail;section", firstGroup);
        if (firstScope >= 0) page.Add(T(page, "scope"), "at=detail;section", firstScope);
        if (firstLength >= 0)
        {
            if (scope is not null) page.Add(T(page, scope.Global ? "scope_hint.global" : "scope_hint.local"), "at=detail;group=hint", firstLength);
            page.Add(T(page, "length"), "at=detail;section", firstLength);
        }
        if (summary < 0) return;
        if (who is null || group is null)
        {
            page.Draw(summary, T(page, "pick_player"), "info;at=detailbar");
            return;
        }
        var line = T(page, scope?.Global == false ? "summary.local" : "summary.global", Directive.Clean(who.Name), Directive.Clean(group.Group.Name));
        var until = length?.Length is { } span
            ? T(page, "until", Date(page, DateTimeOffset.UtcNow.Add(span).ToUnixTimeSeconds()))
            : T(page, "permanently");
        page.Draw(summary, line, "info;at=detailbar;sub=" + until);
    }

    private string Badge(VipPlayerRow row)
    {
        if (row.Group is { Length: > 0 } group) return $";tag={Directive.Clean(group)};tagtone={Style(group).Tone}";
        return row.Team switch
        {
            "T" => ";tag=T;tagtone=t",
            "CT" => ";tag=CT;tagtone=ct",
            "SPEC" => ";tag=SPEC;tagtone=dark",
            _ => "",
        };
    }

    // ---- shared

    // Lowest weight blue with a star, highest purple; a gold crown second from the top, or for a lone group.
    private Dictionary<string, (string Glyph, string Tone)> Styles()
    {
        var groups = api.GetGroupInfos();
        var styles = new Dictionary<string, (string Glyph, string Tone)>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < groups.Count; i++)
        {
            var fromTop = groups.Count - 1 - i;
            styles[groups[i].Name] = groups.Count == 1 || (fromTop == 0 && groups.Count == 2) || (fromTop == 1 && groups.Count >= 3)
                ? ("crown", "gold")
                : fromTop == 0 ? ("legend", "legend")
                : ("star", "info");
        }
        return styles;
    }

    private (string Glyph, string Tone) Style(string group)
    {
        var now = Environment.TickCount64;
        if (_styles is null || now - _stylesAt >= StylesMs)
        {
            _styles = Styles();
            _stylesAt = now;
        }
        return _styles.TryGetValue(group, out var style) ? style : ("star", "info");
    }

    private static string Glyph(PageDraw page, string featureKey) => page.Icon(featureKey) ?? "star";

    private static string? SectionGroup(string featureKey) => featureKey switch
    {
        FovKey => "fov",
        TagKey => "chattag",
        _ => null,
    };

    // The dial drawn nearest a FOV choice; the addon ships one per ten degrees from 80 to 130.
    private static string Dial(string id)
        => int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fov)
            ? "fov-" + Math.Clamp((int)Math.Round(fov / 10.0) * 10, 80, 130)
            : "fov";

    // Only a plain class name reaches the image path.
    private static string? WeaponClass(string id)
        => id.StartsWith("weapon_", StringComparison.Ordinal) && id.Length <= 40
           && id.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_')
            ? id
            : null;

    // A chat colour as one of the page's tones; an unknown one keeps the row's own colour.
    private static string? ToneOf(string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) return null;
        var name = color.Trim().ToLowerInvariant();
        if (name.StartsWith('#') && name.Length == 7 && int.TryParse(name[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            int r = rgb >> 16 & 0xFF, g = rgb >> 8 & 0xFF, b = rgb & 0xFF;
            if (r > 150 && b > 150 && g < r) return "legend";
            if (b >= r && b >= g) return "info";
            if (g >= r && g >= b) return "good";
            return g > r * 55 / 100 ? "gold" : "bad";
        }
        return name switch
        {
            "blue" or "lightblue" or "darkblue" or "bluegrey" => "info",
            "gold" or "yellow" or "lightyellow" or "orange" or "olive" => "gold",
            "green" or "lime" or "lightgreen" => "good",
            "red" or "darkred" or "lightred" => "bad",
            "purple" or "lightpurple" or "magenta" or "pink" => "legend",
            _ => null,
        };
    }

    private static bool Outlasts(VipGrant grant, VipGrant active)
        => !active.Permanent && (grant.Permanent || grant.ExpiresAt > active.ExpiresAt);

    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static long DaysLeft(VipGrant grant) => Math.Max(0, (grant.ExpiresAt - Now + 86_399) / 86_400);

    private static long TotalDays(VipGrant grant) => Math.Max(1, (grant.ExpiresAt - grant.GrantedAt + 43_200) / 86_400);

    // The share still to run. A grant from before VIPCore recorded its start is measured against a month.
    private static int Share(VipGrant grant)
    {
        if (grant.Permanent) return 100;
        var total = grant.GrantedAt > 0 ? grant.ExpiresAt - grant.GrantedAt : 30 * 86_400;
        return Directive.Bar(Directive.Percent(Math.Max(0, grant.ExpiresAt - Now), Math.Max(1, total)));
    }

    private static string Scope(PageDraw page, bool global) => T(page, global ? "scope.global" : "scope.local");

    // "+1 m": the largest unit the step is a whole number of.
    private static string Step(PageDraw page, TimeSpan? length)
    {
        if (length is not { } span || span <= TimeSpan.Zero) return T(page, "permanent");
        if (span.Ticks % TimeSpan.TicksPerDay == 0)
        {
            var days = (int)span.TotalDays;
            return days % 365 == 0 ? T(page, "step.y", days / 365)
                : days % 30 == 0 ? T(page, "step.mo", days / 30)
                : days % 7 == 0 ? T(page, "step.w", days / 7)
                : T(page, "step.d", days);
        }
        return span.Ticks % TimeSpan.TicksPerHour == 0
            ? T(page, "step.h", (int)span.TotalHours)
            : T(page, "step.min", Math.Max(1, (int)Math.Round(span.TotalMinutes)));
    }

    private static string Seen(PageDraw page, VipUserInfo user, bool full)
    {
        if (user.Online) return T(page, full ? "seen.online_now" : "seen.online");
        if (user.LastVisit <= 0) return T(page, "seen.never");
        var ago = TimeSpan.FromSeconds(Math.Max(0, Now - user.LastVisit));
        var text = ago.TotalHours < 1 ? T(page, "seen.minutes", Math.Max(1, (int)ago.TotalMinutes))
            : ago.TotalHours < 24 ? T(page, "seen.hours", (int)ago.TotalHours)
            : ago.TotalHours < 48 ? T(page, "seen.yesterday")
            : ago.TotalDays < 14 ? T(page, "seen.days", (int)ago.TotalDays)
            : T(page, "seen.weeks", (int)(ago.TotalDays / 7));
        return full ? T(page, "seen.last", text) : text;
    }

    private static string Date(PageDraw page, long unix) => Format(page, unix, "date_format", "d MMM yyyy");

    private static string ShortDate(PageDraw page, long unix) => Format(page, unix, "date_format_short", "d MMM");

    // The pattern is the translation's, so a language can drop English month names.
    private static string Format(PageDraw page, long unix, string key, string fallback)
    {
        var date = DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;
        var pattern = T(page, key);
        try
        {
            return date.ToString(pattern.Length > 0 ? pattern : fallback, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return date.ToString(fallback, CultureInfo.InvariantCulture);
        }
    }

    private static object? TagOf(IMenuComponent component) => (component as MenuComponentBase)?.Tag;

    private static string TextOf(IMenuComponent component) => (component as TextComponent)?.Text ?? string.Empty;

    private static string T(PageDraw page, string key, params object[] args) => page.Text("customhud.vip." + key, args);
}
