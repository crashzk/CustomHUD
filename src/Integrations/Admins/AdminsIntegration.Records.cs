using System.Globalization;
using Admins.Contract;

namespace CustomHUD.Integrations.AdminPages;

internal sealed partial class AdminsIntegration
{
    // A ban or a gag or mute the way the pages show it. BCL fields only: see the class comment.
    private sealed record RecordFacts(
        string Kind, long Id, string Name, string Mark, string Sub, string Ident, string Ip, bool ByIp,
        string Reason, string Admin, long CreatedAt, long Length, long ExpiresAt, bool Global);

    private static RecordFacts FactsOf(PageDraw page, AdminRecordRow row)
    {
        if (row.Ban is { } b)
            return RecordOf(page, "ban", b.Id, b.PlayerName, b.SteamId64, b.PlayerIp, b.BanType == BanType.IP, b.Reason, b.AdminName,
                b.CreatedAt, b.Length, b.ExpiresAt, b.GlobalBan);
        var s = row.Sanction!;
        return RecordOf(page, s.SanctionKind == SanctionKind.Gag ? "gag" : "mute", s.Id, s.PlayerName, s.SteamId64, s.PlayerIp,
            s.SanctionType == SanctionType.IP, s.Reason, s.AdminName, s.CreatedAt, s.Length, s.ExpiresAt, s.GlobalSanction);
    }

    // The SteamID or IP stands in for a name the record never got.
    private static RecordFacts RecordOf(PageDraw page, string kind, long id, string? name, long steamId, string? ip, bool byIp,
        string? reason, string? admin, long createdAt, long length, long expiresAt, bool global)
    {
        var ident = steamId > 0 ? steamId.ToString(CultureInfo.InvariantCulture) : ip ?? string.Empty;
        var named = !string.IsNullOrWhiteSpace(name) && name != "Unknown";
        return new RecordFacts(kind, id,
            Directive.Clean(named ? name : ident),
            byIp && steamId <= 0 ? "IP" : named ? Initial(name) : "?",
            byIp ? T(page, "by_ip") : named ? ident : T(page, "name_unknown"),
            ident, Directive.Clean(ip), byIp, Reason(page, reason), Directive.Clean(admin), createdAt, length, expiresAt, global);
    }

    private static HistoryLine LineOf(AdminRecordRow row)
        => row.Ban is { } b
            ? new("ban", b.Id, b.Length, b.Reason, b.AdminName, b.CreatedAt, b.ExpiresAt, b.GlobalBan)
            : new(row.Sanction!.SanctionKind == SanctionKind.Gag ? "gag" : "mute", row.Sanction.Id, row.Sanction.Length,
                row.Sanction.Reason, row.Sanction.AdminName, row.Sanction.CreatedAt, row.Sanction.ExpiresAt, row.Sanction.GlobalSanction);

    private static int Share(RecordFacts r)
        => r.Length <= 0 ? 100 : Directive.Bar(Directive.Percent(Math.Max(0, r.ExpiresAt - NowMs), r.Length));

    // ---- Bans and Comms: a table of records, the focused one's fields beside it

