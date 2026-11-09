using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CustomHUD.Config;
using CustomHUD.Navigation;
using SwiftlyS2.Shared.Menu;
using SwiftlyS2.Shared.Menu.Components;
using SwiftlyS2.Shared.Translation;

namespace CustomHUD.Rendering;

// MenuFrame -> FrameSnapshot. PURE: reads only the render context, the config
// and the cached player strings, touches no engine state, allocates nothing shared.
//
// This runs on IMenuRenderer's background thread. The moment anything in here
// reads a pawn, a controller or an entity, the renderer becomes a crash
// waiting for load. Keep it a function.
//
// Reads the provider rather than a captured config so a live reload reaches it;
// capturing CustomHudConfig here would pin the renderer id to load time and
// silently stop matching raw nodes after an operator renames it.
internal sealed partial class FrameComposer(CustomHudConfigProvider config, HudTranslations? strings)
{
    // Text after the first tab becomes the right-hand value, after the second
    // an icon token. Carried over from the private plugin so shop-style labels
    // keep working, and it costs a consumer nothing to adopt.
    private const char SegmentSeparator = '\t';

    public const int MaxRowSlots = 256;
    public const int MaxCardSlots = 32;

    public FrameSnapshot Compose(
        IMenuRenderContext context,
        MenuLayoutProfile profile,
        string layoutPath,
        NavMode mode,
        int scalePercent,
        ulong steamId,
        Action<MenuNode> reportUnsupported)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(profile.RowSlots, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(profile.RowSlots, MaxRowSlots);
        if (profile.Page is { } pageProfile)
            return ComposePage(context, profile, pageProfile, layoutPath, mode, scalePercent, steamId, reportUnsupported);
        var vars = ImmutableArray.CreateBuilder<VarWrite>();
        var classes = ImmutableArray.CreateBuilder<ClassWrite>();
        var frame = context.Frame;

        vars.Add(new VarWrite(
            profile.Variables.TitlePanel, profile.Variables.TitleVar,
            CleanTitle(FlattenRegion(frame.Header, reportUnsupported))));

        var entries = frame.Body.Select(node => Flatten(node, reportUnsupported)).Where(e => !e.Divider).ToArray();

        // frame.Footer is deliberately ignored: core runs the consumer's footer,
        // the focused row's hint and every key binding together, which is far
        // too long for a HUD strip and wrong for any mode core does not route.
        vars.Add(new VarWrite(
            profile.Variables.FooterPanel, profile.Variables.FooterVar,
            ComposeHint(context, profile, mode, viewOnly: !entries.Any(e => e.Selectable))));

        // Panorama renders the literal `{s:name}` token for any dialog variable
        // the server never writes, so an optional slot shows its own
        // placeholder to the player unless it is blanked explicitly.
        foreach (var slot in profile.BlankOnOpen)
            vars.Add(new VarWrite(slot, slot, string.Empty));

        var page = (context as FrameContext)?.Page;
        var tiles = mode == NavMode.Mouse && profile.PageTiles && page is { Total: > 1 } ? page : null;
        RowMap rows;
        int rowCount;
        if (profile.Card is { } card && entries is [{ Hero: true }, ..])
        {
            rowCount = 0;
            ComposeCard(profile, card, mode, context.Session.FocusedIndex, frame.PageOffset, entries, vars, classes, out rows);
        }
        else
        {
            rowCount = ComposeRows(frame, entries, profile, mode, context.Session.FocusedIndex,
                page?.FocusablesBefore ?? frame.PageOffset, tiles, vars, classes, out rows);
            if (profile.Card is { } hidden)
            {
                classes.Add(new ClassWrite(hidden.HeroPanel, profile.Classes.Hidden, true));
                for (var i = 0; i < Math.Clamp(hidden.Slots, 0, MaxCardSlots); i++)
                    classes.Add(new ClassWrite(hidden.Field(i), profile.Classes.Hidden, true));
            }
        }
        rows = rows with
        {
            Session = context.Session,
            BackButton = profile.Panels.Back,
            CloseButton = profile.Panels.Close,
            PreviousButton = profile.Panels.Previous,
            NextButton = profile.Panels.Next,
            PreviousPageFocus = page?.PreviousFocus ?? -1,
            NextPageFocus = page?.NextFocus ?? -1,
        };
        if (mode == NavMode.Mouse && profile.Sidebar is { } sidebar)
            rows = ComposeSidebar(context, profile, sidebar, rows, vars, classes);
        if (mode == NavMode.Mouse && profile.Tabs is { } tabs)
            rows = ComposeTabs(context, profile, TabStrip.Of(tabs), rows, vars, classes);

        // Without a Close button, Back stays visible on a root menu too: core's
        // Close() exits there, and it is a mouse player's only way out because
        // Tab is filtered. A menu whose plugin unbound Close shows neither.
        var closable = Closable(context.Menu);
        if (rows.BackButton.Length > 0)
            classes.Add(new ClassWrite(rows.BackButton, profile.Classes.Hidden,
                mode != NavMode.Mouse || !closable || (rows.CloseButton.Length > 0 && !HasBackTarget(context.Menu, rows))));
        if (rows.CloseButton.Length > 0)
            classes.Add(new ClassWrite(rows.CloseButton, profile.Classes.Hidden, mode != NavMode.Mouse || !closable));
        var paged = page is not null
            ? page.Total > 1
            : frame.PageOffset > 0
              || entries.Length > profile.RowSlots
              || rows.Focusable.Count(f => f) < frame.TotalItems;
        foreach (var panel in new[] { rows.PreviousButton, rows.NextButton })
            if (panel.Length > 0)
                classes.Add(new ClassWrite(panel, profile.Classes.Hidden, mode != NavMode.Mouse || !paged));
        ComposePageCounter(profile, page, vars, classes);

        // Only the current step is written; FrameSnapshot.DiffTo clears the old one.
        var shown = Math.Clamp(rowCount, 1, profile.RowSlots);
        classes.Add(new ClassWrite(profile.Panels.Body, Format(profile.Classes.RowCount, shown), true));

        if (profile.Classes.ScaleSteps.Contains(scalePercent))
            classes.Add(new ClassWrite(profile.Panels.Shell, Format(profile.Classes.Scale, scalePercent), true));

        // Revealed last, so a player never sees a frame mid-population. The
        // entity spawns with this class set.
        classes.Add(new ClassWrite(profile.Panels.Shell, profile.Classes.Hidden, false));

        return new FrameSnapshot(
            vars.ToImmutable(), classes.ToImmutable(), layoutPath,
            Fingerprint(profile), profile.Panels.Shell, profile.Classes.Hidden,
            steamId, rows, CaptureInput: mode == NavMode.Mouse);
    }

