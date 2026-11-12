using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using CustomHUD.Config;
using CustomHUD.Navigation;
using SwiftlyS2.Shared.Menu;
using SwiftlyS2.Shared.Menu.Components;
using SwiftlyS2.Shared.Translation;

namespace CustomHUD.Rendering;

// The page path (MenuLayoutProfile.Page, menu_locker.vxml). Each entry fills one
// slot -- of the main list, a header chip, or a named region such as a detail
// pane -- and every slot takes the same directives, so a design is where entries go plus
// how the layout styles them.
//
// Physical rows are body indices here: Focusable covers every entry in order and
// Targets names the Button each is clicked through, since a slot index no longer
// says which entry it holds once entries live outside the list.
internal sealed partial class FrameComposer
{
    private const int MaxPips = 20;
    private const int MaxTabSlots = 16;

    private FrameSnapshot ComposePage(
        IMenuRenderContext context,
        MenuLayoutProfile profile,
        MenuPageProfile page,
        string layoutPath,
        NavMode mode,
        int scalePercent,
        ulong steamId,
        Action<MenuNode> reportUnsupported)
    {
        var vars = ImmutableArray.CreateBuilder<VarWrite>();
        var classes = ImmutableArray.CreateBuilder<ClassWrite>();
        var targets = ImmutableDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
        var frame = context.Frame;
        var cls = profile.Classes;
        var pc = page.Classes;
        var shell = profile.Panels.Shell;

        // A plugin can put chips (and a view) in the header, which every chunk of a long list keeps.
        var chips = new List<RowContent>();
        var titleParts = new List<string>();
        var live = new List<string>();
        var variants = new SortedSet<string>(StringComparer.Ordinal);
        string? view = null;
        string? hint = null;
        foreach (var node in frame.Header)
        {
            var piece = Flatten(node, reportUnsupported);
            view ??= piece.View;
            if (piece.Variant is { } headerVariant) variants.Add(headerVariant);
            if (piece.Live is { } entry) live.Add(entry);
            // A spotlight belongs to row layouts (MenuLayoutProfile.Spot); a page has no place for it.
            if (piece.Spot) continue;
            if (piece.Chip) chips.Add(piece);
            else if (piece.Hint) hint ??= piece.Text.Length > 0 ? piece.Text : null;
            else if (piece.Text.Length > 0) titleParts.Add(piece.Text);
        }
        vars.Add(new VarWrite(profile.Variables.TitlePanel, profile.Variables.TitleVar,
            WithoutCrumb(CleanTitle(string.Join(' ', titleParts)), CrumbOf(context.Menu))));

        // A page's own hint says what clicking does there; keys still need the mode's line.
        var entries = frame.Body.Select(node => Flatten(node, reportUnsupported)).Where(e => !e.Divider).ToArray();
        vars.Add(new VarWrite(profile.Variables.FooterPanel, profile.Variables.FooterVar,
            mode == NavMode.Mouse && hint is not null
                ? hint
                : ComposeHint(context, profile, mode, viewOnly: !entries.Any(e => e.Selectable && !e.Info))));
        foreach (var slot in profile.BlankOnOpen)
            vars.Add(new VarWrite(slot, slot, string.Empty));

        var paging = (context as FrameContext)?.Page;
        var keys = RowKeys(entries, mode, paging?.KeyedBefore ?? 0);
        var controls = ImmutableDictionary.CreateBuilder<string, RowControl>(StringComparer.Ordinal);

        // Route every entry. An `inline` one joins the slot straight before it as that slot's own
        // Button, where that slot has one; a row without (the list's) keeps it as a row of its own.
        var regions = page.Regions;
        var lists = new List<int>[regions.Count];
        for (var r = 0; r < lists.Length; r++) lists[r] = [];
        var main = new List<int>();
        var inlineOf = Enumerable.Repeat(-1, entries.Length).ToArray();
        var head = -1;
        List<int>? previous = null;
        var previousHasInline = false;
        for (var k = 0; k < entries.Length; k++)
        {
            var e = entries[k];
            view ??= e.View;
            if (e.Variant is { } entryVariant) variants.Add(entryVariant);
            // Drawn by another row of a mouse page; still counted, so focus and clicks keep their places.
            if (e.Fold && mode == NavMode.Mouse) continue;
            if (e.Inline && previousHasInline && previous is [.., var owner] && owner == k - 1 && inlineOf[owner] < 0)
            {
                inlineOf[owner] = k;
                continue;
            }
            previous = null;
            if (e.Chip && page.ChipSlots > 0)
            {
                chips.Add(e);
                continue;
            }
            if (e.Head && head < 0)
            {
                head = k;
                continue;
            }
            var r = e.At is { } at ? regions.FindIndex(x => string.Equals(x.Name, at, StringComparison.OrdinalIgnoreCase)) : -1;
            var inRegion = r >= 0 && lists[r].Count < Math.Clamp(regions[r].Slots, 0, MaxRowSlots);
            var list = inRegion ? lists[r] : main;
            list.Add(k);
            previous = list;
            previousHasInline = (inRegion ? regions[r].Row : profile.Row).InlineSuffix is not null;
        }

        // The main list, with PageTiles cards closing a chunk of a longer one.
        var row = profile.Row;
        var tiles = mode == NavMode.Mouse && profile.PageTiles && paging is { Total: > 1 } ? paging : null;
        var lead = tiles is { Index: > 0 } ? 1 : 0;
        var trail = tiles is not null && tiles.Index < tiles.Total - 1 ? 1 : 0;
        var count = Math.Min(main.Count, profile.RowSlots - lead - trail);
        var used = lead + count + trail;
        var leader = Leader(entries, main, page.AutoLead);
        var localizer = strings?.For(context.Player.PlayerID);
        var leadTag = page.LeadTag.Length == 0 ? string.Empty
            : (localizer is null ? null : HudTranslations.Find(localizer, "customhud.lead")) ?? page.LeadTag;
        string? group = null;
        for (var i = 0; i < profile.RowSlots; i++)
        {
            if (i >= used)
            {
                classes.Add(new ClassWrite(row.Panel(i), cls.Hidden, true));
                continue;
            }
            if ((lead == 1 && i == 0) || (trail == 1 && i == used - 1))
            {
                var back = lead == 1 && i == 0;
                var tile = new RowContent
                {
                    Text = (back ? "«" : "»") + SegmentSeparator
                        + FormatPage(profile.Variables.PageFormat, tiles!.Index + (back ? 0 : 2), tiles.Total),
                };
                ComposeSlot(profile, page, row, i, tile, -1, default, -1, null, null, 0, mode, vars, classes, targets, controls);
                if (cls.PageTile.Length > 0) classes.Add(new ClassWrite(row.Panel(i), cls.PageTile, true));
                targets[row.Hit(i)] = back ? RowMap.PreviousPageTarget : RowMap.NextPageTarget;
                continue;
            }
            var k = main[i - lead];
            group = NextGroup(entries[k], group, out var applied);
            ComposeSlot(profile, page, row, i, entries[k], k,
                inlineOf[k] >= 0 ? entries[inlineOf[k]] : default, inlineOf[k], applied, k == leader ? leadTag : null, keys[k],
                mode, vars, classes, targets, controls);
        }
        if (count > 0) classes.Add(new ClassWrite(shell, Format(pc.Region, "main"), true));
        classes.Add(new ClassWrite(profile.Panels.Body, Format(cls.RowCount, count), true));
        if (main.Take(count).Any(k => entries[k].Glyph is not null && (entries[k].Avatar || entries[k].Mark is not null)))
            classes.Add(new ClassWrite(shell, pc.GlyphColumn, true));

        for (var r = 0; r < regions.Count; r++)
        {
            var region = regions[r];
            var list = lists[r];
            var regionLeader = Leader(entries, list, page.AutoLead);
            string? regionGroup = null;
            for (var i = 0; i < Math.Clamp(region.Slots, 0, MaxRowSlots); i++)
            {
                if (i >= list.Count)
                {
                    classes.Add(new ClassWrite(region.Row.Panel(i), cls.Hidden, true));
                    continue;
                }
                var k = list[i];
                regionGroup = NextGroup(entries[k], regionGroup, out var applied);
                ComposeSlot(profile, page, region.Row, i, entries[k], k,
                    inlineOf[k] >= 0 ? entries[inlineOf[k]] : default, inlineOf[k], applied, k == regionLeader ? leadTag : null,
                    keys[k], mode, vars, classes, targets, controls);
            }
            if (list.Count > 0 && region.Name.Length > 0)
                classes.Add(new ClassWrite(shell, Format(pc.Region, region.Name.ToLowerInvariant()), true));
        }

        // Chips are read, never clicked.
        for (var i = 0; i < Math.Clamp(page.ChipSlots, 0, 8); i++)
        {
            if (i >= chips.Count)
            {
                classes.Add(new ClassWrite(page.ChipRow.Panel(i), cls.Hidden, true));
                continue;
            }
            ComposeSlot(profile, page, page.ChipRow, i, chips[i], -1, default, -1, null, null, 0, mode, vars, classes, targets, controls);
        }
        if (chips.Count > 0) classes.Add(new ClassWrite(shell, Format(pc.Region, "chips"), true));

        var columns = head >= 0 ? entries[head].Text.Split(SegmentSeparator) : [];
        for (var j = 0; j < Math.Clamp(page.HeadSlots, 0, 16); j++)
            vars.Add(new VarWrite(page.HeadPanel, page.HeadColumn(j), j < columns.Length ? columns[j] : string.Empty));
        if (head >= 0) classes.Add(new ClassWrite(shell, pc.HasHead, true));

        if (page.ResolveView(context.Menu.Id, view, config.Current.PageViews) is { } resolved && PresetToken().IsMatch(resolved))
            classes.Add(new ClassWrite(shell, Format(pc.View, resolved), true));
        foreach (var variant in variants)
            if (PresetToken().IsMatch(variant)) classes.Add(new ClassWrite(shell, Format(pc.Variant, variant), true));

        var rows = new RowMap(
            [.. entries.Select(e => e.Selectable)],
            Array.FindIndex(entries, e => e.Focused),
            context.Session.FocusedIndex,
            string.Empty)
        {
            FirstRow = frame.PageOffset,
            Session = context.Session,
            BackButton = profile.Panels.Back,
            CloseButton = profile.Panels.Close,
            PreviousButton = profile.Panels.Previous,
            NextButton = profile.Panels.Next,
            PreviousPageFocus = paging?.PreviousFocus ?? -1,
            NextPageFocus = paging?.NextFocus ?? -1,
            Keyed = [.. keys.Select(key => key > 0)],
        };

        if (mode == NavMode.Mouse && profile.Sidebar is { } sidebar)
            rows = ComposeSidebar(context, profile, sidebar, rows, vars, classes, live);
        if (rows.SidebarItems.IsEmpty) classes.Add(new ClassWrite(shell, pc.NoSidebar, true));
        rows = ComposeTabs(context, profile, TabStrip.Of(page), rows, vars, classes);
        ComposeTitleLine(context, profile, page, vars, classes);
        classes.Add(new ClassWrite(shell, Format(pc.Mode, NavModeResolver.Name(mode)), true));

        // A menu whose plugin unbound Close (MixScrims' picks) has no way out, by design.
        var closable = Closable(context.Menu);
        if (rows.BackButton.Length > 0)
            classes.Add(new ClassWrite(rows.BackButton, cls.Hidden,
                mode != NavMode.Mouse || !closable || (rows.CloseButton.Length > 0 && !HasBackTarget(context.Menu, rows))));
        if (rows.CloseButton.Length > 0)
            classes.Add(new ClassWrite(rows.CloseButton, cls.Hidden, mode != NavMode.Mouse || !closable));
        var paged = paging is not null
            ? paging.Total > 1
            : frame.PageOffset > 0 || rows.Focusable.Count(f => f) < frame.TotalItems;
        foreach (var panel in new[] { rows.PreviousButton, rows.NextButton })
            if (panel.Length > 0)
                classes.Add(new ClassWrite(panel, cls.Hidden, mode != NavMode.Mouse || !paged));
        ComposePageCounter(profile, paging, vars, classes);
        if (page.Legend is { } legend)
        {
            ComposePageDots(profile, legend, paging, classes);
            ComposeLegend(context, legend, cls.Hidden, mode, entries, keys, paged, closable && HasBackTarget(context.Menu, rows),
                closable, vars, classes);
        }

        if (cls.ScaleSteps.Contains(scalePercent))
            classes.Add(new ClassWrite(shell, Format(cls.Scale, scalePercent), true));
        classes.Add(new ClassWrite(shell, cls.Hidden, false));

        return new FrameSnapshot(
            vars.ToImmutable(), classes.ToImmutable(), layoutPath,
            Fingerprint(profile), shell, cls.Hidden,
            steamId, rows with { Targets = targets.ToImmutable(), Controls = controls.ToImmutable() }, CaptureInput: mode == NavMode.Mouse);
    }

