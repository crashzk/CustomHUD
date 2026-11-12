using System.Globalization;
using Admins.Contract;
using SwiftlyS2.Shared.Menu;
using SwiftlyS2.Shared.Players;

namespace CustomHUD.Integrations.AdminPages;

internal sealed partial class AdminsIntegration
{
    // The sheet's record stops here; the History page has the rest.
    private const int HistoryShown = 4;

    // ---- Players: the roster, the focused player's sheet beside it

    private void Players(PageDraw page)
    {
        var locker = page.Regions.Contains("detail");
        page.Hint(T(page, "hint.players"));
        ChipNotices(page, "chip;glyph=info");

        // A mouse player has no hover focus: this is the row they came back from.
        var focused = FocusedTag(page) as AdminPlayerRow;
        AdminPlayerRow? selected = null;
        for (var k = 0; k < page.Components.Count; k++)
        {
            switch (TagOf(page.Components[k]))
            {
                case AdminActionRow { Action: AdminAction.TeamFilter }:
                    page.Decorate(k, locker ? "at=foot;glyph=team" : "glyph=team");
                    break;
                case AdminPlayerRow row:
                    var chosen = locker && row.Equals(focused);
                    if (chosen) selected = row;
                    PlayerRow(page, k, row, chosen);
                    break;
            }
        }

        if (!locker) return;
        if (selected is not null) Sheet(page, selected, history: false);
        else page.Add(T(page, "pick_player"), $"at=detail;group=empty;glyph=player;sub={T(page, "pick_player.sub")}");
    }

    private void PlayerRow(PageDraw page, int k, AdminPlayerRow row, bool chosen)
    {
        var name = Directive.Clean(row.Name);
        var parts = new List<string>(12);
        if (Live(page, row) is not { } live)
        {
            parts.AddRange(["avatar", $"sub={T(page, "left")}", "subtone=faint"]);
            if (chosen) parts.Add("chosen");
            page.Draw(k, name, string.Join(';', parts));
            return;
        }

        var facts = FactsOf(live);
        var self = live.SessionId == page.Player.SessionId;
        parts.Add(facts.Bot ? $"avatar={T(page, "bot")}" : "avatar");
        if (TeamTag(page, facts.Team) is { } tag) parts.AddRange([$"tag={tag}", $"tagtone={TeamTone(facts.Team)}"]);
        if (self) parts.AddRange([$"tag2={T(page, "you")}", "tag2tone=accent"]);

        if (!self && !facts.Bot && !admins.CanTarget(page.Player, live.SteamID))
            parts.AddRange([$"sub={T(page, "immune", AdminGroup(page, live.SteamID) ?? T(page, "admin"))}", "subtone=faint"]);
        else if (Status(page, live, full: false) is { } status)
            parts.AddRange([$"sub={status}", "subtone=bad"]);

        if (chosen) parts.Add("chosen");
        page.Draw(k, $"{name}\t{facts.Kills} / {facts.Deaths}", string.Join(';', parts));
    }