    private void Records(PageDraw page, IAdminRecordList list)
    {
        var locker = page.Regions.Contains("detail");
        var glyph = list.Comms ? "mute" : "ban";
        page.Hint(T(page, "hint.records"));
        ChipNotices(page, list.Failed ? "chip=warn;glyph=info" : list.Loading ? "chip;glyph=hourglass" : "chip;glyph=info");
        if (list.Counts is { } counts)
        {
            page.AddHeader($"{counts.Active}\t{T(page, "chip.active")}", "chip;glyph=" + glyph);
            page.AddHeader($"{counts.Expired}\t{T(page, "chip.expired")}", "chip;glyph=clock");
        }

        var focused = FocusedTag(page) as AdminRecordRow;
        AdminRecordRow? selected = null;
        var first = -1;
        for (var k = 0; k < page.Components.Count; k++)
        {
            switch (TagOf(page.Components[k]))
            {
                case AdminActionRow { Action: AdminAction.Give }:
                    page.Decorate(k, locker ? "at=foot;primary;glyph=add" : "glyph=add");
                    break;
                case AdminActionRow { Action: AdminAction.StatusFilter }:
                    page.Decorate(k, locker ? "at=foot;glyph=clock" : "glyph=clock");
                    break;
                case AdminActionRow { Action: AdminAction.KindFilter }:
                    page.Decorate(k, locker ? "at=foot;glyph=tag" : "glyph=tag");
                    break;
                case AdminActionRow { Action: AdminAction.Retry }:
                    page.Decorate(k, locker ? "at=foot;glyph=refresh" : "glyph=refresh");
                    break;
                case AdminRecordRow row:
                    if (first < 0) first = k;
                    var chosen = locker && row.Equals(focused);
                    if (chosen) selected = row;
                    RecordRow(page, k, row, chosen);
                    break;
            }
        }

        if (!locker || first < 0) return;
        if (page.Room > 0) page.Add($"#\t{T(page, "col.player")}\t{T(page, "col.reason")}\t{T(page, "col.ends")}", "head", first);
        if (selected is not null) RecordSheet(page, selected);
        else page.Add(T(page, "pick_record"), $"at=detail;group=empty;glyph={glyph};sub={T(page, "pick_record.sub")}");
    }

    private static void RecordRow(PageDraw page, int k, AdminRecordRow row, bool chosen)
    {
        var r = FactsOf(page, row);
        var parts = new List<string>(12) { $"avatar={r.Mark}", $"tag=#{r.Id}", "tagtone=dark", $"sub={r.Sub}" };
        parts.Add("type=" + (row.Sanction is null ? r.Reason : $"{T(page, "kind." + r.Kind)} · {r.Reason}"));
        if (r.ExpiresAt == 0)
        {
            parts.AddRange([$"note={T(page, "time.permanent")}", "notetone=bad"]);
        }
        else if (r.ExpiresAt > NowMs)
        {
            var share = Share(r);
            parts.AddRange([$"note={Left(page, r.ExpiresAt)}", $"bar={share}"]);
            if (share < 20) parts.Add("metertone=bad");
        }
        else
        {
            parts.AddRange([$"note={T(page, "ended")}", "notetone=faint", "tone=faint"]);
        }
        if (r.Global) parts.Add("glyph=globe");
        if (chosen) parts.Add("chosen");
        page.Draw(k, r.Name, string.Join(';', parts));
    }

    private static void RecordSheet(PageDraw page, AdminRecordRow row)
    {
        var r = FactsOf(page, row);
        page.Add($"{T(page, "kind." + r.Kind)} #{r.Id}", $"at=detail;avatar={r.Mark};group=head;sub={r.Name};note={r.Ident}");
        Field(page, "field.type", T(page, r.ByIp ? "match.ip" : "match.steamid"));
        if (r.Ip.Length > 0) Field(page, "field.ip", MaskIp(r.Ip));
        Field(page, "field.reason", r.Reason);
        Field(page, "field.admin", r.Admin.Length > 0 ? r.Admin : T(page, "console"));
        Field(page, "field.scope", T(page, r.Global ? "scope.global" : "scope.local"), r.Global ? "glyph=globe" : null);
        Field(page, "field.issued", Date(page, r.CreatedAt));
        Field(page, "field.length", r.Length <= 0 || r.ExpiresAt == 0 ? T(page, "time.permanent") : Long(page, TimeSpan.FromMilliseconds(r.Length)));
        if (r.ExpiresAt == 0) Field(page, "field.expires", T(page, "never"));
        else Field(page, r.ExpiresAt > NowMs ? "field.expires" : "field.ended", Date(page, r.ExpiresAt));
        if (r.ExpiresAt > NowMs) page.Add($"{T(page, "time_left")}\t{Left(page, r.ExpiresAt)}", $"at=detail;group=left;bar={Share(r)}");
    }

    private static void Field(PageDraw page, string key, string value, string? extra = null)
        => page.Add($"{T(page, key)}\t{value}", "at=detail;group=field" + (extra is null ? "" : ";" + extra));

    // Enough of the address to tell two apart, not enough to reach it.
    private static string MaskIp(string ip)
    {
        var parts = ip.Split('.');
        return parts.Length == 4 ? $"{parts[0]}.{parts[1]}.•••.•••" : ip;
    }

