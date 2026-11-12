using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Admins.Contract;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Menu;
using SwiftlyS2.Shared.Menu.Components;
using SwiftlyS2.Shared.Players;

namespace CustomHUD.Integrations.AdminPages;

// Admins' !admin menu as its designed pages: the players as a roster with a sheet beside it, a
// player's actions as tiles, a punishment's steps as a stepper over chips, the bans as a table, and
// a vote's ballot as the compact poll. The menus are Admins' own; every row carries an
// Admins.Contract record as its Tag, and what changes while a page is open is read through the API.
//
// Soft dependency: no type here may hold an Admins.Contract enum or struct in a field, a closure
// included, or the assembly stops loading on servers without Admins (tools/SoftDependencyCheck).
internal sealed partial class AdminsIntegration(
    IAdminMenuAPI menus, IAdminsManager admins, IGroupsManager groups, IBansManager bans, ICommsManager comms, ISwiftlyCore core) : IPageIntegration
{
    private const long HistoryCacheMs = 15_000;

    // The players the viewer's menu lists, read once per draw; game thread only, like the history cache.
    private IReadOnlyList<IPlayer>? _listed;
    private readonly Dictionary<ulong, (Task<HistoryLine[]> Task, long At)> _history = [];

    public string Provider => "Admins";

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IPageIntegration? TryCreate(IInterfaceManager interfaces, ISwiftlyCore core)
    {
        ApiProbe.Require(typeof(IAdminMenuAPI), "Admins 2.0.0", nameof(IAdminMenuAPI.GetListedPlayers),
            nameof(IAdminMenuAPI.GetPlayerState), nameof(IAdminMenuAPI.IsMapChangePending));
        ApiProbe.Require(typeof(IBansManager), "Admins 2.0.0", nameof(IBansManager.CountBansAsync));
        ApiProbe.Require(typeof(IAdminVote), "Admins 2.0.0", nameof(IAdminVote.Options));
        ApiProbe.Require(typeof(IAdminDraft), "Admins 2.0.0", nameof(IAdminDraft.TypedInChat));
        ApiProbe.Require(typeof(AdminMenuIds), "Admins 2.0.0", nameof(AdminMenuIds.PlayerSessions));
        ApiProbe.Require(typeof(IBan), "Admins 2.0.0", nameof(IBan.Status));
        ApiProbe.Require(typeof(ISanction), "Admins 2.0.0", nameof(ISanction.Status));
        ApiProbe.Require(typeof(AdminRecordCounts), "Admins 2.0.0", nameof(AdminRecordCounts.Lifted));
        return interfaces.TryGetSharedInterface<IAdminMenuAPI>("Admins.Menu.V2", out var menus) && menus is not null
               && interfaces.TryGetSharedInterface<IAdminsManager>("Admins.Admins.V2", out var admins) && admins is not null
               && interfaces.TryGetSharedInterface<IGroupsManager>("Admins.Groups.V2", out var groups) && groups is not null
               && interfaces.TryGetSharedInterface<IBansManager>("Admins.Bans.V2", out var bans) && bans is not null
               && interfaces.TryGetSharedInterface<ICommsManager>("Admins.Comms.V2", out var comms) && comms is not null
            ? new AdminsIntegration(menus, admins, groups, bans, comms, core)
            : null;
    }

    public bool Owns(IMenu menu) => menu.Id.StartsWith("admins.", StringComparison.Ordinal);

    // A punishment's last two buttons sit under its rail, so they take no main-list slot.
    public string? RegionOf(IMenu menu, IMenuComponent component)
        => menu.Tag is AdminFlowPage && TagOf(component) is AdminActionRow { Action: AdminAction.Confirm or AdminAction.Summary or AdminAction.PrintCommand }
            ? "detailbar"
            : null;

    // A section heading, a table head, a quote: the most any page adds to its main list. A player's sessions page
    // heads each of its three lists.
    public int RowsAdded(IMenu menu) => menu.Tag is AdminFlowPage ? 2 : menu.Id == AdminMenuIds.PlayerSessions ? 3 : 1;

    public void Draw(PageDraw page)
    {
        _listed = null;
        var id = page.Menu.Id;
        switch (page.Menu.Tag)
        {
            case IAdminVote vote:
                Ballot(page, vote);
                return;
            case AdminFlowPage flow:
                Chips(page);
                Flow(page, flow);
                return;
            case IAdminRecordList list:
                Records(page, list);
                return;
            case AdminRecordRow record when id is AdminMenuIds.BanRecord or AdminMenuIds.CommsRecord:
                Chips(page);
                Record(page, record);
                return;
            case AdminPlayerRow target when id.StartsWith(AdminMenuIds.Players + ".", StringComparison.Ordinal):
                Chips(page);
                PlayerPages(page, target);
                return;
        }

        if (id == AdminMenuIds.Players)
        {
            Chips(page);
            Players(page);
        }
        else if (id == AdminMenuIds.Tools)
        {
            Chips(page);
            Tools(page);
        }
        else if (id.StartsWith(AdminMenuIds.Server, StringComparison.Ordinal))
        {
            Chips(page);
            Server(page);
        }
    }

    // ---- My tools: what an admin turns on for themselves, each with its state

    private void Tools(PageDraw page)
    {
        page.Hint(T(page, "hint.tools"));
        var state = menus.GetPlayerState(page.Player);
        for (var k = 0; k < page.Components.Count; k++)
        {
            switch (TagOf(page.Components[k]))
            {
                case AdminActionRow { Action: AdminAction.Esp }:
                    Toggle(page, k, "esp", "crosshair", state.Esp);
                    break;
                case AdminActionRow { Action: AdminAction.Hide }:
                    Toggle(page, k, "hide", "lock", state.Hidden);
                    break;
                case AdminActionRow { Action: AdminAction.Noclip }:
                    Toggle(page, k, "noclip", "movement", state.Noclip);
                    break;
                case AdminActionRow { Action: AdminAction.God }:
                    Toggle(page, k, "god", "shield", state.God);
                    break;
                case AdminActionRow { Action: AdminAction.Respawn }:
                    page.Decorate(k, $"glyph=refresh;sub={T(page, "tool.respawn.sub")}");
                    break;
            }
        }
    }

    private static void Toggle(PageDraw page, int k, string key, string glyph, bool on)
        => page.Draw(k, $"{T(page, "tool." + key)}\t{T(page, on ? "on" : "off")}",
            $"glyph={glyph};sub={T(page, "tool." + key + ".sub")}" + (on ? ";chosen" : ""));

    // ---- Server: the round, the weapons on the floor, the map

    private void Server(PageDraw page)
    {
        page.Hint(T(page, "hint.server"));
        ChipNotices(page, "chip=warn;glyph=info");
        var map = Directive.Clean(core.Engine.GlobalVars.MapName.Value);
        for (var k = 0; k < page.Components.Count; k++)
        {
            switch (TagOf(page.Components[k]))
            {
                case AdminActionRow { Action: AdminAction.RestartRound }:
                    page.Decorate(k, $"glyph=restart;sub={T(page, "server.restart.sub")}");
                    break;
                case AdminActionRow { Action: AdminAction.CleanWeapons }:
                    page.Decorate(k, $"glyph=weapons;sub={T(page, "server.clean.sub")}");
                    break;
                case AdminActionRow { Action: AdminAction.ChangeMap }:
                    page.Decorate(k, $"glyph=maps;sub={T(page, "server.map.sub", map)}");
                    break;
                case AdminRestartRow:
                    page.Decorate(k, "glyph=timer");
                    break;
                case AdminMapRow row:
                    page.Decorate(k, "glyph=maps" + (string.Equals(row.Map, map, StringComparison.OrdinalIgnoreCase)
                        ? $";chosen;note={T(page, "server.map.current")};notetone=accent"
                        : ""));
                    break;
                case AdminActionRow { Action: AdminAction.OtherMap }:
                    page.Decorate(k, $"glyph=edit;note={T(page, "in_chat")};notetone=faint");
                    break;
                case AdminActionRow { Action: AdminAction.ConfirmMap }:
                    page.Decorate(k, "glyph=maps;danger");
                    break;
            }
        }
    }

    // ---- The ballot: the question over its answers, who called it, the turnout below

    private void Ballot(PageDraw page, IAdminVote vote)
    {
        var options = vote.Options(page.Player);
        var kick = vote.Kind == AdminVoteKind.Kick;
        var map = vote.Kind == AdminVoteKind.Map;

        // Yes | No side by side; a map vote is MapChooser's board, a longer poll a plain list.
        var view = map ? "maps-board" : options.Count <= 3 ? "mix-ballot" : "list";
        var timer = TitleLine(page) is { } title ? NoticeLines(page, title) : [];
        foreach (var i in timer) page.DecorateHeader(i, $"chip=warn;glyph=stopwatch;view={view}");
        if (timer.Count == 0) page.AddHeader($"{vote.SecondsLeft}\t{T(page, "ballot.left")}", $"chip=warn;glyph=stopwatch;view={view}");

        if (page.Regions.Contains("banner"))
        {
            page.Add(T(page, "ballot.called", Directive.Clean(vote.StartedBy)),
                $"at=banner;avatar={Initial(vote.StartedBy)};sub={T(page, "ballot.kind." + KindKey(vote.Kind))}", 0);
            if (kick && vote.Target is { } target)
            {
                var payload = "at=banner;avatar";
                if (Live(page, target) is { } live)
                {
                    var facts = FactsOf(live);
                    payload += $";sub={T(page, "ballot.target", facts.Kills, facts.Deaths)}";
                    if (TeamTag(page, facts.Team) is { } tag) payload += $";tag={tag};tagtone={TeamTone(facts.Team)}";
                }
                page.Add(Directive.Clean(target.Name), payload, 0);
            }
        }

        // A board or a long poll shows the tally so far; Yes | No leaves it to the turnout below.
        var shares = map || view == "list";
        for (var k = 0; k < page.Components.Count; k++)
        {
            if (TagOf(page.Components[k]) is not AdminVoteOptionRow option) continue;
            if (kick)
            {
                page.Decorate(k, option.Index == 0 ? "glyph=check;tone=good" : "glyph=cancel;tone=bad");
                continue;
            }

            // A map's art comes from its name, and a mark would cover it.
            var parts = new List<string>(3);
            if (!map) parts.Add($"mark={option.Index + 1}");
            if (shares)
            {
                var votes = vote.Votes(option.Index);
                parts.Add($"bar={Directive.Bar(Directive.Percent(votes, vote.Voted))}");
                parts.Add($"note={T(page, "ballot.votes", votes)}");
            }
            if (parts.Count > 0) page.Decorate(k, string.Join(';', parts));
        }

        var yes = kick ? vote.Votes(0) : vote.Voted;
        var nays = kick ? $";nays={vote.Votes(1)}" : "";
        page.Add(T(page, "ballot.voted", vote.Voted, vote.Voters), $"at=foot;pips={yes}/{Math.Max(1, vote.Voters)}{nays}");
        page.Add(T(page, "ballot.rule." + KindKey(vote.Kind)), "at=foot;glyph=info");
    }

    private static string KindKey(AdminVoteKind kind) => kind switch
    {
        AdminVoteKind.Kick => "kick",
        AdminVoteKind.Map => "map",
        _ => "poll",
    };

    // ---- shared

    private void Chips(PageDraw page)
    {
        page.AddHeader($"{Listed(page).Count} / {core.Engine.GlobalVars.MaxClients}\t{T(page, "chip.players")}", "chip;glyph=player");
        page.AddHeader($"{AdminGroup(page, page.Player.SteamID) ?? T(page, "unknown")}\t{T(page, "chip.group")}", "chip;glyph=shield");
    }

    // Of an admin's groups here, the one with the highest immunity; null for someone who is no admin.
    private string? AdminGroup(PageDraw page, ulong steamId)
        => steamId == 0 || admins.GetAdmin(steamId) is not { } admin ? null
            : groups.GetAdminGroups(admin).MaxBy(g => g.Immunity)?.Name is { Length: > 0 } name ? Directive.Clean(name)
            : T(page, "admin");

    // Admins puts a page's notices (left the server, still loading) on header lines, which join the
    // title; drawn as chips they keep the title the page's name.
    private static void ChipNotices(PageDraw page, string payload)
    {
        if (TitleLine(page) is not { } title) return;
        foreach (var i in NoticeLines(page, title)) page.DecorateHeader(i, payload);
    }

    private static string? TitleLine(PageDraw page)
        => page.Menu.GetComponents(MenuRegion.Header).OfType<TitleComponent>().FirstOrDefault()?.Text.Trim();

    private static List<int> NoticeLines(PageDraw page, string title)
    {
        var lines = new List<int>();
        for (var i = 0; i < page.HeaderLines.Count; i++)
        {
            var line = page.HeaderLines[i].Trim();
            if (line != title && line.Any(char.IsLetterOrDigit)) lines.Add(i);
        }
        return lines;
    }

    private IReadOnlyList<IPlayer> Listed(PageDraw page) => _listed ??= menus.GetListedPlayers(page.Player);

    // Null once they have left: a reconnect is a new session, so a new row.
    private IPlayer? Live(PageDraw page, AdminPlayerRow row)
        => row.SessionId == 0 ? null : Listed(page).FirstOrDefault(p => p.SessionId == row.SessionId);

    private sealed record Facts(Team Team, int Kills, int Deaths, bool Bot);

    private static Facts FactsOf(IPlayer player)
    {
        var controller = player.Controller;
        if (controller is not { IsValid: true }) return new(Team.None, 0, 0, player.IsFakeClient);
        var tracking = controller.ActionTrackingServices;
        var stats = tracking is { IsValid: true } ? tracking.MatchStats : null;
        return new((Team)controller.TeamNum, stats?.Kills ?? 0, stats?.Deaths ?? 0, player.IsFakeClient);
    }

    private static string? TeamTag(PageDraw page, Team team) => team switch
    {
        Team.CT => T(page, "team.ct"),
        Team.T => T(page, "team.t"),
        Team.Spectator => T(page, "team.spec"),
        _ => null,
    };

    private static string TeamTone(Team team) => team switch
    {
        Team.CT => "ct",
        Team.T => "t",
        _ => "dark",
    };

    private string? Status(PageDraw page, IPlayer player, bool full)
    {
        var gag = comms.GetActiveSanction(player, SanctionKind.Gag);
        var mute = comms.GetActiveSanction(player, SanctionKind.Mute);
        if (gag is not null && mute is not null)
        {
            var end = gag.ExpiresAt == 0 || mute.ExpiresAt == 0 ? 0 : Math.Max(gag.ExpiresAt, mute.ExpiresAt);
            return T(page, "status.silenced", Left(page, end));
        }
        if (mute is not null)
            return full ? T(page, "status.muted_by", Directive.Clean(mute.AdminName), Left(page, mute.ExpiresAt)) : T(page, "status.muted", Left(page, mute.ExpiresAt));
        if (gag is not null)
            return full ? T(page, "status.gagged_by", Directive.Clean(gag.AdminName), Left(page, gag.ExpiresAt)) : T(page, "status.gagged", Left(page, gag.ExpiresAt));

        var state = menus.GetPlayerState(player);
        return state.Frozen ? T(page, "status.frozen")
            : state.God ? T(page, "status.god")
            : state.Noclip ? T(page, "status.noclip")
            : state.Beacon ? T(page, "status.beacon")
            : state.Drugged ? T(page, "status.drugged")
            : state.Hidden ? T(page, "status.hidden")
            : null;
    }

    // ---- history: a player's bans, gags and mutes on every server, newest first

    private sealed record HistoryLine(string Kind, long Id, long Length, string Reason, string Admin, long CreatedAt, long ExpiresAt, bool Global);

    // Null until loaded. Shared for a moment between pages, so walking a player's pages queries it once.
    private HistoryLine[]? History(PageDraw page, ulong steamId)
        => steamId == 0 ? [] : page.TryLoad("admins.history." + steamId, () => HistoryTask(steamId), out var lines) ? lines : null;

    private Task<HistoryLine[]> HistoryTask(ulong steamId)
    {
        var now = Environment.TickCount64;
        if (_history.TryGetValue(steamId, out var cached) && now - cached.At < HistoryCacheMs && !cached.Task.IsFaulted)
            return cached.Task;
        if (_history.Count > 64)
            foreach (var stale in _history.Where(h => now - h.Value.At >= HistoryCacheMs).Select(h => h.Key).ToList())
                _history.Remove(stale);

        var id = (long)steamId;
        var banned = bans.FindBansAsync(id);
        var sanctioned = comms.FindSanctionsAsync(id);
        var task = Task.WhenAll(banned, sanctioned).ContinueWith(_ => Merge(banned.Result, sanctioned.Result), TaskScheduler.Default);
        _history[steamId] = (task, now);
        return task;
    }

    private static HistoryLine[] Merge(IReadOnlyList<IBan> banned, IReadOnlyList<ISanction> sanctioned)
        =>
        [
            .. banned.Select(b => new HistoryLine("ban", b.Id, b.Length, b.Reason, b.AdminName, b.CreatedAt, b.ExpiresAt, b.GlobalBan))
                .Concat(sanctioned.Select(s => new HistoryLine(s.SanctionKind == SanctionKind.Gag ? "gag" : "mute", s.Id, s.Length,
                    s.Reason, s.AdminName, s.CreatedAt, s.ExpiresAt, s.GlobalSanction)))
                .OrderByDescending(l => l.CreatedAt),
        ];

    // "Ban 12 d", "Perm ban": one unit, since the label shares its row with the reason and the note.
    private static string HistoryText(PageDraw page, HistoryLine line)
    {
        var kind = T(page, "kind." + line.Kind);
        if (line.Length <= 0 || line.ExpiresAt == 0)
            return T(page, "history.permanent", kind);

        var span = TimeSpan.FromMilliseconds(line.Length);
        var length = span.TotalDays >= 14 ? T(page, "unit.week", (int)Math.Round(span.TotalDays / 7))
            : span.TotalDays >= 1 ? T(page, "unit.day", (int)Math.Round(span.TotalDays))
            : span.TotalHours >= 1 ? T(page, "unit.hour", (int)Math.Round(span.TotalHours))
            : T(page, "unit.minute", Math.Max(1, (int)Math.Round(span.TotalMinutes)));
        return $"{kind} {length}";
    }

    private static string HistoryPayload(PageDraw page, HistoryLine line, string at)
        => $"{at}tag={Reason(page, line.Reason)};note={Directive.Clean(line.Admin)} · {Ago(page, line.CreatedAt)};tone={(Active(line.ExpiresAt) ? "bad" : "faint")}"
           + (line.Global ? ";glyph=globe" : "");

    // ---- time

    private static long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static bool Active(long expiresAt) => expiresAt == 0 || expiresAt > NowMs;

    // Time left of something ending at a Unix ms; 0 is permanent.
    private static string Left(PageDraw page, long expiresAt)
        => expiresAt == 0 ? T(page, "time.permanent") : Short(page, TimeSpan.FromMilliseconds(Math.Max(0, expiresAt - NowMs)));

    private static string Short(PageDraw page, TimeSpan span)
    {
        if (span.TotalDays >= 1)
            return span.Hours > 0 ? T(page, "time.days_hours", (int)span.TotalDays, span.Hours) : T(page, "time.days", (int)span.TotalDays);
        if (span.TotalHours >= 1)
            return span.Minutes > 0 ? T(page, "time.hours_minutes", (int)span.TotalHours, span.Minutes) : T(page, "time.hours", (int)span.TotalHours);
        return T(page, "time.minutes", Math.Max(1, (int)Math.Ceiling(span.TotalMinutes)));
    }

    private static string Long(PageDraw page, TimeSpan span)
    {
        if (span <= TimeSpan.Zero) return T(page, "len.permanent");
        var (count, unit) = span.Ticks % TimeSpan.TicksPerDay == 0
            ? (int)span.TotalDays % 7 == 0 ? ((int)span.TotalDays / 7, "week") : ((int)span.TotalDays, "day")
            : span.Ticks % TimeSpan.TicksPerHour == 0 ? ((int)span.TotalHours, "hour")
            : (Math.Max(1, (int)Math.Round(span.TotalMinutes)), "minute");
        return T(page, $"len.{unit}.{(count == 1 ? "one" : "other")}", count);
    }

    private static string Ago(PageDraw page, long unixMs)
    {
        var ago = TimeSpan.FromMilliseconds(Math.Max(0, NowMs - unixMs));
        return ago.TotalHours < 1 ? T(page, "ago.minutes", Math.Max(1, (int)ago.TotalMinutes))
            : ago.TotalHours < 24 ? T(page, "ago.hours", (int)ago.TotalHours)
            : ago.TotalDays < 14 ? T(page, "ago.days", (int)ago.TotalDays)
            : ago.TotalDays < 60 ? T(page, "ago.weeks", (int)(ago.TotalDays / 7))
            : T(page, "ago.months", (int)(ago.TotalDays / 30));
    }

    // The translation's pattern, so a language can drop English month names; the server's own time zone.
    private static string Date(PageDraw page, long unixMs)
    {
        var date = DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToLocalTime().DateTime;
        var pattern = T(page, "date_format");
        try
        {
            return date.ToString(pattern.Length > 0 ? pattern : "d MMM yyyy, HH:mm", CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return date.ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture);
        }
    }

    // ---- text

    private static string Reason(PageDraw page, string? reason)
        => string.IsNullOrWhiteSpace(reason) ? T(page, "no_reason") : Directive.Clean(reason);

    private static string Initial(string? name)
        => name?.FirstOrDefault(char.IsLetterOrDigit) is { } c and not '\0' ? char.ToUpperInvariant(c).ToString() : "?";

    [GeneratedRegex(@"\[/?[a-zA-Z]+\]")]
    private static partial Regex ChatColor();

    // Admins' chat messages carry colour tags; a page shows their words.
    private static string Plain(string text) => Directive.Clean(ChatColor().Replace(text, string.Empty)).Trim();

    private static object? TagOf(IMenuComponent component) => (component as MenuComponentBase)?.Tag;

    private static object? FocusedTag(PageDraw page) => (page.Session.FocusedComponent as MenuComponentBase)?.Tag;

    private static string T(PageDraw page, string key, params object[] args) => page.Text("customhud.admins." + key, args);
}