    private void Sheet(PageDraw page, AdminPlayerRow row, bool history)
    {
        var live = Live(page, row);
        var facts = live is null ? null : FactsOf(live);
        var bot = facts?.Bot == true;

        var head = new List<string>(8) { "at=detail", "group=head", bot ? $"avatar={T(page, "bot")}" : "avatar" };
        if (facts is not null && TeamTag(page, facts.Team) is { } tag) head.AddRange([$"tag={tag}", $"tagtone={TeamTone(facts.Team)}"]);
        head.Add("sub=" + (bot ? T(page, "bot_sub") : row.SteamId64 != 0 ? row.SteamId64.ToString(CultureInfo.InvariantCulture) : T(page, "no_steamid")));
        var note = live is null ? T(page, "left")
            : !bot && live.SessionId != page.Player.SessionId && !admins.CanTarget(page.Player, row.SteamId64) ? T(page, "immune_to_you")
            : Status(page, live, full: true);
        if (note is not null) head.AddRange([$"note={note}", "notetone=bad"]);
        page.Add(Directive.Clean(row.Name), string.Join(';', head));

        var unknown = T(page, "unknown");
        page.Add($"{T(page, "stat.kd")}\t{(facts is null ? unknown : $"{facts.Kills} / {facts.Deaths}")}", "at=detail;group=stat");
        page.Add($"{T(page, "stat.group")}\t{AdminGroup(page, row.SteamId64) ?? unknown}", "at=detail;group=stat");

        if (!history || row.SteamId64 == 0) return;
        var lines = History(page, row.SteamId64);
        page.Add($"{T(page, "history")}\t{(lines is null ? T(page, "loading") : lines.Length.ToString(CultureInfo.InvariantCulture))}",
            "at=detail;section;group=history");
        foreach (var line in lines?.Take(HistoryShown) ?? [])
            page.Add(HistoryText(page, line), HistoryPayload(page, line, "at=detail;"));
    }

    // ---- One player's pages: what can be done as cards and tiles, their sheet beside

    private void PlayerPages(PageDraw page, AdminPlayerRow target)
    {
        var locker = page.Regions.Contains("detail");
        var id = page.Menu.Id;
        ChipNotices(page, "chip=warn;glyph=info");
        page.Hint(T(page, id switch
        {
            AdminMenuIds.Player => "hint.player",
            AdminMenuIds.PlayerHistory => "hint.history",
            AdminMenuIds.PlayerSessions => "hint.sessions",
            _ => "hint.actions",
        }));

        var live = Live(page, target);
        int first = -1, records = 0;
        string? list = null;
        for (var k = 0; k < page.Components.Count; k++)
        {
            var tag = TagOf(page.Components[k]);
            switch (tag)
            {
                case AdminPlayerGroupRow group:
                    GroupCard(page, k, group, target, locker);
                    break;
                case AdminPlayerActionRow action:
                    ActionTile(page, k, action, live, locker);
                    break;
                case AdminTeamRow team:
                    TeamTile(page, k, team, live, locker);
                    break;
                case AdminHealthRow health:
                    Tile(page, k, locker, health.Armor > 0 ? T(page, "hp.armor", health.Health) : T(page, "hp.only", health.Health),
                        health.Armor > 0 ? "armor" : "health");
                    break;
                case AdminMoneyRow money:
                    Tile(page, k, locker, T(page, money.Add ? "money.give" : "money.set", Directive.Number(money.Amount)), "money");
                    break;
                case AdminActionRow { Action: AdminAction.OtherAmount }:
                    Tile(page, k, locker, T(page, "act.other"), "edit");
                    break;
                case AdminRecordRow record:
                    records++;
                    var line = LineOf(record);
                    page.Draw(k, HistoryText(page, line), HistoryPayload(page, line, locker ? "group=record;" : ""));
                    break;
                case AdminPlaytimeRow playtime when locker:
                    Heading(page, k, ref list, "playtime");
                    // Month and Total end their lines of the grid locker_admins.vcss sizes.
                    var cell = playtime.Period switch
                    {
                        AdminPlaytimePeriod.Month => "playtime-end",
                        AdminPlaytimePeriod.Total => "playtime-total",
                        _ => "playtime",
                    };
                    page.Draw(k, $"{T(page, "playtime." + playtime.Period.ToString().ToLowerInvariant())}\t{Hours(page, playtime.Played)}", "group=" + cell);
                    break;
                case AdminSessionIpRow ip when locker:
                    Heading(page, k, ref list, "ips");
                    page.Draw(k, T(page, ip.Sessions == 1 ? "sessions.one" : "sessions.other", ip.Sessions),
                        $"group=record;tag={Directive.Clean(ip.Ip)};note={Ago(page, ip.LastSeen.ToUnixTimeMilliseconds())}");
                    break;
                case AdminSessionRow session when locker:
                    Heading(page, k, ref list, "sessions");
                    page.Draw(k, Hours(page, session.Played), SessionPayload(page, session));
                    break;
                case AdminPlaytimeRow or AdminSessionIpRow or AdminSessionRow:
                    page.Decorate(k, tag is AdminSessionIpRow ? "glyph=globe" : "glyph=clock");
                    break;
                case AdminActionRow { Action: AdminAction.Retry }:
                    page.Decorate(k, "glyph=refresh");
                    break;
                default:
                    continue;
            }
            if (first < 0 && tag is not AdminActionRow { Action: AdminAction.Retry }) first = k;
        }

        if (!locker) return;
        if (first >= 0 && page.Room > 0)
        {
            var section = id switch
            {
                AdminMenuIds.Player => T(page, "section.player"),
                AdminMenuIds.PlayerPunish => T(page, "group.punish"),
                AdminMenuIds.PlayerControl => T(page, "group.control"),
                AdminMenuIds.PlayerMove => T(page, "group.move"),
                AdminMenuIds.PlayerTeam => T(page, "section.team"),
                AdminMenuIds.PlayerHealth => T(page, "section.health"),
                AdminMenuIds.PlayerMoney => T(page, "section.money"),
                AdminMenuIds.PlayerHistory => $"{T(page, "history")}\t{records}",
                _ => "",
            };
            if (section.Length > 0) page.Add(section, "section", first);
        }
        Sheet(page, target, history: id != AdminMenuIds.PlayerHistory && HistoryAllowed(page.Menu));
    }