    // The key each entry shows and answers to: Numbers mode counts the page from 1,
    // the others an item's place across pages; 0 for none. Core focuses a disabled
    // entry but cannot act on it, so it gets no key, nor does an `info` fact; a Chat
    // menu keeps core's !N.
    private static int[] RowKeys(RowContent[] entries, NavMode mode, int keyedBefore)
    {
        var keys = new int[entries.Length];
        if (mode == NavMode.Mouse) return keys;
        for (int k = 0, ordinal = 0; k < entries.Length; k++)
        {
            var entry = entries[k];
            if (!entry.Selectable) continue;
            if (entry.Number > 0)
            {
                keys[k] = entry.Number;
                continue;
            }
            if (entry.Disabled || entry.Info) continue;
            ordinal++;
            keys[k] = mode == NavMode.Numbers ? ordinal : keyedBefore + ordinal;
        }
        return keys;
    }

    internal static bool Closable(IMenu menu) => menu.Keymap.GetKey(MenuActions.Close) != MenuKey.None;

    // A section opens a group its followers carry until the next section, without
    // joining it itself: a view styles the group's members, never their heading.
    // An entry's own `group=` applies to it alone.
    private static string? NextGroup(in RowContent entry, string? running, out string? applied)
    {
        if (entry.Section)
        {
            applied = null;
            return entry.Group;
        }
        applied = entry.Group ?? running;
        return running;
    }