    // Identifies everything about a profile that changes the SET of writes a
    // frame produces -- not their values. A live config edit that shrinks
    // RowSlots or renames a panel leaves the dropped keys untouched by the
    // diff, so the apply treats a change here as a respawn and redraws blank.
    // Every field named by a VarWrite or ClassWrite must appear.
    private static string Fingerprint(MenuLayoutProfile p)
        => string.Join('|',
            p.RowSlots, p.ShowAllRows, p.PageTiles,
            p.Panels.Shell, p.Panels.Body, p.Panels.Title, p.Panels.Footer,
            p.Panels.Back, p.Panels.Close, p.Panels.Previous, p.Panels.Next,
            p.Sidebar?.Slots, p.Sidebar?.RootButton, p.Sidebar?.ButtonPrefix, p.Sidebar?.LabelVar, p.Sidebar?.DividerClass,
            p.Sidebar?.IconPanel, p.Sidebar?.IconClass,
            p.Card?.ModeClass, p.Card?.HeroPanel, p.Card?.HeroThumbPanel, p.Card?.HeroNameVar, p.Card?.HeroTypeVar,
            p.Card?.Slots, p.Card?.FieldPanel, p.Card?.LabelVar, p.Card?.ValueVar, p.Card?.KindClass, p.Card?.AsideClass,
            p.Card?.OnClass, p.Card?.AsideOnClass, p.Card?.TrackClass, p.Card?.FillClass, p.Card?.PresetClass,
            p.Variables.TitlePanel, p.Variables.TitleVar,
            p.Variables.FooterPanel, p.Variables.FooterVar,
            p.Variables.PagePanel, p.Variables.PageVar,
            p.Row.PanelId, p.Row.LabelVar, p.Row.ValueVar, p.Row.KeyVar, p.Row.IconPanel, p.Row.ThumbPanel, p.Row.TypeVar,
            p.Classes.Hidden, p.Classes.Selected, p.Classes.Disabled,
            p.Classes.Danger, p.Classes.Marquee, p.Classes.Thumb, p.Classes.Tint, p.Classes.HasThumb, p.Classes.HasType, p.Classes.PageTile,
            p.Classes.HasBar, p.Classes.Bar, p.Classes.Chosen,
            p.Classes.RowCount, p.Classes.Scale, p.Classes.Icon,
            string.Join(',', p.Classes.IconTokens),
            string.Join(',', p.Classes.ScaleSteps),
            string.Join(',', p.BlankOnOpen),
            p.Sidebar?.CountVar, p.Sidebar?.CountClass, p.Sidebar?.LiveClass,
            p.Tabs?.Slots, p.Tabs?.Panel, p.Tabs?.Prefix, p.Tabs?.LabelVar, p.Tabs?.CountVar,
            p.Tabs?.HasTabsClass, p.Tabs?.HasCountClass,
            RowFingerprint(p.Row),
            p.Page is { } page ? PageFingerprint(page) : "");

    private static string RowFingerprint(LayoutRowSpec r)
        => string.Join(',',
            r.PanelId, r.LabelVar, r.ValueVar, r.KeyVar, r.IconPanel, r.ThumbPanel, r.TypeVar,
            r.HitSuffix, r.InlineSuffix, r.SubVar, r.UnitVar, r.BadgeVar, r.TagVar, r.Tag2Var, r.NoteVar, r.MarkVar,
            r.GlyphPanel, r.ValueGlyphPanel, r.LogoPanel, r.TagGlyphPanel, r.ValueUnitVar, r.RatingVar, r.RatingMinorVar,
            r.DecSuffix, r.IncSuffix, r.SegmentSuffix, r.Segments);

    private static string PageFingerprint(MenuPageProfile p)
    {
        var c = p.Classes;
        var l = p.Legend;
        return string.Join('|',
            p.EyebrowVar, p.CrumbVar, p.ChipSlots, RowFingerprint(p.ChipRow),
            p.TabSlots, p.TabsPanel, p.TabPrefix, p.TabLabelVar, p.TabCountVar,
            p.HeadPanel, p.HeadVar, p.HeadSlots,
            string.Join(';', p.Regions.Select(r => r.Name + ':' + r.Slots + ':' + RowFingerprint(r.Row))),
            l is null ? "" : string.Join(',', l.Panel, l.Slots, l.SlotPanel, l.KeyVar, l.Key2Var, l.VerbVar,
                l.ShownClass, l.HasKey2Class, l.Dots, l.PagesClass, l.PageClass),
            string.Join(',', c.View, c.Region, c.HasTabs, c.HasHead, c.NoSidebar, c.Mode, c.HasCrumb, c.HasEyebrow, c.Tone, c.SubTone,
                c.BadgeTone, c.TagTone, c.Tag2Tone, c.NoteTone, c.MeterTone, c.Swatch, c.Avatar, c.AvatarColors, c.Rarity, c.Kind,
                c.On, c.Track, c.Rating, c.Pips, c.Lit, c.Yes, c.Section, c.Group, c.Lead, c.Primary, c.HasKey, c.HasSub, c.HasUnit,
                c.HasBadge, c.HasTag, c.HasTag2, c.HasNote, c.HasValue, c.HasGlyph, c.HasValueGlyph, c.HasTagGlyph, c.HasValueUnit, c.HasMark,
                c.HasInline, c.HasRating, c.HasPips, c.HasCount,
                c.Glyph, c.InlineState, c.HasBar2, c.Bar2));
    }