    // ---- A record's page: what can be done to it, its fields beside

    private static void Record(PageDraw page, AdminRecordRow record)
    {
        var locker = page.Regions.Contains("detail");
        page.Hint(T(page, "hint.record"));
        var first = -1;
        for (var k = 0; k < page.Components.Count; k++)
        {
            var payload = TagOf(page.Components[k]) switch
            {
                AdminActionRow { Action: AdminAction.Summary } => "glyph=chat",
                AdminActionRow { Action: AdminAction.Remove } => "glyph=trash;danger",
                AdminActionRow { Action: AdminAction.ChangeLength } => "glyph=clock",
                AdminActionRow { Action: AdminAction.OpenPlayer } => "glyph=player",
                _ => null,
            };
            if (payload is null) continue;
            if (first < 0) first = k;
            page.Decorate(k, payload);
        }

        if (!locker) return;
        if (first >= 0 && page.Room > 0) page.Add(T(page, "section.record"), "section", first);
        RecordSheet(page, record);
    }

    // ---- A punishment's steps: a stepper over the step's choices, the punishment as it stands beside

    private void Flow(PageDraw page, AdminFlowPage flow)
    {
        var draft = flow.Draft;
        var locker = page.Regions.Contains("detail");
        var kick = draft.Kind == AdminPunishment.Kick;
        var changing = draft.Record is not null;
        var step = StepKey(flow.Step);
        var typed = draft.TypedInChat;
        ChipNotices(page, "chip=warn;glyph=info");
        page.Hint(T(page, kick && flow.Step == AdminFlowStep.Reason ? "hint.flow.kick"
            : flow.Step == AdminFlowStep.Confirm && typed.Count > 0 ? "hint.flow.print" : "hint.flow." + step));

        TimeSpan? firstLength = null;
        var first = -1;
        for (var k = 0; k < page.Components.Count; k++)
        {
            switch (TagOf(page.Components[k]))
            {
                case AdminPlayerRow row:
                    if (first < 0) first = k;
                    PlayerRow(page, k, row, chosen: false);
                    break;
                case AdminRecentRow recent:
                    if (first < 0) first = k;
                    page.Draw(k, Directive.Clean(recent.Name),
                        $"avatar;sub={T(page, "left_ago", Ago(page, recent.LeftAt.ToUnixTimeMilliseconds()))};subtone=faint");
                    break;
                case AdminActionRow { Action: AdminAction.Recent }:
                    page.Decorate(k, "glyph=clock");
                    break;
                case AdminActionRow { Action: AdminAction.ById }:
                    page.Decorate(k, $"glyph=edit;note={T(page, "in_chat")};notetone=faint");
                    break;
                case AdminKindRow row:
                    if (first < 0) first = k;
                    page.Decorate(k, (locker ? "group=pick;" : "") + "glyph=" + KindGlyph(row.Kind));
                    break;
                case AdminReasonRow:
                    if (first < 0) first = k;
                    if (locker) page.Decorate(k, "group=pick2");
                    break;
                case AdminActionRow { Action: AdminAction.CustomReason or AdminAction.CustomLength }:
                    page.Decorate(k, (locker ? "group=pick2;" : "") + "glyph=chat");
                    break;
                case AdminLengthRow row:
                    if (first < 0) first = k;
                    firstLength ??= row.Length;
                    var permanent = row.Length == TimeSpan.Zero;
                    if (locker) page.Decorate(k, permanent ? "group=wide;glyph=lock;danger" : "group=length");
                    else if (permanent) page.Decorate(k, "glyph=lock;danger");
                    break;
                case AdminScopeRow row:
                    if (first < 0) first = k;
                    page.Decorate(k, (locker ? "group=pick2;" : "") + "glyph=" + (row.Global ? "globe" : "server"));
                    break;
                case AdminActionRow { Action: AdminAction.Match }:
                    if (first < 0) first = k;
                    page.Decorate(k, (locker ? "group=pick2;" : "") + "glyph=key");
                    break;
                case AdminActionRow { Action: AdminAction.Confirm }:
                    page.Decorate(k, "at=detailbar;danger");
                    break;
                case AdminActionRow { Action: AdminAction.Summary or AdminAction.PrintCommand }:
                    page.Decorate(k, "at=detailbar;glyph=chat");
                    break;
            }
        }

        if (!locker) return;

        var steps = draft.Steps;
        var current = 0;
        for (var i = 0; i < steps.Count; i++)
            if (steps[i] == flow.Step) current = i;

        if (first >= 0 && page.Room > 0) page.Add(T(page, "section." + step), "section", first);
        Stepper(page, draft, steps, current);

        // What the player will read, for the length in focus while picking one.
        var length = FocusedTag(page) is AdminLengthRow focused ? focused.Length : draft.Length ?? firstLength;
        if (!kick && !changing && length is { } shown && page.Room > 1
            && flow.Step is AdminFlowStep.Length or AdminFlowStep.Scope or AdminFlowStep.Confirm)
            Quote(page, draft, shown);

        Rail(page, draft, steps, current, kick, changing);
        if (!kick && !changing && flow.Step != AdminFlowStep.Confirm)
            page.Add(typed.Count > 0 ? T(page, "print_command") : T(page, "confirm." + KindKey(draft.Kind)),
                typed.Count > 0 ? "at=detailbar;glyph=chat;disabled" : "at=detailbar;danger;disabled");
    }

