namespace CustomHUD.Contract;

// Shared interface key "CustomHUD.Panels.v1". Draws a config-defined Panorama
// panel (scoreboard strip, ready counter, timeout banner ...) for one player.
//
// Safe from any thread: calls are marshalled onto the next game tick.
// BCL types only, so consumers need no SwiftlyS2 reference to compile against it.
public interface ICustomHudPanels
{
    const string InterfaceKey = "CustomHUD.Panels.v1";

    // Profile names from the server's CustomHUD config ("versus", "ready", "timeout").
    IReadOnlyCollection<string> Profiles { get; }

    // False before CustomHUD has loaded, or when the profile is unknown or has no
    // File; the draw itself happens next tick.
    // Variables missing from the call keep their previous value. Non-null classes
    // replace the set applied by the previous Show; null leaves it.
    // durationSeconds: null = profile default, <= 0 = until hidden.
    bool Show(
        int playerId,
        string profile,
        IReadOnlyDictionary<string, string> variables,
        IEnumerable<string>? classes = null,
        float? durationSeconds = null);

    void Hide(int playerId, string profile);

    // Every panel this player has open.
    void HideAll(int playerId);

    // This profile for every player.
    void HideEverywhere(string profile);
}