    private static void ComposePageCounter(
        MenuLayoutProfile profile, MenuPage? page,
        ImmutableArray<VarWrite>.Builder vars, ImmutableArray<ClassWrite>.Builder classes)
    {
        var v = profile.Variables;
        if (v.PageVar.Length == 0) return;

        // Core's own window (a Chat menu) has no page number to show.
        var shown = page is { Total: > 1 };
        vars.Add(new VarWrite(v.PagePanel, v.PageVar, shown ? FormatPage(v.PageFormat, page!.Index + 1, page.Total) : string.Empty));
        classes.Add(new ClassWrite(v.PagePanel, profile.Classes.Hidden, !shown));
    }

    private static string FormatPage(string format, int index, int total)
    {
        try { return string.Format(CultureInfo.InvariantCulture, format, index, total); }
        catch (FormatException) { return $"{index} / {total}"; }
    }

    private static RowMap ComposeSidebar(
        IMenuRenderContext context, MenuLayoutProfile profile, MenuSidebarProfile sidebar,
        RowMap rows, ImmutableArray<VarWrite>.Builder vars, ImmutableArray<ClassWrite>.Builder classes,
        IReadOnlyCollection<string>? live = null)
    {
        var root = SidebarRoot(context.Menu);
        var items = SidebarEntries(root, sidebar);
        var active = ActiveEntry(context.Menu, root, items, (context as FrameContext)?.RememberedEntry);
        for (var i = 0; i < Math.Clamp(sidebar.Slots, 0, 32); i++)
        {
            var panel = sidebar.ButtonPrefix + i;
            var item = i < items.Length ? items[i] : null;
            var label = StripAside((item as TextComponent)?.Text ?? "");
            if (sidebar.CountVar.Length > 0)
            {
                var (text, count) = SplitCount(label);
                vars.Add(new VarWrite(panel, sidebar.LabelVar, text));
                vars.Add(new VarWrite(panel, sidebar.CountVar, count));
                if (count.Length > 0 && sidebar.CountClass.Length > 0)
                    classes.Add(new ClassWrite(panel, sidebar.CountClass, true));
            }
            else
            {
                vars.Add(new VarWrite(panel, sidebar.LabelVar, label));
            }
            classes.Add(new ClassWrite(panel, profile.Classes.Hidden, item is null));
            if (item is DividerComponent && sidebar.DividerClass.Length > 0)
                classes.Add(new ClassWrite(panel, sidebar.DividerClass, true));
            if (item is TextComponent { Enabled: false })
                classes.Add(new ClassWrite(panel, profile.Classes.Disabled, true));
            if (i == active)
                classes.Add(new ClassWrite(panel, profile.Classes.Selected, true));
            if (live is { Count: > 0 } && sidebar.LiveClass.Length > 0 && item is SubmenuComponent { Id: { Length: > 0 } entryId }
                && live.Contains(entryId, StringComparer.OrdinalIgnoreCase))
                classes.Add(new ClassWrite(panel, sidebar.LiveClass, true));
            if (item is TextComponent entry && sidebar.Icon(i) is { } iconPanel && SidebarIcon(sidebar, entry) is { } icon)
                classes.Add(new ClassWrite(iconPanel, Format(sidebar.IconClass, icon), true));
        }
        var onRoot = ReferenceEquals(root, context.Menu);
        if (onRoot && sidebar.RootButton.Length > 0)
            classes.Add(new ClassWrite(sidebar.RootButton, profile.Classes.Selected, true));
        return rows with
        {
            RootMenu = root, RootButton = sidebar.RootButton, SidebarPrefix = sidebar.ButtonPrefix,
            SidebarItems = [.. items],
            SkipsRoot = sidebar.AutoOpen >= 0,
            AutoOpenItem = onRoot && sidebar.AutoOpen >= 0
                ? items.OfType<SubmenuComponent>().ElementAtOrDefault(sidebar.AutoOpen)
                : null,
        };
    }

    internal static IMenu SidebarRoot(IMenu menu)
    {
        var root = menu;
        var visited = new HashSet<IMenu> { root };
        while (root.Parent is { } parent && visited.Add(parent)) root = parent;
        return root;
    }

    // The root-menu entries the sidebar mirrors, in order. A root action (a Help button)
    // is listed only while the root's own page is skipped, the one case where nothing
    // else reaches it. Render runs off-thread, so an entry whose visibility, state or
    // text comes from a consumer's provider cannot be evaluated here and is left out.
    internal static IMenuComponent[] SidebarEntries(IMenu root, MenuSidebarProfile sidebar)
    {
        var components = root.GetComponents(MenuRegion.Body);
        var actions = sidebar.AutoOpen >= 0 && components.Any(IsPage);
        return components
            .Where(c => c is DividerComponent { Visible: true, VisibleWhen: null } || IsPage(c)
                || actions && c is ButtonComponent { Visible: true, VisibleWhen: null, EnabledWhen: null, TextProvider: null })
            .Take(Math.Clamp(sidebar.Slots, 0, 32))
            .ToArray();

        static bool IsPage(IMenuComponent c)
            => c is SubmenuComponent { Visible: true, VisibleWhen: null, EnabledWhen: null, TextProvider: null };
    }

