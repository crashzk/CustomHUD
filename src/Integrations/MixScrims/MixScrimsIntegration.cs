using System.Globalization;
using System.Runtime.CompilerServices;
using MixScrims.Contract;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Menu;
using SwiftlyS2.Shared.Players;

namespace CustomHUD.Integrations.MixPages;

// MixScrims' menus as their designed pages, every figure read from IMixScrims: the map vote as
// MapChooser's board, the knife round's side choice, and the mid-round ballots. MixScrims tags each
// row with what it stands for (a MapPoolEntry, a bool answer) and a vote kick's menu with its Team.
// IMixScrims is game-thread only, as Draw is.
//
// Soft dependency: no type here may hold a MixScrims.Contract enum or struct in a field, a closure
// or a cached lambda, or the assembly stops loading on servers without MixScrims
// (tools/SoftDependencyCheck). Its events are handled by instance methods for that reason.
internal sealed partial class MixScrimsIntegration : IPageIntegration, IDisposable
{
    private const string MapVote = "mixscrims.map-vote";
    private const string SidePick = "mixscrims.knife-side-pick";
    private const string TimeoutVote = "mixscrims.timeout-vote";
    private const string SurrenderVote = "mixscrims.surrender-vote";
    private const string VoteKick = "mixscrims.vote-kick";
    private const string TeamPick = "mixscrims.team-pick";
    private const string CaptainPick = "mixscrims.captain-pick";
    private const string Control = "mixscrims.control";
    private const string ChangeMap = "mixscrims.change-map";
    private const string MapPool = "mixscrims.map-pool";

    private readonly IMixScrims _api;
    private readonly ISwiftlyCore _core;
    private readonly IRankChips? _ranks;
    private readonly CancellationTokenSource _clock;

    private MixScrimsIntegration(IMixScrims api, ISwiftlyCore core, IRankChips? ranks)
    {
        _api = api;
        _core = core;
        _ranks = ranks;
        _api.MapVoteCast += OnMapVoteCast;
        _api.TimeoutVoteCast += OnTimeoutVoteCast;
        _api.SurrenderVoteCast += OnSurrenderVoteCast;
        _api.VoteKickCast += OnVoteKickCast;
        _api.TeamPickingStarted += OnTeamPickingStarted;
        _api.PlayerPickedForTeam += OnPlayerPickedForTeam;
        _api.CaptainAssignedBySlot += OnCaptainChanged;
        _api.CaptainRemovedBySlot += OnCaptainChanged;
        _api.PlayerReadyChanged += OnPlayerReadyChanged;
        // The chips count down, and a side vote raises no event; MixScrims never redraws a menu itself.
        _clock = core.Scheduler.RepeatBySeconds(1f, OnClock);
    }

    public string Provider => "MixScrims";

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IPageIntegration? TryCreate(IInterfaceManager interfaces, ISwiftlyCore core, IRankChips? ranks)
    {
        ApiProbe.Require(typeof(IMixScrims), "MixScrims 1.13.0",
            nameof(IMixScrims.GetKnifeRoundWinner), nameof(IMixScrims.GetStartingSideSecondsRemaining),
            nameof(IMixScrims.GetStartingSideVoteTally), nameof(IMixScrims.GetActiveTimeoutVoteTeam),
            nameof(IMixScrims.GetTimeoutVoteTally), nameof(IMixScrims.GetTimeoutVoteSecondsRemaining),
            nameof(IMixScrims.GetTimeoutVoteCaller), nameof(IMixScrims.GetSurrenderVoteSecondsRemaining),
            nameof(IMixScrims.GetSurrenderVoteCaller), nameof(IMixScrims.GetVoteKickSecondsRemaining),
            nameof(IMixScrims.GetVoteKickCaller), nameof(IMixScrims.GetPickOrder), nameof(IMixScrims.GetPickTimeoutSeconds),
            nameof(IMixScrims.GetPickSecondsRemaining), nameof(IMixScrims.GetPickedCtPlayerSlots),
            nameof(IMixScrims.GetPickedTPlayerSlots), nameof(IMixScrims.GetCtCaptainSlot), nameof(IMixScrims.GetTCaptainSlot),
            nameof(IMixScrims.GetCurrentPickIndex));
        // The contract declares no key constant; this is the one MixScrims publishes under.
        return interfaces.TryGetSharedInterface<IMixScrims>("MixScrims.API", out var api) && api is not null
            ? new MixScrimsIntegration(api, core, ranks)
            : null;
    }

    // Only the pages drawn here: every other MixScrims menu keeps core's own path. The panel's map
    // submenus are MapChooser's pool cards.
    public bool Owns(IMenu menu)
        => menu.Id is MapVote or SidePick or TimeoutVote or SurrenderVote or VoteKick or TeamPick or CaptainPick or Control
            or ChangeMap or MapPool;

    // The captain slots are the captain picker's banner cards; the panel's switches are its tiles.
    public string? RegionOf(IMenu menu, IMenuComponent component) => menu.Id switch
    {
        CaptainPick => component is MenuComponentBase { Tag: Team } ? "banner" : null,
        Control => ControlRegion(component),
        _ => null,
    };