    private void GroupCard(PageDraw page, int k, AdminPlayerGroupRow row, AdminPlayerRow target, bool locker)
    {
        var (key, glyph) = row.Group switch
        {
            AdminPlayerGroup.Punish => ("punish", "ban"),
            AdminPlayerGroup.Control => ("control", "freeze"),
            AdminPlayerGroup.Move => ("move", "goto"),
            AdminPlayerGroup.Sessions => ("sessions", "stopwatch"),
            _ => ("history", "clock"),
        };
        if (!locker)
        {
            page.Decorate(k, "glyph=" + glyph);
            return;
        }

        var sub = row.Group == AdminPlayerGroup.History && History(page, target.SteamId64) is { } lines
            ? T(page, "group.history.count", lines.Length)
            : T(page, $"group.{key}.sub");
        page.Draw(k, T(page, "group." + key), $"group=group;glyph={glyph};sub={sub};submenu");
    }

    // An action as a tile: which way a two-way one goes now is read live, and an "on" one is lit.
    private void ActionTile(PageDraw page, int k, AdminPlayerActionRow row, IPlayer? live, bool locker)
    {
        var state = live is null ? default : menus.GetPlayerState(live);
        var muted = live is not null && comms.GetActiveSanction(live, SanctionKind.Mute) is not null;
        var gagged = live is not null && comms.GetActiveSanction(live, SanctionKind.Gag) is not null;
        var (key, glyph, on) = row.Action switch
        {
            AdminPlayerAction.Kick => ("kick", "kick", false),
            AdminPlayerAction.Ban => ("ban", "ban", false),
            AdminPlayerAction.Mute => muted ? ("unmute", "sounds", true) : ("mute", "mute", false),
            AdminPlayerAction.Gag => gagged ? ("ungag", "chat", true) : ("gag", "chat", false),
            AdminPlayerAction.Silence => ("silence", "sound-off", false),
            AdminPlayerAction.Slay => ("slay", "kill", false),
            AdminPlayerAction.Freeze => state.Frozen ? ("unfreeze", "freeze", true) : ("freeze", "freeze", false),
            AdminPlayerAction.Slap => ("slap", "slap", false),
            AdminPlayerAction.Respawn => ("respawn", "refresh", false),
            AdminPlayerAction.RespawnWhereDied => ("respawn_here", "cycle", false),
            AdminPlayerAction.Team => ("team", "team", false),
            AdminPlayerAction.Swap => ("swap", "swap", false),
            AdminPlayerAction.Beacon => ("beacon", "beacon", state.Beacon),
            AdminPlayerAction.Drug => ("drug", "colors", state.Drugged),
            AdminPlayerAction.God => ("god", "shield", state.God),
            AdminPlayerAction.Noclip => ("noclip", "movement", state.Noclip),
            AdminPlayerAction.Goto => ("goto", "goto", false),
            AdminPlayerAction.Bring => ("bring", "bring", false),
            AdminPlayerAction.Rename => ("rename", "rename", false),
            AdminPlayerAction.Health => ("health", "health", false),
            AdminPlayerAction.Money => ("money", "money", false),
            _ => ("", "info", false),
        };

        // An action newer than this build keeps Admins' own label.
        if (!locker || key.Length == 0)
        {
            page.Decorate(k, (locker ? "group=action;" : "") + "glyph=" + glyph);
            return;
        }
        page.Draw(k, T(page, "act." + key), $"group=action;glyph={glyph}" + (on ? ";chosen" : ""));
    }