    // The entry's icon token, or null to leave the layout's default icon. The
    // token becomes part of a class name, so only a plain one is accepted.
    private static string? SidebarIcon(MenuSidebarProfile sidebar, TextComponent entry)
    {
        string? token = null;
        if (entry.Id is { Length: > 0 } id) sidebar.Icons.TryGetValue(id, out token);
        if (token is null && entry.Text is { Length: > 0 } label
            && !sidebar.Icons.TryGetValue(label, out token))
            sidebar.Icons.TryGetValue(SplitCount(StripAside(label)).Label, out token);
        token = token?.Trim().ToLowerInvariant();
        return token is not null && PresetToken().IsMatch(token) ? token : null;
    }

    // A trailing aside ("Help (shown in chat)") has no room on the sidebar's one short
    // line, where the icon carries it; a count ("Knives (12)") stays for the badge.
    internal static string StripAside(string label)
    {
        var match = TrailingAside().Match(label);
        return match.Success ? match.Groups[1].Value : label;
    }

    [GeneratedRegex(@"^(.*\S)\s*\((?!\d{1,6}\))[^()]*\)$")]
    private static partial Regex TrailingAside();

    // The entry for the root's child the player is somewhere beneath: its
    // component id equals that menu's id, or failing that its label its title.
    // A plugin whose entries carry neither falls back to the entry this player
    // last opened through the sidebar.
    private static int ActiveEntry(IMenu menu, IMenu root, IMenuComponent[] items, (IMenu Root, int Index)? remembered)
    {
        var top = menu;
        var visited = new HashSet<IMenu> { top };
        while (top.Parent is { } parent && !ReferenceEquals(parent, root) && visited.Add(parent)) top = parent;
        if (!ReferenceEquals(top.Parent, root)) return -1;

        // An id names the entry only when no other entry shares it: ShopCore opens every
        // category under one menu id, so there the title decides.
        var byId = UniqueIndex(items, c => c is SubmenuComponent && string.Equals(c.Id, top.Id, StringComparison.OrdinalIgnoreCase));
        if (byId >= 0) return byId;
        var title = top.GetComponents(MenuRegion.Header).OfType<TitleComponent>().FirstOrDefault()?.Text;
        var byTitle = string.IsNullOrEmpty(title)
            ? -1
            : Array.FindIndex(items, c => c is SubmenuComponent s && string.Equals(s.Text, title, StringComparison.Ordinal));
        if (byTitle >= 0) return byTitle;
        if (!string.IsNullOrEmpty(title)
            && TitleMatch(title, [.. items.Select(c => (c as SubmenuComponent)?.Text)]) is var loose and >= 0)
            return loose;
        return remembered is { } memory && ReferenceEquals(memory.Root, root)
               && memory.Index >= 0 && memory.Index < items.Length && items[memory.Index] is SubmenuComponent
            ? memory.Index
            : -1;
    }

    // A skipped root or tab host is no destination: Back from its children would only
    // reopen the entry or tab it skips to.
    private static bool HasBackTarget(IMenu menu, RowMap rows)
        => menu.Parent is { } parent
           && !(rows.SkipsRoot && ReferenceEquals(parent, rows.RootMenu))
           && !(rows.SkipsTabHost && ReferenceEquals(parent, rows.TabHost));

    private int ComposeRows(
        MenuFrame frame,
        RowContent[] entries,
        MenuLayoutProfile profile,
        NavMode mode,
        int focusedIndex,
        int selectablesBefore,
        MenuPage? tiles,
        ImmutableArray<VarWrite>.Builder vars,
        ImmutableArray<ClassWrite>.Builder classes,
        out RowMap rows)
    {
        var row = profile.Row;
        var cls = profile.Classes;
        var focusedPhysicalRow = Array.FindIndex(entries, entry => entry.Focused);
        // Core's own window (a Chat menu) can be wider than the layout; walk it with a viewport.
        var offset = Math.Max(0, focusedPhysicalRow) / profile.RowSlots * profile.RowSlots;
        var lead = tiles is { Index: > 0 } ? 1 : 0;
        var trail = tiles is not null && tiles.Index < tiles.Total - 1 ? 1 : 0;
        var count = Math.Min(entries.Length - offset, profile.RowSlots - lead - trail);
        var used = lead + count + trail;
        var skippedSelectables = entries.Take(offset).Count(e => e.Selectable);

        var focusable = ImmutableArray.CreateBuilder<bool>(used);
        var focusedRow = -1;
        int previousTile = -1, nextTile = -1;
        // Body rows include non-focusable entries (spacers, progress bars), so
        // the Nth row and the Nth selectable thing are different numbers.
        // Numbering has to follow what the player can actually pick, or key 3
        // names a spacer.
        var focusOrdinal = 0;

        for (var i = 0; i < profile.RowSlots; i++)
        {
            var panel = row.Panel(i);
            var occupied = i < used;

            if (!occupied)
            {
                // Hidden is the only write a free slot needs: its text is never
                // seen, and the diff clears any class the last occupant had on.
                classes.Add(new ClassWrite(panel, cls.Hidden, true));
                continue;
            }

            if ((lead == 1 && i == 0) || (trail == 1 && i == used - 1))
            {
                var back = lead == 1 && i == 0;
                if (back) previousTile = i; else nextTile = i;
                focusable.Add(false);
                ComposePageTile(profile, vars, classes, i, back ? "«" : "»",
                    FormatPage(profile.Variables.PageFormat, tiles!.Index + (back ? 0 : 2), tiles.Total));
                continue;
            }

            var entry = entries[offset + i - lead];
            focusable.Add(entry.Selectable);
            if (entry.Focused) focusedRow = i;

            var parts = entry.Text.Split(SegmentSeparator);
            var label = parts[0];
            var value = parts.Length > 1 && parts[1].Length > 0 ? parts[1] + entry.ValueUnit : string.Empty;
            // An explicit raw-node icon beats the tab convention: a plugin that
            // went to the trouble of addressing this renderer meant it.
            var iconToken = entry.Icon ?? (parts.Length > 2 ? parts[2] : string.Empty);
            if (iconToken.Length == 0 && entry.Color is { } color
                && profile.ColorIcons.TryGetValue(color, out var colorIcon))
                iconToken = colorIcon;

            if (row.Value(i) is { } valueVar)
            {
                vars.Add(new VarWrite(panel, row.Label(i), label));
                vars.Add(new VarWrite(panel, valueVar, value));
            }
            else
            {
                // No right-hand slot in this layout: keep both halves on the
                // label rather than silently dropping the value.
                vars.Add(new VarWrite(panel, row.Label(i),
                    value.Length == 0 ? label : label + ' ' + value));
            }

            if (entry.Selectable) focusOrdinal++;

            if (row.Type(i) is { } typeVar)
            {
                vars.Add(new VarWrite(panel, typeVar, entry.Type ?? string.Empty));
                if (!string.IsNullOrEmpty(entry.Type))
                    classes.Add(new ClassWrite(panel, cls.HasType, true));
            }

            if (row.Key(i) is { } keyVar)
            {
                // Numbers mode binds keys to the CURRENT page's selectable rows,
                // so the hint must be the key that actually picks this row.
                // Core only populates Number under Chat input, so derive it.
                var number = entry.Selectable
                    ? mode == NavMode.Numbers
                        ? focusOrdinal
                        : entry.Number > 0 ? entry.Number
                        : selectablesBefore + skippedSelectables + focusOrdinal
                    : 0;
                vars.Add(new VarWrite(panel, keyVar, number > 0 ? number.ToString() : string.Empty));
            }

            classes.Add(new ClassWrite(panel, cls.Hidden, false));
            // Mouse and numbers have no persistent cursor -- mouse clicks
            // directly, numbers names its target -- so a highlight there would
            // point at a row the player never chose.
            if (entry.Focused && mode is NavMode.Wasd or NavMode.Buttons)
                classes.Add(new ClassWrite(panel, cls.Selected, true));
            if (entry.Disabled) classes.Add(new ClassWrite(panel, cls.Disabled, true));
            if (entry.Danger) classes.Add(new ClassWrite(panel, cls.Danger, true));
            if (entry.Marquee) classes.Add(new ClassWrite(panel, cls.Marquee, true));
            if (entry.HasBar)
            {
                if (cls.HasBar.Length > 0) classes.Add(new ClassWrite(panel, cls.HasBar, true));
                if (cls.Bar.Length > 0) classes.Add(new ClassWrite(panel, Format(cls.Bar, entry.Bar), true));
            }
            if (entry.Chosen && cls.Chosen.Length > 0) classes.Add(new ClassWrite(panel, cls.Chosen, true));
            ApplyIcon(profile, classes, i, iconToken);
            ApplyThumb(profile, classes, i, entry.Image, entry.Tint);
        }

        rows = new RowMap(focusable.ToImmutable(), focusedRow, focusedIndex, row.PanelPrefix)
        {
            FirstRow = frame.PageOffset + offset,
            PreviousTileRow = previousTile,
            NextTileRow = nextTile,
        };
        return used;
    }