    // An explicit `lead`, else (when the profile counts bars as shares) the entry
    // with the highest `bar=`; a tie leads nobody, and neither does a lone bar (a
    // timer, a progress meter), which has nothing to lead.
    private static int Leader(RowContent[] entries, List<int> list, bool byBar)
    {
        int best = -1, top = 0, ties = 0, bars = 0;
        foreach (var k in list)
        {
            if (entries[k].Lead) return k;
            if (!byBar || !entries[k].HasBar) continue;
            bars++;
            if (entries[k].Bar <= 0) continue;
            if (entries[k].Bar > top)
            {
                (top, best, ties) = (entries[k].Bar, k, 1);
            }
            else if (entries[k].Bar == top)
            {
                ties++;
            }
        }
        return ties == 1 && bars > 1 ? best : -1;
    }

    // One slot of any region. Writes every variable the spec declares, so text a
    // previous occupant left is never shown, and only the classes that are on.
    private void ComposeSlot(
        MenuLayoutProfile profile,
        MenuPageProfile page,
        LayoutRowSpec spec,
        int i,
        RowContent entry,
        int target,
        RowContent inline,
        int inlineTarget,
        string? group,
        string? leadTag,
        int key,
        NavMode mode,
        ImmutableArray<VarWrite>.Builder vars,
        ImmutableArray<ClassWrite>.Builder classes,
        ImmutableDictionary<string, int>.Builder targets,
        ImmutableDictionary<string, RowControl>.Builder controls)
    {
        var cls = profile.Classes;
        var pc = page.Classes;
        var panel = spec.Panel(i);

        var parts = entry.Text.Split(SegmentSeparator);
        var label = parts[0];
        var value = entry.Kind == "slider" && entry.ValueText is { } readout ? readout
            : parts.Length > 1 ? parts[1] : string.Empty;
        // A layout with nowhere else for the unit keeps it on the value ("45%").
        if (value.Length > 0 && spec.ValueUnit(i) is null) value += entry.ValueUnit;
        var iconToken = entry.Icon ?? (parts.Length > 2 ? parts[2] : string.Empty);
        if (iconToken.Length == 0 && entry.Color is { } color && profile.ColorIcons.TryGetValue(color, out var colorIcon))
            iconToken = colorIcon;
        // A label is read as a map only where MapChooser puts maps: an entry to click,
        // a tallied share (bar=), or one marked `map`. Free text ("kaunas_awp" as a
        // voter's name) is left alone.
        var byLabel = entry.Selectable || entry.HasBar || entry.Map;
        var image = !string.IsNullOrWhiteSpace(entry.Image) ? entry.Image : byLabel ? ImageFromLabel(page, label) : null;
        // Only a map-shaped label: "awp_lego_2" gets "AWP", "Extend current map" nothing.
        var mapLike = byLabel && page.MarkFromLabel && image is null && MapName().IsMatch(label.Trim());
        var tone = entry.Tone
            ?? (entry.Color is { } toneColor && page.ColorTones.TryGetValue(toneColor, out var colorTone) ? colorTone : null)
            ?? (mapLike && PresetToken().IsMatch(Prefix(label).ToLowerInvariant()) ? Prefix(label).ToLowerInvariant() : null);
        var mark = entry.Mark ?? (entry.Avatar ? Initial(label) : mapLike ? Prefix(label) : null);
        var (tag, tagTone, tagGlyph) = (entry.Tag, entry.TagTone, entry.TagGlyph);
        if (leadTag is { Length: > 0 } && string.IsNullOrEmpty(tag))
            (tag, tagTone, tagGlyph) = (leadTag, tagTone ?? "gold", tagGlyph ?? (page.LeadGlyph.Length > 0 ? page.LeadGlyph : null));

        if (spec.Value(i) is { } valueVar)
        {
            vars.Add(new VarWrite(panel, spec.Label(i), label));
            vars.Add(new VarWrite(panel, valueVar, value));
        }
        else
        {
            vars.Add(new VarWrite(panel, spec.Label(i), value.Length == 0 ? label : label + ' ' + value));
        }
        WriteText(vars, classes, panel, spec.Type(i), entry.Type, cls.HasType);
        WriteText(vars, classes, panel, spec.Sub(i), entry.Sub, pc.HasSub);
        WriteText(vars, classes, panel, spec.Unit(i), entry.Unit, pc.HasUnit);
        WriteText(vars, classes, panel, spec.Badge(i), entry.Badge, pc.HasBadge);
        WriteText(vars, classes, panel, spec.Tag(i), tag, pc.HasTag);
        WriteText(vars, classes, panel, spec.Tag2(i), entry.Tag2, pc.HasTag2);
        WriteText(vars, classes, panel, spec.Note(i), entry.Note, pc.HasNote);
        WriteText(vars, classes, panel, spec.Mark(i), mark, pc.HasMark);
        WriteText(vars, classes, panel, spec.ValueUnit(i), value.Length > 0 ? entry.ValueUnit : null, pc.HasValueUnit);
        if (spec.TagGlyph(i) is { } tagGlyphPanel)
        {
            var shown = tagGlyph is not null && !string.IsNullOrEmpty(tag);
            if (shown)
            {
                classes.Add(new ClassWrite(panel, pc.HasTagGlyph, true));
                classes.Add(new ClassWrite(tagGlyphPanel, Format(pc.Glyph, tagGlyph!), true));
            }
            classes.Add(new ClassWrite(tagGlyphPanel, cls.Hidden, !shown));
        }
        if (spec.Key(i) is { } keyVar)
        {
            vars.Add(new VarWrite(panel, keyVar, key > 0 ? key.ToString(CultureInfo.InvariantCulture) : string.Empty));
            if (key > 0) classes.Add(new ClassWrite(panel, pc.HasKey, true));
        }

        classes.Add(new ClassWrite(panel, cls.Hidden, false));
        if (value.Length > 0) classes.Add(new ClassWrite(panel, pc.HasValue, true));
        if (entry.Focused && mode is NavMode.Wasd or NavMode.Buttons) classes.Add(new ClassWrite(panel, cls.Selected, true));
        if (entry.Disabled) classes.Add(new ClassWrite(panel, cls.Disabled, true));
        if (entry.Danger) classes.Add(new ClassWrite(panel, cls.Danger, true));
        if (entry.Marquee) classes.Add(new ClassWrite(panel, cls.Marquee, true));
        if (entry.Chosen && cls.Chosen.Length > 0) classes.Add(new ClassWrite(panel, cls.Chosen, true));
        if (entry.HasBar)
        {
            if (cls.HasBar.Length > 0) classes.Add(new ClassWrite(panel, cls.HasBar, true));
            if (cls.Bar.Length > 0) classes.Add(new ClassWrite(panel, Format(cls.Bar, entry.Bar), true));
        }
        else if (entry.Kind == "slider" && cls.Bar.Length > 0)
        {
            // Without HasBar no meter shows, so the ladder is free to size the slider's track.
            classes.Add(new ClassWrite(panel, Format(cls.Bar, Math.Clamp(entry.Percent, 0, 100)), true));
        }
        if (entry.Track is { } preset) Tone(classes, panel, pc.Track, preset);
        if (leadTag is not null) classes.Add(new ClassWrite(panel, pc.Lead, true));
        if (entry.Primary) classes.Add(new ClassWrite(panel, pc.Primary, true));
        if (entry.Section) classes.Add(new ClassWrite(panel, pc.Section, true));
        if (group is not null) classes.Add(new ClassWrite(panel, Format(pc.Group, group), true));
        classes.Add(new ClassWrite(panel, Format(pc.Kind, entry.Submenu && entry.Kind is null ? "submenu" : KindOf(entry)), true));
        if (entry.Kind == "toggle" && entry.On) classes.Add(new ClassWrite(panel, pc.On, true));
        if (entry.Bar2 is { } bar2)
        {
            classes.Add(new ClassWrite(panel, pc.HasBar2, true));
            classes.Add(new ClassWrite(panel, Format(pc.Bar2, bar2), true));
        }
        Tone(classes, panel, pc.Tone, tone);
        Tone(classes, panel, pc.SubTone, entry.SubTone);
        Tone(classes, panel, pc.BadgeTone, entry.BadgeTone);
        Tone(classes, panel, pc.TagTone, tagTone);
        Tone(classes, panel, pc.Tag2Tone, entry.Tag2Tone);
        Tone(classes, panel, pc.NoteTone, entry.NoteTone);
        Tone(classes, panel, pc.MeterTone, entry.MeterTone);
        Tone(classes, panel, pc.Swatch, entry.Swatch);
        if (entry.Avatar && pc.AvatarColors > 0)
            classes.Add(new ClassWrite(panel, Format(pc.Avatar, Fnv(label) % (uint)pc.AvatarColors), true));

        if (entry.Rating is { } rating)
        {
            classes.Add(new ClassWrite(panel, pc.HasRating, true));
            classes.Add(new ClassWrite(panel, Format(pc.Rating, Math.Clamp(rating / 5000, 0, 6)), true));
        }
        // CS2 draws a rating under a thousand whole, at the large size.
        var (major, minor) = entry.Rating is { } points
            ? points >= 1000
                ? ((points / 1000).ToString(CultureInfo.InvariantCulture), "," + (points % 1000).ToString("D3", CultureInfo.InvariantCulture))
                : (points.ToString(CultureInfo.InvariantCulture), string.Empty)
            : (string.Empty, string.Empty);
        if (spec.Rating(i) is { } ratingVar) vars.Add(new VarWrite(panel, ratingVar, major));
        if (spec.RatingMinor(i) is { } minorVar) vars.Add(new VarWrite(panel, minorVar, minor));

        if (entry.PipsOf > 0)
        {
            var lit = Math.Min(entry.PipsOf, entry.PipsLit + entry.PipsNay);
            classes.Add(new ClassWrite(panel, pc.HasPips, true));
            classes.Add(new ClassWrite(panel, Format(pc.Pips, entry.PipsOf), true));
            classes.Add(new ClassWrite(panel, Format(pc.Lit, lit), true));
            if (lit > entry.PipsLit) classes.Add(new ClassWrite(panel, Format(pc.Yes, entry.PipsLit), true));
        }

        var known = iconToken.Length > 0 && cls.IconTokens.Contains(iconToken);
        if (known) classes.Add(new ClassWrite(panel, Format(pc.Rarity, iconToken), true));
        if (spec.Icon(i) is { } iconPanel)
        {
            if (known) classes.Add(new ClassWrite(iconPanel, Format(cls.Icon, iconToken), true));
            classes.Add(new ClassWrite(iconPanel, cls.Hidden, !known));
        }

        var token = image is null ? null : ThumbToken(image);
        if (token is not null) classes.Add(new ClassWrite(panel, cls.HasThumb, true));
        foreach (var picture in new[] { spec.Thumb(i), spec.Logo(i) })
        {
            if (picture is null) continue;
            if (token is not null) classes.Add(new ClassWrite(picture, Format(cls.Thumb, token), true));
            if (token is not null && entry.Tint is { } tint && cls.Tint.Length > 0)
                classes.Add(new ClassWrite(picture, Format(cls.Tint, tint), true));
            classes.Add(new ClassWrite(picture, cls.Hidden, token is null));
        }

        // Without `vglyph=` the value shows the entry's own glyph, as before it existed.
        var glyphPanel = spec.Glyph(i);
        var valueGlyphPanel = spec.ValueGlyph(i);
        var valueGlyph = entry.ValueGlyph ?? entry.Glyph;
        if (entry.Glyph is not null && (glyphPanel ?? valueGlyphPanel) is not null) classes.Add(new ClassWrite(panel, pc.HasGlyph, true));
        if (entry.ValueGlyph is not null && valueGlyphPanel is not null) classes.Add(new ClassWrite(panel, pc.HasValueGlyph, true));
        foreach (var (glyphTarget, glyph) in new[] { (glyphPanel, entry.Glyph), (valueGlyphPanel, valueGlyph) })
        {
            if (glyphTarget is null) continue;
            if (glyph is not null) classes.Add(new ClassWrite(glyphTarget, Format(pc.Glyph, glyph), true));
            classes.Add(new ClassWrite(glyphTarget, cls.Hidden, glyph is null));
        }

        if (spec.Inline(i) is { } inlinePanel)
        {
            var inlineParts = inlineTarget >= 0 ? inline.Text.Split(SegmentSeparator) : [string.Empty];
            vars.Add(new VarWrite(panel, inlinePanel, inlineParts[0]));
            vars.Add(new VarWrite(panel, inlinePanel + "_value", inlineParts.Length > 1 ? inlineParts[1] : string.Empty));
            if (inlineTarget >= 0)
            {
                classes.Add(new ClassWrite(panel, pc.HasInline, true));
                if (inline.Disabled) classes.Add(new ClassWrite(panel, Format(pc.InlineState, "disabled"), true));
                if (inline.Danger) classes.Add(new ClassWrite(panel, Format(pc.InlineState, "danger"), true));
                if (inline.Primary) classes.Add(new ClassWrite(panel, Format(pc.InlineState, "primary"), true));
                if (inline.Kind == "toggle" && inline.On) classes.Add(new ClassWrite(panel, Format(pc.InlineState, "on"), true));
                targets[inlinePanel] = inlineTarget;
            }
        }

        // One button per `act=`, in order; an off one, or any on a row that can't be acted on, is drawn
        // but left out of the click map.
        if (spec.ActSuffix is not null)
        {
            var acts = entry.Acts;
            var actable = target >= 0 && !entry.Info && !entry.Disabled;
            for (var n = 0; n < spec.ActSlots; n++)
            {
                var button = spec.Act(i, n)!;
                var shown = acts is not null && n < acts.Count;
                classes.Add(new ClassWrite(button, cls.Hidden, !shown));
                if (!shown) continue;
                var act = acts![n];
                classes.Add(new ClassWrite(button + "_glyph", Format(pc.Glyph, act.Glyph), true));
                if (act.Off || !actable)
                {
                    classes.Add(new ClassWrite(button, Format(pc.ActState, "off"), true));
                    continue;
                }
                if (act.Danger) classes.Add(new ClassWrite(button, Format(pc.ActState, "danger"), true));
                controls[button] = new RowControl(target, 0, 0, 0, act.Action);
            }
            if (acts is { Count: > 0 }) classes.Add(new ClassWrite(panel, pc.HasActs, true));
        }

        if (target >= 0 && !entry.Info)
        {
            targets[spec.Hit(i)] = target;
            // Steppers and the track send what A, D and a click on the bar would.
            if (!entry.Disabled && entry.Kind is "slider" or "selector")
            {
                if (spec.Dec(i) is { } dec) controls[dec] = new RowControl(target, -1, 0, 0);
                if (spec.Inc(i) is { } inc) controls[inc] = new RowControl(target, 1, 0, 0);
                if (entry.Kind == "slider" && spec.Segments > 1)
                    for (var n = 0; n < spec.Segments; n++)
                        if (spec.Segment(i, n) is { } segment) controls[segment] = new RowControl(target, 0, n, spec.Segments);
            }
        }
    }