    // The captain picker heads its players with a table head where its slots are banner cards; the
    // keyboard list has no banner and no head, so its rows stay the list's.
    public int RowsAdded(IMenu menu, IReadOnlySet<string> regions) => menu.Id == CaptainPick && regions.Contains("banner") ? 1 : 0;

    public void Draw(PageDraw page)
    {
        switch (page.Menu.Id)
        {
            case MapVote:
                DrawMapVote(page);
                break;
            case SidePick:
                DrawSides(page);
                break;
            case TeamPick:
                DrawDraft(page);
                break;
            case CaptainPick:
                DrawCaptains(page);
                break;
            case Control:
                DrawControl(page);
                break;
            case ChangeMap or MapPool:
                DrawMapPool(page);
                break;
            default:
                DrawBallot(page);
                break;
        }
    }

    public void Dispose()
    {
        _clock.Cancel();
        _clock.Dispose();
        _api.MapVoteCast -= OnMapVoteCast;
        _api.TimeoutVoteCast -= OnTimeoutVoteCast;
        _api.SurrenderVoteCast -= OnSurrenderVoteCast;
        _api.VoteKickCast -= OnVoteKickCast;
        _api.TeamPickingStarted -= OnTeamPickingStarted;
        _api.PlayerPickedForTeam -= OnPlayerPickedForTeam;
        _api.CaptainAssignedBySlot -= OnCaptainChanged;
        _api.CaptainRemovedBySlot -= OnCaptainChanged;
        _api.PlayerReadyChanged -= OnPlayerReadyChanged;
    }

    // ---- redraws: a cast at once, a running clock every second

    private void OnMapVoteCast(ulong voter, string map, string? previous) => Invalidate(static menu => menu.Id == MapVote);

    private void OnTeamPickingStarted(Team team) => Invalidate(static menu => menu.Id is TeamPick or Control);

    private void OnPlayerPickedForTeam(Team team, ulong steamId, int slot) => Invalidate(static menu => menu.Id is TeamPick or Control);

    private void OnCaptainChanged(Team team, int slot) => Invalidate(static menu => menu.Id is CaptainPick or Control);

    private void OnPlayerReadyChanged(ulong steamId, bool ready) => Invalidate(static menu => menu.Id is CaptainPick or Control);

    private void OnTimeoutVoteCast(ulong voter, bool yes, Team team) => Invalidate(static menu => menu.Id == TimeoutVote);

    private void OnSurrenderVoteCast(ulong voter, bool yes, Team team) => Invalidate(static menu => menu.Id == SurrenderVote);

    private void OnVoteKickCast(VoteKickCastEventArgs cast) => Invalidate(static menu => menu.Id == VoteKick);

    private void OnClock() => Invalidate(Ticking);

    private bool Ticking(IMenu menu) => menu.Id switch
    {
        MapVote => _api.GetMapVoteSecondsRemaining() > 0,
        SidePick => _api.GetStartingSideSecondsRemaining() > 0,
        TimeoutVote => _api.GetTimeoutVoteSecondsRemaining() > 0,
        SurrenderVote => _api.GetSurrenderVoteSecondsRemaining() > 0,
        VoteKick => menu.Tag is Team team && _api.GetVoteKickSecondsRemaining(team) > 0,
        TeamPick => _api.GetPickSecondsRemaining() > 0,
        // The panel follows the phase, the ready count and the clocks, none of which raises one event for all.
        Control => true,
        _ => false,
    };

    private void Invalidate(Func<IMenu, bool> which)
    {
        foreach (var player in _core.PlayerManager.GetAllPlayers())
            if (_core.Menu.GetSession(player) is { IsOpen: true } session && Owns(session.Menu) && which(session.Menu))
                session.Invalidate();
    }

    // ---- shared

    // MixScrims names players by SteamID; a name is known only while they are on the server.
    private IPlayer? Online(ulong steamId)
    {
        if (steamId == 0) return null;
        foreach (var player in _core.PlayerManager.GetAllPlayers())
            if (player.IsValid && player.SteamID == steamId) return player;
        return null;
    }

    // The name MixScrims gives the team on that side: its captain's, or the side's own.
    private string TeamName(Team side) => Directive.Clean(side == Team.CT ? _api.GetCtTeamName() : _api.GetTTeamName());

    private static Team Other(Team side) => side == Team.CT ? Team.T : Team.CT;

    private static string TeamTag(PageDraw page, Team side) => T(page, side == Team.CT ? "team.ct" : "team.t");

    private static string Tone(Team side) => side == Team.CT ? "ct" : "t";

    private static string Emblem(Team side) => side == Team.CT ? "ct-logo" : "t-logo";

    private static string Clock(int seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : $"{(int)time.TotalMinutes}:{time.Seconds:00}";
    }

    private static string T(PageDraw page, string key, params object[] args) => page.Text("customhud.mix." + key, args);
}