    private static void TeamTile(PageDraw page, int k, AdminTeamRow row, IPlayer? live, bool locker)
    {
        var glyph = row.Team switch
        {
            Team.T => "t-logo",
            Team.CT => "ct-logo",
            _ => "player",
        };
        if (!locker)
        {
            page.Decorate(k, "glyph=" + glyph);
            return;
        }
        var current = live is not null && FactsOf(live).Team == row.Team;
        page.Draw(k, TeamTag(page, row.Team) ?? T(page, "team.spec"), $"group=action;glyph={glyph}" + (current ? ";chosen" : ""));
    }

    private static void Tile(PageDraw page, int k, bool locker, string label, string glyph)
    {
        if (locker) page.Draw(k, label, $"group=action;glyph={glyph}");
        else page.Decorate(k, "glyph=" + glyph);
    }

    // Only where the player's page offers their history: it needs the viewer's permission and the database.
    private static bool HistoryAllowed(IMenu menu)
    {
        var current = menu;
        for (var depth = 0; current is not null && depth < 8; depth++, current = current.Parent)
            if (current.Id == AdminMenuIds.Player)
                return current.GetComponents(MenuRegion.Body).Any(c => TagOf(c) is AdminPlayerGroupRow { Group: AdminPlayerGroup.History });
        return false;
    }

    // ---- A player's sessions: their playtime as stat tiles, then their addresses and connections as records

    // Heads each of the page's lists where it starts on this page; RowsAdded keeps room for all three.
    private static void Heading(PageDraw page, int k, ref string? current, string list)
    {
        if (current == list) return;
        current = list;
        var body = page.Menu.GetComponents(MenuRegion.Body);
        var title = T(page, "section." + list);
        page.Add(list switch
        {
            "ips" => $"{title}\t{body.Count(c => TagOf(c) is AdminSessionIpRow)}",
            "sessions" => $"{title}\t{body.Count(c => TagOf(c) is AdminSessionRow)}",
            _ => title,
        }, "section", k);
    }

    private static string SessionPayload(PageDraw page, AdminSessionRow session)
    {
        var when = Date(page, session.ConnectedAt.ToUnixTimeMilliseconds());
        if (session.Server is { } server) when += " · " + (server.Length > 0 ? Directive.Clean(server) : T(page, "another_server"));
        if (session.Online) when += " · " + T(page, "online");
        return $"group=record;tag={when};note={Directive.Clean(session.Ip)}";
    }

    // Playtime in hours however many there are: "52 h", "3 h 5 min", "40 min".
    private static string Hours(PageDraw page, TimeSpan span)
    {
        var hours = (int)span.TotalHours;
        return hours >= 10 || (hours > 0 && span.Minutes == 0) ? T(page, "time.hours", hours)
            : hours > 0 ? T(page, "time.hours_minutes", hours, span.Minutes)
            : T(page, "time.minutes", span.Minutes);
    }
}
