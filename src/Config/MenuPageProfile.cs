namespace CustomHUD.Config;

// A locker page: everything a layout like menu_locker.vxml adds on top of rows.
// Header chips, a crumb and an eyebrow over the title, tabs under it, named
// regions beside and around the main list, and one view class on the shell.
// Null on every list profile, which keeps them on the row path untouched.
public sealed class MenuPageProfile
{
    // Both on Variables.TitlePanel. The eyebrow is the sidebar root's title, or, when the menu is
    // its own root, a customhud.eyebrow.<plugin> translation, then Eyebrow (its
    // customhud.eyebrow.<Eyebrow> translation winning over the literal text), then
    // customhud.eyebrow.default.
    public string EyebrowVar { get; set; } = "eyebrow";
    public string Eyebrow { get; set; } = "";
    // The parent page's title, shown before this one's.
    public string CrumbVar { get; set; } = "crumb";

    // Entries carrying `chip` (header or body). Their first text segment is the
    // value, the second its caption.
    public int ChipSlots { get; set; } = 3;
    public LayoutRowSpec ChipRow { get; set; } = new()
    {
        PanelId = "chip_{0}", LabelVar = "chip_{0}", ValueVar = "chip_{0}_label",
        KeyVar = null, IconPanel = null, GlyphPanel = "chip_{0}_glyph", ThumbPanel = "chip_{0}_thumb",
        RatingVar = "chip_{0}_rating", RatingMinorVar = "chip_{0}_rating_minor",
    };

    // The second level as tabs: the static submenus of the sidebar entry's page,
    // drawn while the player is anywhere beneath it. TabAutoOpen opens that entry
    // in place of the page itself, as Sidebar.AutoOpen does for the root; -1
    // shows the page. TabSlots 0 turns tabs off.
    public int TabSlots { get; set; } = 8;
    public string TabsPanel { get; set; } = "tabs";
    public string TabPrefix { get; set; } = "tab_";
    public string TabLabelVar { get; set; } = "tab";
    public string TabCountVar { get; set; } = "tab_count";
    public int TabAutoOpen { get; set; } = 0;

    // One view class on the shell: a `view=` directive, else the longest
    // matching Views pattern (exact id, then trailing *), else DefaultView.
    public string DefaultView { get; set; } = "list";
    public Dictionary<string, string> Views { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // A `head` entry: its text segments become the table's column titles.
    public string HeadPanel { get; set; } = "thead";
    public string HeadVar { get; set; } = "col_{0}";
    public int HeadSlots { get; set; } = 6;

    // Entries carrying `at=<Name>` fill these slots instead of the list. A name
    // the profile lacks, or a full region, keeps the entry in the list.
    public List<MenuRegionProfile> Regions { get; set; } = new();

    // A text colour that stands for a tone (MapChooser's Extend is #FFA500).
    public Dictionary<string, string> ColorTones { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // An entry without `image=` whose label is in ImageLabels gets the image key
    // ImageFromLabel formats from it: MapChooser sends bare map names, and only
    // stock maps have art. MarkFromLabel puts the label's prefix ("awp" of
    // awp_lego_2) on the mark slot of an entry left without an image, and as its
    // tone when it has none, so a workshop card can be coloured by its kind.
    public string ImageFromLabel { get; set; } = "";
    public List<string> ImageLabels { get; set; } = new();
    public bool MarkFromLabel { get; set; }

    // The tag a leading row (PageClasses.Lead) shows when it carries none of its
    // own; a customhud.lead translation wins. Empty leaves the tag alone.
    public string LeadTag { get; set; } = "Leading";

    // The icon in that tag's pill; empty for none.
    public string LeadGlyph { get; set; } = "";

    // Whether the unique highest `bar=` leads without saying so. Right for a vote,
    // where a bar is a share; wrong where it is time left or progress.
    public bool AutoLead { get; set; }

    // The list's footer as keycaps (menu_list). Null on layouts without a legend.
    public MenuLegendProfile? Legend { get; set; }

    public PageClasses Classes { get; set; } = new();

    public string HeadColumn(int i) => string.Format(HeadVar, i);

    // The operator's choice (CustomHudConfig.PageViews) first, then the page's own
    // `view=`, then this profile's map.
    internal string? ResolveView(string menuId, string? directive, IReadOnlyDictionary<string, string>? chosen = null)
        => (chosen is null ? null : MatchView(chosen, menuId))
            ?? (directive is { Length: > 0 } ? directive : null)
            ?? MatchView(Views, menuId)
            ?? (DefaultView.Length > 0 ? DefaultView : null);

    // Exact id first, then the longest trailing-* prefix.
    internal static string? MatchView(IReadOnlyDictionary<string, string> views, string menuId)
    {
        if (views.TryGetValue(menuId, out var exact)) return exact;
        string? match = null;
        var longest = -1;
        foreach (var (pattern, view) in views)
            if (pattern.EndsWith('*') && pattern.Length > longest
                && menuId.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase))
            {
                match = view;
                longest = pattern.Length;
            }
        return match;
    }
}

// A run of slots outside the main list. The shell carries PageClasses.Region of
// its name while it holds anything, so a layout can make room for it.
public sealed class MenuRegionProfile
{
    public string Name { get; set; } = "";
    public int Slots { get; set; }
    public LayoutRowSpec Row { get; set; } = new();
}

// The footer drawn as keys: a group per thing the focused row answers to, each one
// or two keycaps and a verb (customhud.legend.<verb>). Panel carries ShownClass while
// the legend is drawn; without it the layout shows the plain hint instead (Chat menus,
// Mouse mode, an operator's Hints entry).
public sealed class MenuLegendProfile
{
    public string Panel { get; set; } = "legend";
    public int Slots { get; set; } = 4;
    public string SlotPanel { get; set; } = "legend_{0}";
    public string KeyVar { get; set; } = "legend_{0}_key";
    public string Key2Var { get; set; } = "legend_{0}_key2";
    public string VerbVar { get; set; } = "legend_{0}_verb";
    public string ShownClass { get; set; } = "has-legend";
    public string HasKey2Class { get; set; } = "has-key2";
    // A dot per page beside the counter, up to Dots pages; past that the counter alone.
    // On LayoutVariables.PagePanel: PagesClass `{0}` = the page count, PageClass `{0}` = the page, from 1.
    public int Dots { get; set; } = 8;
    public string PagesClass { get; set; } = "pages-{0}";
    public string PageClass { get; set; } = "page-{0}";