    // The banner holds five: a longer flow shows the five around the step it is on.
    private static void Stepper(PageDraw page, IAdminDraft draft, IReadOnlyList<AdminFlowStep> steps, int current)
    {
        if (steps.Count < 2) return;
        var start = Math.Clamp(current - 2, 0, Math.Max(0, steps.Count - 5));
        var end = Math.Min(steps.Count, start + 5);
        for (var i = start; i < end; i++)
        {
            var parts = new List<string>(6) { "at=banner", i == end - 1 ? "group=step-last" : "group=step" };
            if (i < current) parts.AddRange(["glyph=check", "tone=good", $"sub={StepValue(page, draft, steps[i])}"]);
            else if (i == current) parts.AddRange([$"mark={i + 1}", "chosen", $"sub={T(page, "pick_one")}"]);
            else parts.Add($"mark={i + 1}");
            page.Add(T(page, "step." + StepKey(steps[i])), string.Join(';', parts), 0);
        }
    }

    private static string StepValue(PageDraw page, IAdminDraft draft, AdminFlowStep step) => step switch
    {
        _ when draft.TypedInChat.Contains(step) => T(page, "typed"),
        AdminFlowStep.Target => draft.Target is { } target ? Directive.Clean(target.Name) : "—",
        AdminFlowStep.Kind => T(page, "kind." + KindKey(draft.Kind)),
        AdminFlowStep.Reason => draft.HasReason ? Reason(page, draft.Reason) : "—",
        AdminFlowStep.Length => draft.Length is { } length ? LengthText(page, length) : "—",
        AdminFlowStep.Scope => T(page, draft.Global ? "scope.global" : "scope.local"),
        _ => "",
    };

    private static void Quote(PageDraw page, IAdminDraft draft, TimeSpan length)
    {
        var text = Plain(draft.Preview(length));
        if (text.Length == 0) return;

        // A ban's message is several lines, which Admins joins with " · ": the first is the sentence.
        var cut = text.IndexOf(" · ", StringComparison.Ordinal);
        var who = draft.Target is { } target ? Directive.Clean(target.Name) : T(page, "they");
        var kind = KindKey(draft.Kind);
        var type = length == TimeSpan.Zero ? T(page, $"quote.{kind}.permanent", who) : T(page, "quote." + kind, who, Long(page, length));
        page.Add(cut > 0 ? text[..cut] : text, $"group=quote;type={type}" + (cut > 0 ? ";sub=" + text[(cut + 3)..] : ""));
    }

