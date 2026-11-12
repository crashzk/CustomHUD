using System.Collections.Concurrent;
using CustomHUD.Config;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace CustomHUD.Rendering;

// A one-line text HUD element per player, used by the native-text capture.
//
// Deliberately NOT built on the menu pipeline: a menu is a composed frame with
// focus and paging, this is a string with an expiry. Sharing the renderer would
// mean a captured center message and an open menu fighting over one entity.
//
// GAME THREAD ONLY.
internal sealed class HudSurface(MenuEntityHost entities, ILogger logger)
{
    private readonly ConcurrentDictionary<int, Surface> _surfaces = new();

    private sealed class Surface
    {
        public CHandle<CCSCustomHudLayout> Entity;
        public string LayoutPath = string.Empty;
        // The spawning profile's, so a hide needs no config: a later reload may name another layout.
        public string ShellPanel = string.Empty;
        public string HiddenClass = string.Empty;
        public string Text = string.Empty;
        public string KindClass = string.Empty;
        public DateTime ExpiresAt = DateTime.MinValue;
        public bool Visible;
    }

    public void Show(int slot, ulong steamId, NativeTextProfile profile, string layoutPath, NativeTextKind kind, string text)
    {
        try
        {
            var surface = _surfaces.GetOrAdd(slot, _ => new Surface());

            // StrLayout is fixed once an entity has spawned, so a profile
            // pointing at a different file is a replacement.
            if (!surface.Entity.IsValid || !string.Equals(surface.LayoutPath, layoutPath, StringComparison.Ordinal))
            {
                entities.Despawn(surface.Entity);
                surface.Entity = entities.Spawn(
                    steamId, slot, layoutPath, profile.ShellPanel, profile.HiddenClass);
                surface.LayoutPath = layoutPath;
                surface.ShellPanel = profile.ShellPanel;
                surface.HiddenClass = profile.HiddenClass;
                surface.Text = string.Empty;
                surface.KindClass = string.Empty;
                surface.Visible = false;
            }

            if (surface.Entity.Value is not { } entity) return;

            var kindClass = Format(profile.KindClass, kind.ToString().ToLowerInvariant());

            // Same diff-before-write rule the menu renderer follows: the native
            // center message is re-sent on a timer by some plugins, so an
            // unchanged string must not cost a write.
            if (!string.Equals(surface.Text, text, StringComparison.Ordinal))
            {
                entity.SetDialogVariableString(profile.TextPanel, profile.TextVariable, text);
                surface.Text = text;
            }

            if (!string.Equals(surface.KindClass, kindClass, StringComparison.Ordinal))
            {
                if (surface.KindClass.Length > 0)
                    SetClass(entity, profile.ShellPanel, surface.KindClass, false);
                SetClass(entity, profile.ShellPanel, kindClass, true);
                surface.KindClass = kindClass;
            }

            if (!surface.Visible)
            {
                SetClass(entity, profile.ShellPanel, profile.HiddenClass, false);
                surface.Visible = true;
            }

            surface.ExpiresAt = profile.DurationSeconds > 0f
                ? DateTime.UtcNow.AddSeconds(profile.DurationSeconds)
                // Left up until something replaces it.
                : DateTime.MaxValue;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[CustomHUD] Native text surface failed for slot {Slot}.", slot);
        }
    }

    // Swept on a timer rather than scheduled per message: a per-message timer
    // would have to be cancelled every time a new message replaced the old one.
    public void ExpireDue()
    {
        var now = DateTime.UtcNow;
        foreach (var (slot, surface) in _surfaces)
        {
            if (!surface.Visible || surface.ExpiresAt > now) continue;
            Hide(slot);
        }
    }

    public void Hide(int slot)
    {
        if (!_surfaces.TryGetValue(slot, out var surface)) return;
        if (surface.Entity.Value is not { } entity) return;

        try
        {
            SetClass(entity, surface.ShellPanel, surface.HiddenClass, true);
            surface.Visible = false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[CustomHUD] Native text hide failed for slot {Slot}.", slot);
        }
    }

    public void Drop(int slot)
    {
        if (!_surfaces.TryRemove(slot, out var surface)) return;
        entities.Despawn(surface.Entity);
        surface.Entity = default;
    }

    // Same reason the menu renderer re-denies on join: a transmit block only
    // covers clients connected when it was issued.
    public void DenyForJoiningClient(int viewerSlot)
    {
        foreach (var (ownerSlot, surface) in _surfaces)
        {
            if (ownerSlot == viewerSlot) continue;
            entities.DenyFor(surface.Entity, viewerSlot);
        }
    }

    public void Shutdown()
    {
        foreach (var slot in _surfaces.Keys)
            Drop(slot);
    }

    private static void SetClass(CCSCustomHudLayout entity, string panel, string className, bool on)
        => entity.SetHasClass(panel, className, on
            ? EHudPanelClassStatus_t.k_eHudPanelClassStatus_HasClass
            : EHudPanelClassStatus_t.k_eHudPanelClassStatus_DoesNotHaveClass);

    private static string Format(string template, string value)
        => template.Contains('{') ? string.Format(template, value) : template;
}