    private static void WriteText(
        ImmutableArray<VarWrite>.Builder vars, ImmutableArray<ClassWrite>.Builder classes,
        string panel, string? variable, string? text, string presentClass)
    {
        if (variable is null) return;
        vars.Add(new VarWrite(panel, variable, text ?? string.Empty));
        if (!string.IsNullOrEmpty(text) && presentClass.Length > 0)
            classes.Add(new ClassWrite(panel, presentClass, true));
    }

    private static void Tone(ImmutableArray<ClassWrite>.Builder classes, string panel, string template, string? token)
    {
        if (token is { Length: > 0 } && template.Length > 0)
            classes.Add(new ClassWrite(panel, Format(template, token), true));
    }

    private static string? ImageFromLabel(MenuPageProfile page, string label)
    {
        if (page.ImageFromLabel.Length == 0) return null;
        var key = label.Trim().ToLowerInvariant();
        return key.Length > 0 && page.ImageLabels.Contains(key, StringComparer.OrdinalIgnoreCase)
            ? string.Format(CultureInfo.InvariantCulture, page.ImageFromLabel, key)
            : null;
    }

    private static string Initial(string label)
    {
        foreach (var c in label)
            if (char.IsLetterOrDigit(c)) return char.ToUpperInvariant(c).ToString();
        return "?";
    }