    // Written like an entry so a tile left in a slot clears the same way.
    private static void ComposePageTile(
        MenuLayoutProfile profile,
        ImmutableArray<VarWrite>.Builder vars,
        ImmutableArray<ClassWrite>.Builder classes,
        int index,
        string arrow,
        string page)
    {
        var row = profile.Row;
        var panel = row.Panel(index);
        if (row.Value(index) is { } valueVar)
        {
            vars.Add(new VarWrite(panel, row.Label(index), arrow));
            vars.Add(new VarWrite(panel, valueVar, page));
        }
        else
        {
            vars.Add(new VarWrite(panel, row.Label(index), arrow + ' ' + page));
        }
        if (row.Type(index) is { } typeVar) vars.Add(new VarWrite(panel, typeVar, string.Empty));
        if (row.Key(index) is { } keyVar) vars.Add(new VarWrite(panel, keyVar, string.Empty));
        classes.Add(new ClassWrite(panel, profile.Classes.Hidden, false));
        if (profile.Classes.PageTile.Length > 0)
            classes.Add(new ClassWrite(panel, profile.Classes.PageTile, true));
        ApplyIcon(profile, classes, index, string.Empty);
        ApplyThumb(profile, classes, index, null, null);
    }

    private static readonly HashSet<string> ControlKinds = new(StringComparer.Ordinal) { "input", "toggle", "slider", "choice", "selector", "progress" };