    public string Slot(int i) => string.Format(SlotPanel, i);
    public string Key(int i) => string.Format(KeyVar, i);
    public string Key2(int i) => string.Format(Key2Var, i);
    public string Verb(int i) => string.Format(VerbVar, i);
}

// Class names a page writes. Composite entries take `{0}`.
public sealed class PageClasses
{
    // On the shell.
    public string View { get; set; } = "view-{0}";
    public string Region { get; set; } = "has-{0}";
    public string HasTabs { get; set; } = "has-tabs";
    public string HasHead { get; set; } = "has-head";
    public string NoSidebar { get; set; } = "no-sidebar";
    // `{0}` = wasd | buttons | numbers | mouse: the player's nav mode.
    public string Mode { get; set; } = "mode-{0}";
    // On the title panel.
    public string HasCrumb { get; set; } = "has-crumb";
    public string HasEyebrow { get; set; } = "has-eyebrow";
    // On a slot, from its directives.
    public string Tone { get; set; } = "tone-{0}";
    public string SubTone { get; set; } = "sub-{0}";
    public string BadgeTone { get; set; } = "badge-{0}";
    public string TagTone { get; set; } = "tag-{0}";
    public string Tag2Tone { get; set; } = "tag2-{0}";
    public string NoteTone { get; set; } = "note-{0}";
    public string MeterTone { get; set; } = "meter-{0}";
    public string Swatch { get; set; } = "sw-{0}";
    // `{0}` = 0 .. AvatarColors - 1, a stable hash of the label.
    public string Avatar { get; set; } = "av-{0}";
    public int AvatarColors { get; set; } = 8;
    // The rarity token of the row's badge, on the slot itself.
    public string Rarity { get; set; } = "rarity-{0}";
    // input | toggle | slider | selector | choice | progress | action | text | submenu, and the toggle's state.
    public string Kind { get; set; } = "kind-{0}";
    public string On { get; set; } = "is-on";
    // A slider's `track=` preset ("wear"); its position rides the Bar ladder.
    public string Track { get; set; } = "track-{0}";
    // `bar2=N`: a second meter, for a row with two shares (headshots and accuracy).
    public string HasBar2 { get; set; } = "has-bar2";
    public string Bar2 { get; set; } = "bar2-{0}";
    // `{0}` = the CS Rating band, 0 .. 6, 5 000 points apart.
    public string Rating { get; set; } = "csr-{0}";
    // `pips=K/N`: N pips, K of them lit. `nays=M` lights M more after them as no
    // votes: Lit takes K + M, and Yes `{0}` = K says where the no run starts.
    public string Pips { get; set; } = "pips-{0}";
    public string Lit { get; set; } = "lit-{0}";
    public string Yes { get; set; } = "yes-{0}";
    public string Section { get; set; } = "section";
    public string Group { get; set; } = "grp-{0}";
    // The row holding the highest `bar=`, when no other ties it.
    public string Lead { get; set; } = "lead";
    public string Primary { get; set; } = "primary";
    // A row carrying a number key (MenuLayoutProfile.Row.KeyVar).
    public string HasKey { get; set; } = "has-key";
    public string HasSub { get; set; } = "has-sub";
    // `unit=`: a word after the name ("1 210" won), flowing beside it in views that want it.
    public string HasUnit { get; set; } = "has-unit";
    // `badge=`: a small boxed label after the name, on its own ground (a map's kind).
    public string HasBadge { get; set; } = "has-badge";
    public string HasTag { get; set; } = "has-tag";
    public string HasTag2 { get; set; } = "has-tag2";
    public string HasNote { get; set; } = "has-note";
    public string HasValue { get; set; } = "has-value";
    public string HasGlyph { get; set; } = "has-glyph";
    // `vglyph=`: the value's own glyph (a currency), leaving `glyph=` for the entry's icon.
    public string HasValueGlyph { get; set; } = "has-vglyph";
    public string HasTagGlyph { get; set; } = "has-tagglyph";
    public string HasValueUnit { get; set; } = "has-vunit";
    public string HasMark { get; set; } = "has-mark";
    public string HasInline { get; set; } = "has-inline";
    public string HasRating { get; set; } = "has-rating";
    public string HasPips { get; set; } = "has-pips";
    // On a tab whose label ended in a count.
    public string HasCount { get; set; } = "has-count";
    // A `glyph=` token's class on the glyph panel (customhud_icons.vcss).
    public string Glyph { get; set; } = "ico-{0}";
    // On a slot holding an inline entry: `{0}` = disabled | danger | primary | on.
    public string InlineState { get; set; } = "inline-{0}";
}