    // "awp_lego_2" -> "AWP": what a map without a screenshot shows instead.
    private static string Prefix(string label)
    {
        var name = label.Trim();
        var cut = name.IndexOf('_');
        var prefix = cut > 0 ? name[..cut] : name;
        return (prefix.Length > 6 ? prefix[..6] : prefix).ToUpperInvariant();
    }

    // Stable across restarts, unlike string.GetHashCode, so a name keeps its colour.
    private static uint Fnv(string text)
    {
        var hash = 2166136261u;
        foreach (var c in text.ToLowerInvariant())
            hash = (hash ^ c) * 16777619u;
        return hash;
    }

    // ---- title line ----

    private void ComposeTitleLine(
        IMenuRenderContext context, MenuLayoutProfile profile, MenuPageProfile page,
        ImmutableArray<VarWrite>.Builder vars, ImmutableArray<ClassWrite>.Builder classes)
    {
        var root = SidebarRoot(context.Menu);
        var crumb = CrumbOf(context.Menu);
        var titlePanel = profile.Variables.TitlePanel;
        if (page.CrumbVar.Length > 0) vars.Add(new VarWrite(titlePanel, page.CrumbVar, crumb));
        if (crumb.Length > 0) classes.Add(new ClassWrite(titlePanel, page.Classes.HasCrumb, true));

        if (page.EyebrowVar.Length == 0) return;
        var eyebrow = ReferenceEquals(root, context.Menu) ? string.Empty : CleanTitle(MenuTitle(root));
        if (eyebrow.Length == 0)
            eyebrow = RootEyebrow(context.Menu.Id, page.Eyebrow, strings?.For(context.Player.PlayerID));
        vars.Add(new VarWrite(titlePanel, page.EyebrowVar, eyebrow));
        if (eyebrow.Length > 0) classes.Add(new ClassWrite(titlePanel, page.Classes.HasEyebrow, true));
    }

    // The parent's title, shown before the page's own, less what its own crumb already says; empty
    // under the root. The depth cap only guards a Parent cycle.
    private static string CrumbOf(IMenu menu, int depth = 0)
    {
        var parent = menu.Parent;
        if (parent is null || depth > 8 || ReferenceEquals(parent, SidebarRoot(menu))) return string.Empty;
        var crumb = WithoutCrumb(CleanTitle(MenuTitle(parent)), CrumbOf(parent, depth + 1));
        // A tab page usually repeats its host's title; "Bans › Bans" says nothing twice.
        return string.Equals(crumb, CleanTitle(MenuTitle(menu)), StringComparison.OrdinalIgnoreCase) ? string.Empty : crumb;
    }

    // A title that is a path from its parent ("Visuals > Player Models" under "Visuals") keeps only
    // what the crumb does not already say.
    private static string WithoutCrumb(string title, string crumb)
    {
        if (crumb.Length == 0 || !title.StartsWith(crumb, StringComparison.OrdinalIgnoreCase)) return title;
        var rest = title.AsSpan(crumb.Length).TrimStart();
        if (rest.Length < 2 || !PathSeparators.Contains(rest[0])) return title;
        var tail = rest[1..].Trim();
        return tail.Length > 0 ? tail.ToString() : title;
    }