    // A frame whose first entry is a hero, drawn as one card. The hero fills the
    // card's head; each later entry takes a field, except one marked `inline`,
    // which joins the field before it: a slider as its track, anything else as
    // its trailing control. Rows stay mapped by body index so the input bridge
    // can move focus to the entry a field's click target names.
    private static void ComposeCard(
        MenuLayoutProfile profile,
        MenuCardProfile card,
        NavMode mode,
        int focusedIndex,
        int pageOffset,
        RowContent[] entries,
        ImmutableArray<VarWrite>.Builder vars,
        ImmutableArray<ClassWrite>.Builder classes,
        out RowMap rows)
    {
        var cls = profile.Classes;
        for (var i = 0; i < profile.RowSlots; i++)
            classes.Add(new ClassWrite(profile.Row.Panel(i), cls.Hidden, true));
        classes.Add(new ClassWrite(profile.Panels.Shell, card.ModeClass, true));

        var hero = entries[0];
        vars.Add(new VarWrite(card.HeroPanel, card.HeroNameVar, hero.Text.Split(SegmentSeparator)[0]));
        vars.Add(new VarWrite(card.HeroPanel, card.HeroTypeVar, hero.Type ?? string.Empty));
        classes.Add(new ClassWrite(card.HeroPanel, cls.Hidden, false));
        if (!string.IsNullOrEmpty(hero.Type)) classes.Add(new ClassWrite(card.HeroPanel, cls.HasType, true));
        var heroIcon = hero.Icon ?? (hero.Color is { } color && profile.ColorIcons.TryGetValue(color, out var colorIcon) ? colorIcon : "");
        if (cls.IconTokens.Contains(heroIcon)) classes.Add(new ClassWrite(card.HeroPanel, Format(cls.Icon, heroIcon), true));
        var thumb = string.IsNullOrWhiteSpace(hero.Image) ? null : ThumbToken(hero.Image);
        if (thumb is not null)
        {
            classes.Add(new ClassWrite(card.HeroThumbPanel, Format(cls.Thumb, thumb), true));
            classes.Add(new ClassWrite(card.HeroPanel, cls.HasThumb, true));
        }
        classes.Add(new ClassWrite(card.HeroThumbPanel, cls.Hidden, thumb is null));

        var slots = Math.Clamp(card.Slots, 0, MaxCardSlots);
        var main = Enumerable.Repeat(-1, slots).ToArray();
        var aside = Enumerable.Repeat(-1, slots).ToArray();
        var track = Enumerable.Repeat(-1, slots).ToArray();
        var field = -1;
        for (var k = 1; k < entries.Length; k++)
        {
            var entry = entries[k];
            if (entry.Inline && field >= 0 && field < slots)
            {
                var into = entry.Kind == "slider" ? track : aside;
                if (into[field] < 0)
                {
                    into[field] = k;
                    continue;
                }
            }
            if (++field >= slots) break;
            main[field] = k;
            if (entry.Kind == "slider") track[field] = k;
        }

        for (var i = 0; i < slots; i++)
        {
            var panel = card.Field(i);
            if (main[i] < 0)
            {
                classes.Add(new ClassWrite(panel, cls.Hidden, true));
                continue;
            }

            var entry = entries[main[i]];
            var parts = entry.Text.Split(SegmentSeparator);
            var value = entry.Kind == "slider" && entry.ValueText is { } readout ? readout
                : parts.Length > 1 && parts[1].Length > 0 ? parts[1] + entry.ValueUnit : string.Empty;
            vars.Add(new VarWrite(panel, card.Label(i), parts[0]));
            vars.Add(new VarWrite(panel, card.Value(i), value));
            classes.Add(new ClassWrite(panel, cls.Hidden, false));
            classes.Add(new ClassWrite(panel, Format(card.KindClass, KindOf(entry)), true));
            if (entry.Kind == "toggle" && entry.On) classes.Add(new ClassWrite(panel, card.OnClass, true));
            if (entry.Focused && mode is NavMode.Wasd or NavMode.Buttons)
                classes.Add(new ClassWrite(panel, cls.Selected, true));
            if (entry.Disabled) classes.Add(new ClassWrite(panel, cls.Disabled, true));
            if (entry.Danger) classes.Add(new ClassWrite(panel, cls.Danger, true));

            if (aside[i] >= 0)
            {
                var control = entries[aside[i]];
                classes.Add(new ClassWrite(panel, Format(card.AsideClass, KindOf(control)), true));
                if (control.Kind == "toggle" && control.On) classes.Add(new ClassWrite(panel, card.AsideOnClass, true));
            }

            if (track[i] >= 0)
            {
                var slider = entries[track[i]];
                classes.Add(new ClassWrite(panel, card.TrackClass, true));
                classes.Add(new ClassWrite(panel, Format(card.FillClass, Math.Clamp(slider.Percent, 0, 100)), true));
                if (slider.Track is { } preset) classes.Add(new ClassWrite(panel, Format(card.PresetClass, preset), true));
            }
        }

        rows = new RowMap([.. entries.Select(e => e.Selectable)], Array.FindIndex(entries, e => e.Focused), focusedIndex, card.FieldPrefix)
        {
            FirstRow = pageOffset,
            Card = new CardMap([.. main], [.. aside], [.. track],
                card.HitSuffix, card.AsideSuffix, card.SegmentSuffix, card.Segments),
        };
    }

    private static string KindOf(RowContent entry)
        => entry.Kind is { } kind && ControlKinds.Contains(kind) ? kind
            : entry.Selectable && !entry.Info ? "action" : "text";

    private static void ApplyIcon(
        MenuLayoutProfile profile,
        ImmutableArray<ClassWrite>.Builder classes,
        int index,
        string token)
    {
        if (profile.Row.Icon(index) is not { } iconPanel) return;

        var known = token.Length > 0 && profile.Classes.IconTokens.Contains(token);
        if (known)
            classes.Add(new ClassWrite(iconPanel, Format(profile.Classes.Icon, token), true));

        // Collapse rather than leave a hole where the badge would be.
        classes.Add(new ClassWrite(iconPanel, profile.Classes.Hidden, !known));
    }

    private static void ApplyThumb(
        MenuLayoutProfile profile,
        ImmutableArray<ClassWrite>.Builder classes,
        int index,
        string? imageKey,
        string? tint)
    {
        if (profile.Row.Thumb(index) is not { } thumbPanel) return;

        var token = string.IsNullOrWhiteSpace(imageKey) ? null : ThumbToken(imageKey);
        if (token is not null)
        {
            classes.Add(new ClassWrite(thumbPanel, Format(profile.Classes.Thumb, token), true));
            classes.Add(new ClassWrite(profile.Row.Panel(index), profile.Classes.HasThumb, true));
            if (tint is not null && profile.Classes.Tint.Length > 0)
                classes.Add(new ClassWrite(thumbPanel, Format(profile.Classes.Tint, tint), true));
        }
        classes.Add(new ClassWrite(thumbPanel, profile.Classes.Hidden, token is null));
    }

    private static readonly ConcurrentDictionary<string, string> ThumbTokens = new(StringComparer.Ordinal);