    private static void Rail(PageDraw page, IAdminDraft draft, IReadOnlyList<AdminFlowStep> steps, int current, bool kick, bool changing)
    {
        page.Add(changing ? T(page, "change_length") : T(page, "kind." + KindKey(draft.Kind)),
            $"at=detail;group=title;glyph={KindGlyph(draft.Kind)};tone=bad");

        var typed = draft.TypedInChat;
        if (draft.Target is { } target)
        {
            var ident = target.SteamId64 != 0 ? target.SteamId64.ToString(CultureInfo.InvariantCulture) : T(page, "by_ip");
            page.Add(Directive.Clean(target.Name), $"at=detail;avatar={Initial(target.Name)};group=target;sub={ident}");
        }
        else if (typed.Contains(AdminFlowStep.Target))
        {
            page.Add(T(page, "chat_target"), $"at=detail;avatar=?;group=target;sub={T(page, "typed")}");
        }
        else
        {
            page.Add(T(page, "no_target"), $"at=detail;avatar=?;group=target;sub={T(page, "pick_target")}");
        }

        if (changing && draft.Record is { } record)
        {
            var r = FactsOf(page, record);
            Pick(page, "field.record", $"#{r.Id}");
            Pick(page, "field.length", r.Length <= 0 || r.ExpiresAt == 0 ? T(page, "time.permanent") : Long(page, TimeSpan.FromMilliseconds(r.Length)));
            Pick(page, "field.new_length", T(page, "pick_one"), "tone=gold");
            return;
        }

        // Someone typed in chat gets both forms of the command, unless the admin asked to match by IP.
        var match = draft.ByIp ? "match.ip" : typed.Contains(AdminFlowStep.Target) ? "match.either" : "match.steamid";
        if (!kick) Pick(page, "field.match", T(page, match));
        Pending(page, "field.reason", draft.HasReason ? Reason(page, draft.Reason) : Typed(page, draft, AdminFlowStep.Reason), Index(steps, AdminFlowStep.Reason), current);
        if (kick) return;
        Pending(page, "field.length", draft.Length is { } length ? LengthText(page, length) : Typed(page, draft, AdminFlowStep.Length), Index(steps, AdminFlowStep.Length), current);

        // Settled from the start when the admin may give only one of the two.
        var scope = Index(steps, AdminFlowStep.Scope);
        Pending(page, "field.scope", scope < 0 || scope < current ? T(page, draft.Global ? "scope.global" : "scope.local") : null, scope, current);
    }

    private static string? Typed(PageDraw page, IAdminDraft draft, AdminFlowStep step)
        => draft.TypedInChat.Contains(step) ? T(page, "typed") : null;

    private static void Pick(PageDraw page, string key, string value, string? tone = null)
        => page.Add($"{T(page, key)}\t{value}", "at=detail;group=field" + (tone is null ? "" : ";" + tone));

    private static void Pending(PageDraw page, string key, string? value, int step, int current)
    {
        if (value is not null) Pick(page, key, value);
        else if (step == current) Pick(page, key, T(page, "pick_one"), "tone=gold");
        else Pick(page, key, "—", "tone=faint");
    }

    private static int Index(IReadOnlyList<AdminFlowStep> steps, AdminFlowStep step)
    {
        for (var i = 0; i < steps.Count; i++)
            if (steps[i] == step) return i;
        return -1;
    }

    private static string LengthText(PageDraw page, TimeSpan length) => length == TimeSpan.Zero ? T(page, "time.permanent") : Long(page, length);

    private static string StepKey(AdminFlowStep step) => step switch
    {
        AdminFlowStep.Target => "target",
        AdminFlowStep.Kind => "kind",
        AdminFlowStep.Reason => "reason",
        AdminFlowStep.Length => "length",
        AdminFlowStep.Scope => "scope",
        _ => "confirm",
    };

    private static string KindKey(AdminPunishment kind) => kind switch
    {
        AdminPunishment.Ban => "ban",
        AdminPunishment.Kick => "kick",
        AdminPunishment.Mute => "mute",
        AdminPunishment.Gag => "gag",
        _ => "silence",
    };

    private static string KindGlyph(AdminPunishment kind) => kind switch
    {
        AdminPunishment.Ban => "ban",
        AdminPunishment.Kick => "kick",
        AdminPunishment.Mute => "mute",
        AdminPunishment.Gag => "chat",
        _ => "sound-off",
    };
}