    private const string PathSeparators = ">›»/|:";

    // A menu that is its own root has no root title to show: an operator's customhud.eyebrow.<plugin>
    // (the menu id's first part, "missions" for missions.main), then the profile's Eyebrow, then
    // customhud.eyebrow.default. An empty value draws no line. Only the built-in pages' keys ship, so
    // one an operator adds to en.jsonc reaches every language.
    private static string RootEyebrow(string menuId, string profileEyebrow, ILocalizer? localizer)
    {
        var dot = menuId.IndexOf('.');
        var plugin = (dot > 0 ? menuId[..dot] : menuId).Trim().ToLowerInvariant();
        return Eyebrow(localizer, plugin)
            ?? (profileEyebrow.Length > 0 ? Eyebrow(localizer, profileEyebrow) ?? profileEyebrow : null)
            ?? Eyebrow(localizer, "default")
            ?? string.Empty;
    }

    // SwiftlyS2 turns a translation's colour tags into chat control characters, which a label cannot draw.
    private static string? Eyebrow(ILocalizer? localizer, string name)
        => localizer is null || name.Length == 0 || HudTranslations.Find(localizer, "customhud.eyebrow." + name) is not { } text
            ? null
            : string.Concat(text.Where(c => !char.IsControl(c))).Trim();

    // ---- legend ----

    // The words `verb=` may name; each has a customhud.legend.<verb> translation.
    private static readonly HashSet<string> LegendVerbs = new(StringComparer.Ordinal)
        { "select", "pick", "open", "switch", "edit", "change", "vote", "apply" };

    // The footer as keys: a group for each thing the focused row answers to. Built from
    // the menu's own keymap and the focused component's kind, so it only offers keys
    // that work there; an operator's Hints entry, a Chat menu and Mouse mode keep the
    // plain hint line instead.
    private void ComposeLegend(
        IMenuRenderContext context, MenuLegendProfile legend, string hidden, NavMode mode, RowContent[] entries, int[] keys,
        bool paged, bool back, bool closable,
        ImmutableArray<VarWrite>.Builder vars, ImmutableArray<ClassWrite>.Builder classes)
    {
        var menu = context.Menu;
        var name = NavModeResolver.Name(mode);
        var shown = mode != NavMode.Mouse
            && !(mode == NavMode.Buttons && menu.InputMethod == MenuInputMethod.Chat)
            && Override(name) is null && Override(name + ".view") is null;

        var groups = new List<(string Key, string Key2, string Verb)>(4);
        if (shown)
        {
            var focused = Array.FindIndex(entries, e => e.Focused);
            var focus = focused >= 0 ? entries[focused] : default;
            var keyed = keys.Count(k => k > 0);
            // Facts take no key, yet the cursor still walks them to scroll a long page.
            var movable = keyed > 0 || entries.Any(e => e.Info && e.Selectable);
            var steps = focused >= 0 && keys[focused] > 0 && focus.Kind is "slider" or "selector";
            var verb = focused >= 0 && keys[focused] > 0 ? RowVerb(focus) : null;
            var leave = back ? "back" : "close";
            var keymap = menu.Keymap;
            string Bound(MenuActionId action) => MenuKeys.Describe(keymap.GetKey(action));
            switch (mode)
            {
                case NavMode.Wasd:
                    if (movable) groups.Add(("W", "S", "move"));
                    if (steps) groups.Add(("A", "D", "change"));
                    else if (verb is not null) groups.Add(("E", "", verb));
                    if (closable) groups.Add(("R", "", leave));
                    break;
                case NavMode.Buttons:
                    if (movable) groups.Add((Bound(MenuActions.NavigateUp), Bound(MenuActions.NavigateDown), "move"));
                    if (steps) groups.Add((Bound(MenuActions.NavigateLeft), Bound(MenuActions.NavigateRight), "change"));
                    else if (verb is not null) groups.Add((Bound(MenuActions.Select), "", verb));
                    if (closable) groups.Add((Bound(MenuActions.Close), "", leave));
                    break;
                case NavMode.Numbers:
                    // Numbers names one word for the whole page, so it is the rows' common one.
                    var common = CommonVerb(entries, keys);
                    if (keyed == 1) groups.Add(("1", "", common));
                    else if (keyed == 2) groups.Add(("1", "2", common));
                    else if (keyed > 2) groups.Add(("1-" + keyed.ToString(CultureInfo.InvariantCulture), "", common));
                    if (paged) groups.Add(("7", "8", "page"));
                    if (closable) groups.Add(("9", "", leave));
                    break;
            }
            groups.RemoveAll(g => g.Key.Length == 0);
        }

        var localizer = strings?.For(context.Player.PlayerID);
        for (var i = 0; i < Math.Clamp(legend.Slots, 0, 8); i++)
        {
            var panel = legend.Slot(i);
            var group = i < groups.Count ? groups[i] : (Key: string.Empty, Key2: string.Empty, Verb: string.Empty);
            vars.Add(new VarWrite(panel, legend.Key(i), group.Key));
            vars.Add(new VarWrite(panel, legend.Key2(i), group.Key2));
            vars.Add(new VarWrite(panel, legend.Verb(i), group.Verb.Length == 0 ? string.Empty : VerbText(group.Verb, localizer)));
            classes.Add(new ClassWrite(panel, hidden, i >= groups.Count));
            if (group.Key2.Length > 0) classes.Add(new ClassWrite(panel, legend.HasKey2Class, true));
        }
        if (groups.Count > 0) classes.Add(new ClassWrite(legend.Panel, legend.ShownClass, true));
    }

    private static string? RowVerb(in RowContent entry)
        => entry.Verb ?? (entry.Submenu && entry.Kind is null ? "open" : KindOf(entry) switch
        {
            "toggle" => "switch",
            "input" => "edit",
            "choice" => "change",
            "action" => "select",
            _ => null,
        });

    private static string CommonVerb(RowContent[] entries, int[] keys)
    {
        string? common = null;
        for (var k = 0; k < entries.Length; k++)
        {
            if (keys[k] <= 0) continue;
            var verb = RowVerb(entries[k]) ?? "select";
            if (common is null) common = verb;
            else if (common != verb) return "pick";
        }
        return common ?? "pick";
    }