    // Must match scripts/generate-econ-thumbs.ps1. A short hash rather than the
    // key itself: every distinct class name is networked once per HUD entity.
    internal static string ThumbToken(string imageKey)
    {
        var key = imageKey.Trim().ToLowerInvariant();
        static string Hash(string k) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(k)))[..12];
        return ThumbTokens.Count < 8192 ? ThumbTokens.GetOrAdd(key, Hash) : Hash(key);
    }

    private static readonly Dictionary<string, string> FallbackHints = CustomHudConfig.DefaultHints();

    private string ComposeHint(IMenuRenderContext context, MenuLayoutProfile profile, NavMode mode, bool viewOnly)
    {
        var menu = context.Menu;
        // Core only routes the keymap on Buttons menus, so a buttons-mode
        // player on a Chat menu is really typing !N / !0.
        var key = mode == NavMode.Buttons && menu.InputMethod == MenuInputMethod.Chat
            ? "chat"
            : NavModeResolver.Name(mode);
        var localizer = strings?.For(context.Player.PlayerID);

        // A menu with nothing to select can only be closed: "<mode>.view" says so. Under an
        // operator's mode hint a translated view hint would mix their text with ours, so theirs is kept.
        string? template = null;
        if (viewOnly && Hint(key + ".view", localizer) is { } view
            && (Override(key) is null || Override(key + ".view") is not null))
            template = view;
        template ??= Hint(key, localizer);
        if (string.IsNullOrEmpty(template) || template.IndexOf('{') < 0) return template ?? string.Empty;

        var keymap = menu.Keymap;
        // Mouse mode closes with a button, not a key: the layout's × where it has one, else <.
        var close = mode == NavMode.Mouse
            ? profile.Panels.Close.Length > 0 ? "×" : "<"
            : MenuKeys.Describe(keymap.GetKey(MenuActions.Close));
        return template
            .Replace("{up}", MenuKeys.Describe(keymap.GetKey(MenuActions.NavigateUp)), StringComparison.OrdinalIgnoreCase)
            .Replace("{down}", MenuKeys.Describe(keymap.GetKey(MenuActions.NavigateDown)), StringComparison.OrdinalIgnoreCase)
            .Replace("{select}", MenuKeys.Describe(keymap.GetKey(MenuActions.Select)), StringComparison.OrdinalIgnoreCase)
            .Replace("{close}", close, StringComparison.OrdinalIgnoreCase);
    }

    private string? Hint(string key, ILocalizer? localizer)
        => Override(key)
           ?? (localizer is null ? null : HudTranslations.Find(localizer, "customhud.hint." + key))
           ?? FallbackHints.GetValueOrDefault(key);

    // An operator's entry wins in every language. One still holding the English default
    // counts as unset, because older builds wrote every default into config.jsonc.
    private string? Override(string key)
        => config.Current.Hints.TryGetValue(key, out var template) && template is not null
           && template != FallbackHints.GetValueOrDefault(key)
            ? template
            : null;

    private static readonly char[] RuleEdges = "-=_~*|•·─━—– \t".ToCharArray();

    // Core's DividerComponent is a run of '─' in the header, and CenterHTML-era
    // titles pad themselves ("----- Stats -----"); the layout already frames the
    // title, so both only eat width. "{0} - Hit Stats" keeps its lone hyphen, and
    // a prompt's trailing colon ("Vote for the next map:") goes: a heading needs none.
    private static string CleanTitle(string title)
    {
        if (title.Length == 0) return title;
        var s = Rule().Replace(title, " ").Trim(RuleEdges).TrimEnd(':', '：').TrimEnd();
        return Spaces().Replace(s, " ");
    }

    [GeneratedRegex(@"[\-=_~*|•·─━—–]{3,}")]
    private static partial Regex Rule();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();

    // A chip or a page hint is a header node with a place of its own on a page; a
    // plain list has neither, so it keeps them out of the title.
    private string FlattenRegion(IReadOnlyList<MenuNode> region, Action<MenuNode> reportUnsupported)
    {
        if (region.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        foreach (var node in region)
        {
            var flat = Flatten(node, reportUnsupported);
            if (flat.Chip || flat.Hint || flat.Text.Length == 0) continue;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(flat.Text);
        }
        return sb.ToString();
    }

    private RowContent Flatten(MenuNode node, Action<MenuNode> reportUnsupported)
    {
        var sb = new StringBuilder();
        var content = new RowContent();
        Walk(node, sb, ref content, reportUnsupported);
        content.Text = sb.ToString();
        return content;
    }

    private void Walk(MenuNode node, StringBuilder sb, ref RowContent content, Action<MenuNode> reportUnsupported)
    {
        switch (node)
        {
            case MenuTextNode text:
                sb.Append(text.Text);
                content.Color ??= text.Style.Color;
                break;

            case MenuLineNode line:
                foreach (var child in line.Children)
                    Walk(child, sb, ref content, reportUnsupported);
                break;

            case MenuStackNode stack:
                // A row is one line. Joining with a space keeps a multi-line
                // component readable instead of running its lines together.
                for (var i = 0; i < stack.Children.Count; i++)
                {
                    if (i > 0 && sb.Length > 0) sb.Append(' ');
                    Walk(stack.Children[i], sb, ref content, reportUnsupported);
                }
                break;

            case MenuBlankNode:
                // Vertical space has no meaning in a fixed row ladder, and the
                // row is already allocated. Leave it empty but visible.
                break;

            case MenuSelectionNode selection:
                // The marker slot itself draws nothing -- the layout shows
                // focus with a CSS class, not a glyph. Its PRESENCE is the only
                // signal that this body entry can take focus at all, which is
                // what lets the input bridge map a clicked row to a cursor
                // move: non-focusable entries carry no marker.
                content.Selectable = true;
                content.Focused |= selection.Focused;
                if (selection.Number > 0) content.Number = selection.Number;
                break;

            case MenuRawNode raw when IsOwnRaw(raw.RendererId):
                ApplyRawPayload(raw.Payload, sb, ref content);
                break;

            case MenuRawNode:
                // Aimed at another renderer. Ignoring it is correct, and it is
                // not an unsupported node.
                break;

            default:
                // New node types can appear in a future core release. Report
                // and skip; a renderer that throws takes the menu down.
                reportUnsupported(node);
                break;
        }
    }

    private bool IsOwnRaw(string rendererId)
        => string.Equals(rendererId, config.Current.RendererId, StringComparison.OrdinalIgnoreCase)
        || string.Equals(rendererId, CustomHudConfig.DefaultRendererId, StringComparison.OrdinalIgnoreCase);

    // Opt-in richness for cooperating plugins: a component emits
    // `new MenuRawNode(rendererId, "danger")` or `"icon=7"` for rows that
    // deserve special treatment, and generic nodes otherwise. Unknown
    // directives are ignored so a payload aimed at a newer CustomHUD never
    // breaks an older one.
    private static void ApplyRawPayload(string payload, StringBuilder sb, ref RowContent content)
    {
        foreach (var token in payload.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.StartsWith("text=", StringComparison.OrdinalIgnoreCase))
                sb.Append(token[5..]);
            else if (token.StartsWith("icon=", StringComparison.OrdinalIgnoreCase))
                content.Icon = token[5..];
            else if (token.StartsWith("image=", StringComparison.OrdinalIgnoreCase))
                content.Image = token[6..];
            else if (token.StartsWith("type=", StringComparison.OrdinalIgnoreCase))
                content.Type = token[5..];
            else if (token.Equals("danger", StringComparison.OrdinalIgnoreCase))
                content.Danger = true;
            else if (token.Equals("disabled", StringComparison.OrdinalIgnoreCase))
                content.Disabled = true;
            else if (token.Equals("marquee", StringComparison.OrdinalIgnoreCase))
                content.Marquee = true;
            else if (token.Equals("chosen", StringComparison.OrdinalIgnoreCase))
                content.Chosen = true;
            else if (token.Equals("hero", StringComparison.OrdinalIgnoreCase))
                content.Hero = true;
            else if (token.Equals("divider", StringComparison.OrdinalIgnoreCase))
                content.Divider = true;
            else if (token.Equals("inline", StringComparison.OrdinalIgnoreCase))
                content.Inline = true;
            else if (token.StartsWith("kind=", StringComparison.OrdinalIgnoreCase) && ControlKinds.Contains(token[5..]))
                content.Kind = token[5..];
            else if (token.StartsWith("on=", StringComparison.OrdinalIgnoreCase))
                content.On = token[3..] == "1";
            else if (token.StartsWith("pct=", StringComparison.OrdinalIgnoreCase)
                     && int.TryParse(token.AsSpan(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent))
                content.Percent = percent;
            else if (token.StartsWith("num=", StringComparison.OrdinalIgnoreCase))
                content.ValueText = token[4..];
            else if (token.StartsWith("bar=", StringComparison.OrdinalIgnoreCase)
                     && int.TryParse(token.AsSpan(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var bar))
            {
                content.HasBar = true;
                content.Bar = Math.Clamp(bar, 0, 100);
            }
            // Becomes part of a class name, so only a plain token is accepted.
            else if (token.StartsWith("track=", StringComparison.OrdinalIgnoreCase) && PresetToken().IsMatch(token[6..]))
                content.Track = token[6..];
            else if (token.StartsWith("tint=", StringComparison.OrdinalIgnoreCase) && PresetToken().IsMatch(token[5..]))
                content.Tint = token[5..];
            else
                ApplyPageDirective(token, ref content);
        }
    }

    [GeneratedRegex("^[a-z0-9-]{1,24}$")]
    private static partial Regex PresetToken();

    private static string Format(string template, object value)
        => template.Contains('{') ? string.Format(template, value) : template;

    private struct RowContent
    {
        public string Text;
        public bool Focused;
        public bool Selectable;
        public int Number;
        public bool Danger;
        public bool Disabled;
        public bool Marquee;
        public string? Icon;
        public string? Image;
        public string? Tint;
        public string? Type;
        public string? Color;
        public bool Hero;
        // Takes no row or tile; only a sidebar draws a divider.
        public bool Divider;
        public bool Inline;
        public string? Kind;
        public bool On;
        public int Percent;
        public string? ValueText;
        public string? Track;
        public bool HasBar;
        public int Bar;
        public bool Chosen;
        // Page directives (FrameComposer.Page.cs); a row layout ignores them all.
        public bool Chip;
        public bool Section;
        public bool Head;
        public bool Avatar;
        public bool Primary;
        public bool Lead;
        // A text label that names a map (MenuPageProfile.ImageFromLabel / MarkFromLabel).
        public bool Map;
        public string? At;
        public string? View;
        public string? Group;
        public string? Sub;
        public string? Unit;
        public string? Badge;
        public string? Tag;
        public string? Tag2;
        public string? Note;
        public string? Mark;
        public string? Tone;
        public string? SubTone;
        public string? BadgeTone;
        public string? TagTone;
        public string? Tag2Tone;
        public string? NoteTone;
        public string? MeterTone;
        public string? Glyph;
        // `vglyph=`: the value's glyph, where it differs from the entry's own.
        public string? ValueGlyph;
        public string? TagGlyph;
        public string? ValueUnit;
        public string? Swatch;
        public int? Rating;
        public int PipsLit;
        public int PipsOf;
        public int PipsNay;
        public int? Bar2;
        // The legend's word for this row (vote, pick, apply ...), else one from its kind.
        public string? Verb;
        // From PanoramaComponentRenderer: the row opens a page.
        public bool Submenu;
        // A fact, not an action (`info`): drawn as text with no key and nothing to click. Core may
        // still focus it -- TextComponent is focusable so a long page can scroll past it.
        public bool Info;
        // Drawn by another row (`fold`): a mouse page gives it no slot; keyboard pages draw it.
        public bool Fold;
        // A header node holding the page's own footer line for a mouse player (`hint`).
        public bool Hint;
        // A header node's `live=<entry id>`: that sidebar entry is happening now.
        public string? Live;
    }
}