    // Literal keys, so the translation check sees each one.
    private static string VerbText(string verb, ILocalizer? localizer) => (localizer is null ? null : verb switch
    {
        "move" => HudTranslations.Find(localizer, "customhud.legend.move"),
        "select" => HudTranslations.Find(localizer, "customhud.legend.select"),
        "pick" => HudTranslations.Find(localizer, "customhud.legend.pick"),
        "open" => HudTranslations.Find(localizer, "customhud.legend.open"),
        "switch" => HudTranslations.Find(localizer, "customhud.legend.switch"),
        "edit" => HudTranslations.Find(localizer, "customhud.legend.edit"),
        "change" => HudTranslations.Find(localizer, "customhud.legend.change"),
        "vote" => HudTranslations.Find(localizer, "customhud.legend.vote"),
        "apply" => HudTranslations.Find(localizer, "customhud.legend.apply"),
        "page" => HudTranslations.Find(localizer, "customhud.legend.page"),
        "back" => HudTranslations.Find(localizer, "customhud.legend.back"),
        "close" => HudTranslations.Find(localizer, "customhud.legend.close"),
        _ => null,
    }) ?? verb;

    // A dot per page, the current one lit, while there are few enough to count.
    private static void ComposePageDots(
        MenuLayoutProfile profile, MenuLegendProfile legend, MenuPage? page, ImmutableArray<ClassWrite>.Builder classes)
    {
        var panel = profile.Variables.PagePanel;
        if (panel.Length == 0 || page is not { Total: > 1 } || page.Total > legend.Dots) return;
        classes.Add(new ClassWrite(panel, Format(legend.PagesClass, page.Total), true));
        classes.Add(new ClassWrite(panel, Format(legend.PageClass, page.Index + 1), true));
    }

    internal static string MenuTitle(IMenu menu)
        => menu.GetComponents(MenuRegion.Header).OfType<TitleComponent>().FirstOrDefault()?.Text ?? string.Empty;

    // ---- tabs ----

    // A page layout's strip (MenuPageProfile) takes any sidebar entry's page that qualifies,
    // a row layout's (MenuTabsProfile) only the hosts it names.
    private readonly record struct TabStrip(
        int Slots, string Panel, string Prefix, string LabelVar, string CountVar, int AutoOpen,
        string HasTabsClass, string HasCountClass, IReadOnlyList<string>? Hosts)
    {
        public static TabStrip Of(MenuPageProfile page) => new(page.TabSlots, page.TabsPanel, page.TabPrefix,
            page.TabLabelVar, page.TabCountVar, page.TabAutoOpen, page.Classes.HasTabs, page.Classes.HasCount, null);

        public static TabStrip Of(MenuTabsProfile tabs) => new(tabs.Slots, tabs.Panel, tabs.Prefix,
            tabs.LabelVar, tabs.CountVar, tabs.AutoOpen, tabs.HasTabsClass, tabs.HasCountClass, tabs.Hosts);

        public bool IsHost(IMenu menu) => Hosts is null || Hosts.Any(pattern => pattern.EndsWith('*')
            ? menu.Id.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase)
            : string.Equals(pattern, menu.Id, StringComparison.OrdinalIgnoreCase));
    }

    private static RowMap ComposeTabs(
        IMenuRenderContext context, MenuLayoutProfile profile, TabStrip strip,
        RowMap rows, ImmutableArray<VarWrite>.Builder vars, ImmutableArray<ClassWrite>.Builder classes)
    {
        var cls = profile.Classes;
        var slots = Math.Clamp(strip.Slots, 0, MaxTabSlots);
        var (host, items) = TabsOf(context.Menu, strip);
        var onHost = items.Length > 0 && ReferenceEquals(host, context.Menu);
        // The host's own frame is about to open a tab in its place; drawing its tabs would only flash.
        // A page below a tab (a product, a player) is a detail of that tab, and drops the strip.
        var shown = items.Length > 0 && !onHost && ReferenceEquals(context.Menu.Parent, host);
        var active = shown ? ActiveTab(context, host!, items) : -1;

        for (var j = 0; j < slots; j++)
        {
            var panel = strip.Prefix + j;
            var item = shown && j < items.Length ? items[j] : null;
            var (label, count) = SplitCount(item?.Text ?? string.Empty);
            vars.Add(new VarWrite(panel, strip.LabelVar, label));
            vars.Add(new VarWrite(panel, strip.CountVar, count));
            classes.Add(new ClassWrite(panel, cls.Hidden, item is null));
            if (item is null) continue;
            if (count.Length > 0) classes.Add(new ClassWrite(panel, strip.HasCountClass, true));
            if (!item.Enabled) classes.Add(new ClassWrite(panel, cls.Disabled, true));
            if (j == active) classes.Add(new ClassWrite(panel, cls.Selected, true));
        }
        if (strip.Panel.Length > 0) classes.Add(new ClassWrite(strip.Panel, cls.Hidden, !shown));
        if (shown) classes.Add(new ClassWrite(profile.Panels.Shell, strip.HasTabsClass, true));

        var (open, index) = TabAutoOpen(context.Menu, host, items, strip.AutoOpen);
        return rows with
        {
            TabHost = items.Length > 0 ? host : null,
            TabPrefix = strip.Prefix,
            TabItems = shown ? [.. items] : [],
            SkipsTabHost = items.Length > 0 && strip.AutoOpen >= 0,
            TabAutoOpenItem = open,
            TabAutoOpenIndex = index,
        };
    }

    // The tab host above a menu and the tabs its strip shows; no tabs when the menu has no host.
    private static (IMenu? Host, SubmenuComponent[] Items) TabsOf(IMenu menu, TabStrip strip)
    {
        var slots = Math.Clamp(strip.Slots, 0, MaxTabSlots);
        var host = slots == 0 ? null : TabHostOf(menu, SidebarRoot(menu));
        return (host, host is null || !strip.IsHost(host) ? [] : TabEntries(host, slots));
    }

    // On the host's own frame, the tab it opens in its place (TabAutoOpen) and that tab's index.
    private static (SubmenuComponent? Item, int Index) TabAutoOpen(IMenu menu, IMenu? host, SubmenuComponent[] items, int auto)
        => items.Length > 0 && ReferenceEquals(host, menu) && auto >= 0 && auto < items.Length ? (items[auto], auto) : (null, -1);

    // The sidebar entry's page: the menu directly under the root that the player is somewhere beneath.
    internal static IMenu? TabHostOf(IMenu menu, IMenu root)
    {
        if (ReferenceEquals(menu, root)) return null;
        var top = menu;
        var visited = new HashSet<IMenu> { top };
        while (top.Parent is { } parent && !ReferenceEquals(parent, root) && visited.Add(parent)) top = parent;
        return ReferenceEquals(top.Parent, root) ? top : null;
    }

    // A page made only of plain submenus (and dividers), two or more: a choice
    // of views onto one thing, which is what a tab strip shows.
    internal static SubmenuComponent[] TabEntries(IMenu host, int slots)
    {
        var tabs = new List<SubmenuComponent>();
        foreach (var component in host.GetComponents(MenuRegion.Body))
        {
            switch (component)
            {
                case DividerComponent:
                    continue;
                case SubmenuComponent { Visible: true, VisibleWhen: null, EnabledWhen: null, TextProvider: null } tab:
                    tabs.Add(tab);
                    continue;
                default:
                    return [];
            }
        }
        return tabs.Count >= 2 ? [.. tabs.Take(slots)] : [];
    }

    private static int ActiveTab(IMenuRenderContext context, IMenu host, SubmenuComponent[] items)
    {
        var top = context.Menu;
        var visited = new HashSet<IMenu> { top };
        while (top.Parent is { } parent && !ReferenceEquals(parent, host) && visited.Add(parent)) top = parent;
        if (!ReferenceEquals(top.Parent, host)) return -1;

        var byId = UniqueIndex(items, c => c.Id is { Length: > 0 } id && string.Equals(id, top.Id, StringComparison.OrdinalIgnoreCase));
        if (byId >= 0) return byId;
        var byTitle = TitleMatch(MenuTitle(top), items.Select(c => c.Text).ToArray());
        if (byTitle >= 0) return byTitle;
        return (context as FrameContext)?.RememberedTab is { } memory
               && ReferenceEquals(memory.Host, host) && memory.Index < items.Length
            ? memory.Index
            : -1;
    }

    // A page titled "Visuals › Player models" belongs to the "Player models (8)" entry.
    internal static int TitleMatch(string title, string?[] labels)
    {
        title = CleanTitle(title);
        if (title.Length == 0) return -1;
        var exact = Array.FindIndex(labels, l => l is not null && string.Equals(SplitCount(l).Label, title, StringComparison.OrdinalIgnoreCase));
        if (exact >= 0) return exact;
        return Array.FindIndex(labels, l =>
        {
            var label = l is null ? string.Empty : SplitCount(l).Label;
            return label.Length > 0 && title.Length > label.Length
                && title.EndsWith(label, StringComparison.OrdinalIgnoreCase)
                && !char.IsLetterOrDigit(title[^(label.Length + 1)]);
        });
    }

    // "Visuals (23)" -> ("Visuals", "23").
    internal static (string Label, string Count) SplitCount(string text)
    {
        var match = CountSuffix().Match(text);
        return match.Success ? (match.Groups[1].Value, match.Groups[2].Value) : (text, string.Empty);
    }

    // The one index matching, or -1 for none or several.
    internal static int UniqueIndex<T>(T[] items, Func<T, bool> match)
    {
        var found = -1;
        for (var i = 0; i < items.Length; i++)
        {
            if (!match(items[i])) continue;
            if (found >= 0) return -1;
            found = i;
        }
        return found;
    }

    [GeneratedRegex(@"^(.*\S)\s*\((\d{1,6})\)$")]
    private static partial Regex CountSuffix();

    [GeneratedRegex(@"^[a-z0-9]+_[a-z0-9_]+$", RegexOptions.IgnoreCase)]
    private static partial Regex MapName();

    // ---- directives ----

    // Everything a page understands beyond the row directives. Text values run to
    // the next `;`; a value that becomes part of a class name must be a plain token.
    private static void ApplyPageDirective(string token, ref RowContent content)
    {
        var eq = token.IndexOf('=');
        var key = (eq < 0 ? token : token[..eq]).ToLowerInvariant();
        var value = eq < 0 ? null : token[(eq + 1)..];
        switch (key)
        {
            case "chip":
                content.Chip = true;
                if (Plain(value)) content.Tone ??= value;
                break;
            case "section": content.Section = true; break;
            case "head": content.Head = true; break;
            case "info": content.Info = true; break;
            case "fold": content.Fold = true; break;
            case "hint": content.Hint = true; break;
            case "primary": content.Primary = true; break;
            case "lead": content.Lead = true; break;
            case "map": content.Map = true; break;
            case "avatar":
                content.Avatar = true;
                if (value is { Length: > 0 }) content.Mark = value;
                break;
            case "at": if (Plain(value)) content.At = value; break;
            case "view": if (Plain(value)) content.View = value; break;
            case "variant": if (Plain(value)) content.Variant = value; break;
            case "group": if (Plain(value)) content.Group = value; break;
            case "sub": content.Sub = value; break;
            case "unit": content.Unit = value; break;
            case "badge": content.Badge = value; break;
            case "tag": content.Tag = value; break;
            case "tag2": content.Tag2 = value; break;
            case "note": content.Note = value; break;
            case "mark": content.Mark = value; break;
            case "tone": if (Plain(value)) content.Tone = value; break;
            case "subtone": if (Plain(value)) content.SubTone = value; break;
            case "badgetone": if (Plain(value)) content.BadgeTone = value; break;
            case "tagtone": if (Plain(value)) content.TagTone = value; break;
            case "tag2tone": if (Plain(value)) content.Tag2Tone = value; break;
            case "notetone": if (Plain(value)) content.NoteTone = value; break;
            case "metertone": if (Plain(value)) content.MeterTone = value; break;
            case "glyph": if (Plain(value)) content.Glyph = value; break;
            case "vglyph": if (Plain(value)) content.ValueGlyph = value; break;
            case "tagglyph": if (Plain(value)) content.TagGlyph = value; break;
            case "vunit": content.ValueUnit = value; break;
            case "live": if (value is { Length: > 0 }) content.Live = value; break;
            case "act": if (ParseAct(value) is { } act) (content.Acts ??= []).Add(act); break;
            case "swatch": if (Plain(value)) content.Swatch = value; break;
            case "rating":
                if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var rating))
                    content.Rating = rating;
                break;
            case "bar2":
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bar2))
                    content.Bar2 = Math.Clamp(bar2, 0, 100);
                break;
            case "submenu": content.Submenu = true; break;
            case "verb": if (value is { } verb && LegendVerbs.Contains(verb)) content.Verb = verb; break;
            case "nays":
                if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var nays))
                    content.PipsNay = Math.Clamp(nays, 0, MaxPips);
                break;
            case "pips":
                var slash = value?.IndexOf('/') ?? -1;
                if (slash > 0
                    && int.TryParse(value.AsSpan(0, slash), NumberStyles.None, CultureInfo.InvariantCulture, out var lit)
                    && int.TryParse(value.AsSpan(slash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var of)
                    && of is >= 1 and <= MaxPips)
                {
                    content.PipsOf = of;
                    content.PipsLit = Math.Clamp(lit, 0, of);
                }
                break;
        }
    }

    private static bool Plain(string? value) => value is not null && PresetToken().IsMatch(value);

    // "MoveUp|up|off": the action's name, its glyph token, then danger and off in any order.
    private static RowAct? ParseAct(string? value)
    {
        var parts = value?.Split('|') ?? [];
        if (parts.Length < 2 || !ActName().IsMatch(parts[0]) || !Plain(parts[1])) return null;
        var flags = parts.Skip(2).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new RowAct(parts[0], parts[1], flags.Contains("danger"), flags.Contains("off"));
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_.-]{0,47}$")]
    private static partial Regex ActName();
}
