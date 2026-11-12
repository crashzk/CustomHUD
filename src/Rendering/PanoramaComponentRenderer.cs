using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using CustomHUD.Config;
using SwiftlyS2.Shared.Menu;
using SwiftlyS2.Shared.Menu.Components;

namespace CustomHUD.Rendering;

// Teaches our renderer nine things the node tree alone cannot carry.
//
// 1. DISABLED STATE. A composed MenuNode says nothing about whether its
//    component is enabled -- components signal it by colouring their own text,
//    which we drop when we flatten to plain text. IMenuComponentRenderContext
//    has IsEnabled, so this is the only place it can be recovered.
//
// 2. FITTED TEXT. Core fits every TextComponent to MaxWidth (26 by default)
//    for CenterHTML's narrow column, cutting it with "..." or scrolling a
//    character window, BEFORE we see the node. A Panorama row is far wider,
//    so the component renders with fitting off and the layout's CSS ellipsis
//    cuts the whole string at the row's real edge. A scrolling component also
//    gets a marquee marker, which moves the animation into Panorama CSS,
//    where it costs the server nothing.
//
// 3. VALUE SLOTS. A toggle, input, slider or choice draws "{label}{Separator}"
//    and then its value as one line. Swapping that separator for a tab sends
//    the value to the layout's value slot (FrameComposer's tab convention),
//    so a card reads "Wear" over "0.15" instead of "Wear: 0.15". A choice's
//    brackets and a selector's neighbours go too: the layouts draw their own
//    cycle mark and arrows around the item.
//
// 4. CONTROL KIND. The same components are tagged `kind=` (toggle with `on=`,
//    slider with `pct=` and `num=`, input, choice), so a card layout can draw
//    a switch, a track or a value box instead of a line of text.
//
// 5. DIVIDERS. A DividerComponent becomes a bare `divider` marker, which the
//    composer drops: a row of rule glyphs only eats a slot in a framed layout.
//    A sidebar still draws a root divider, from the component itself.
//
// 6. ACCEPTED INPUT. Core draws "Accepted" in place of an input's new value
//    for two seconds; a value slot shows the value at once instead. A
//    rejected value keeps core's notice.
//
// 7. SUBMENUS. A row that opens a page is tagged `submenu`, which a page
//    layout draws as a chevron; row and card layouts ignore it.
//
// 8. PROGRESS. A ProgressBarComponent becomes its label, its percentage in the
//    value slot and a `bar=` meter (`kind=progress`), instead of a run of block
//    characters; GetProgress is public, so the plugin changes nothing.
//
// 9. FACTS. A focusable component whose activation does nothing (a plain
//    TextComponent) is tagged `info`, so a page layout draws it with no key,
//    no click target and no hover, like an integration's own facts.
//
// Registered against MenuComponentBase so every built-in component is covered
// by one registration. If a future core release stops matching derived types,
// components simply render as core fits them: no disabled class, no marquee,
// text cut at MaxWidth, dividers back as rows of rule glyphs, "Accepted" back
// in place of a new value, plain text keyed like an action. Degradation, not breakage.
//
// Also the one place core's translator is visible, so it is kept for rows
// PageFrame renders past core's window and for synthesized activations, and a
// new one is the cue to re-resolve the player's CustomHUD strings.
internal sealed class PanoramaComponentRenderer(
    CustomHudConfigProvider config, CoreTranslations translations, HudTranslations? strings)
    : IComponentRenderer
{
    public string RendererId => config.Current.RendererId;

    public Type ComponentType => typeof(MenuComponentBase);

    public bool TryRender(IMenuComponent component, IMenuComponentRenderContext context, out MenuNode? node)
    {
        node = null;
        if (translations.Remember(context.Player.PlayerID, context.Translate)) strings?.Refresh(context.Player);

        if (component is DividerComponent)
        {
            node = new MenuRawNode(config.Current.RendererId, "divider");
            return true;
        }

        if (component is ProgressBarComponent progress)
        {
            node = RenderProgress(progress, context);
            return true;
        }

        var text = component as TextComponent;
        var input = component as InputComponent;
        // A page layout draws a row that opens another page with a chevron; others ignore it.
        var submenu = component is SubmenuComponent;
        var fact = component.IsFocusable && IsFact(component.GetType());
        var fitted = text is { MaxWidth: >= 1f } && text.Overflow != MenuTextOverflow.None;
        var marquee = text is not null && MenuTextLayout.IsScrolling(text.Overflow);
        var disabled = !context.IsEnabled;
        var separator = ValueSeparator(component);
        if (!fitted && !marquee && !disabled && separator is null && input is null && !submenu && !fact) return false;

        var body = fitted ? RenderUnfitted(text!, context) : component.Render(context);
        if (input is not null) body = ShowAcceptedValue(body, input, context);
        if (separator is not null) body = SplitValue(body, separator);
        if (separator is not null && ChoiceItem(component) is { } item) body = PlainItem(body, item(component, context.Session));
        var markers = new List<string>(4);
        if (marquee) markers.Add("marquee");
        if (disabled) markers.Add("disabled");
        if (separator is not null) markers.Add(Kind(component, context.Session));
        if (submenu) markers.Add("submenu");
        if (fact) markers.Add("info");

        node = markers.Count == 0
            ? body
            : new MenuLineNode(new MenuNode[]
            {
                body,
                new MenuRawNode(config.Current.RendererId, string.Join(';', markers)),
            });
        return true;
    }

    // The component's own Render, so subclass extras such as raw nodes survive. Safe to flip
    // MaxWidth: core composes on the game thread, and only this menu's renderer draws its components.
    private static MenuNode RenderUnfitted(TextComponent text, IMenuComponentRenderContext context)
    {
        var width = text.MaxWidth;
        text.MaxWidth = 0f;
        try { return text.Render(context); }
        finally { text.MaxWidth = width; }
    }

    // What a card layout draws for a value component: a switch, a track, or a value box.
    private static string Kind(IMenuComponent component, IMenuSession session)
    {
        switch (component)
        {
            case ToggleComponent toggle:
                return toggle.GetValue(session) ? "kind=toggle;on=1" : "kind=toggle;on=0";
            case SliderComponent slider:
            {
                var value = slider.GetValue(session);
                var span = slider.Max - slider.Min;
                var position = span <= 0f ? 0f : Math.Clamp((value - slider.Min) / span, 0f, 1f);
                return $"kind=slider;pct={MathF.Round(position * 100f).ToString(CultureInfo.InvariantCulture)};"
                    + $"num={value.ToString(slider.ValueFormat, CultureInfo.InvariantCulture).Replace(';', ',')}";
            }
            case InputComponent:
                return "kind=input";
            default:
                // A selector steps with NavigateLeft/Right and ignores Select; a choice is the reverse.
                return IsSelector(component.GetType()) ? "kind=selector" : "kind=choice";
        }
    }

    private static readonly ConcurrentDictionary<Type, bool> Selectors = new();

    private static bool IsSelector(Type type) => Selectors.GetOrAdd(type, static t =>
    {
        for (var b = t; b is not null; b = b.BaseType)
            if (b.IsGenericType && b.GetGenericTypeDefinition() == typeof(SelectorComponent<>))
                return true;
        return false;
    });

    private static MenuNode RenderProgress(ProgressBarComponent progress, IMenuComponentRenderContext context)
    {
        var share = (int)MathF.Round(Math.Clamp(progress.GetProgress(context.Session), 0f, 1f) * 100f);
        var label = progress.TextProvider?.Invoke(context.Session) ?? progress.Text;
        var value = progress.ShowPercentage ? share.ToString(CultureInfo.InvariantCulture) + "%" : string.Empty;
        var markers = $"bar={share.ToString(CultureInfo.InvariantCulture)};kind=progress"
            + (context.IsEnabled ? string.Empty : ";disabled");
        return new MenuLineNode(new MenuNode[]
        {
            new MenuTextNode(value.Length == 0 ? label : label + '\t' + value, progress.Style),
            new MenuRawNode(context.RendererId, markers),
        });
    }

    private static readonly ConcurrentDictionary<Type, PropertyInfo?> SeparatorProperties = new();

    private static readonly ConcurrentDictionary<Type, bool> Facts = new();

    // A type that keeps MenuComponentBase's no-op action handler (a TextComponent, and a subclass
    // that only changes its text) does nothing when activated. A collection's item rows override it.
    internal static bool IsFact(Type type) => Facts.GetOrAdd(type, static t =>
        t.GetMethod(nameof(MenuComponentBase.HandleActionAsync), [typeof(MenuActionContext)])?.DeclaringType == typeof(MenuComponentBase));

    // Separator lives on the generic MenuValueComponent<TValue>, so it is found by reflection once per type.
    private static string? ValueSeparator(IMenuComponent component)
    {
        var property = SeparatorProperties.GetOrAdd(component.GetType(), static type =>
        {
            for (var t = type; t is not null; t = t.BaseType)
                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(MenuValueComponent<>))
                    return t.GetProperty(nameof(MenuValueComponent<bool>.Separator));
            return null;
        });
        return property?.GetValue(component) is string { Length: > 0 } separator ? separator : null;
    }

    // A busy component draws its waiting text instead and is left alone.
    private static MenuNode SplitValue(MenuNode node, string separator)
    {
        if (node is not MenuLineNode { Children: [MenuTextNode label, ..] } line
            || !label.Text.EndsWith(separator, StringComparison.Ordinal))
            return node;
        var children = line.Children.ToList();
        children[0] = label with { Text = label.Text[..^separator.Length] + '\t' };
        return line with { Children = children };
    }

    private static readonly ConcurrentDictionary<Type, Func<IMenuComponent, IMenuSession, string?>?> ChoiceItems = new();

    // The formatted item a selector or choice is on, through MenuChoiceComponentBase<T>.
    private static Func<IMenuComponent, IMenuSession, string?>? ChoiceItem(IMenuComponent component)
        => ChoiceItems.GetOrAdd(component.GetType(), static type =>
        {
            for (var t = type; t is not null; t = t.BaseType)
                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(MenuChoiceComponentBase<>))
                    return typeof(PanoramaComponentRenderer).GetMethod(nameof(ItemText), BindingFlags.NonPublic | BindingFlags.Static)!
                        .MakeGenericMethod(t.GetGenericArguments()[0])
                        .CreateDelegate<Func<IMenuComponent, IMenuSession, string?>>();
            return null;
        });

    // Null for an empty list without EmptyText, which keeps core's translated notice.
    private static string? ItemText<TItem>(IMenuComponent component, IMenuSession session)
    {
        var choice = (MenuChoiceComponentBase<TItem>)component;
        if (choice.Choices.Count == 0) return choice.EmptyText;
        return choice.Formatter(choice.Choices[Math.Clamp(choice.GetValue(session), 0, choice.Choices.Count - 1)]);
    }

    // The item alone after the label's tab; raw nodes (a plugin's directives) stay.
    private static MenuNode PlainItem(MenuNode node, string? item)
    {
        if (item is null || node is not MenuLineNode { Children: [MenuTextNode label, ..] } line || !label.Text.EndsWith('\t'))
            return node;
        return line with { Children = [label, new MenuTextNode(item), .. line.Children.Skip(1).OfType<MenuRawNode>()] };
    }

    // The status is the line's last text node, styled as the value it replaces (RenderValue's colours).
    private static MenuNode ShowAcceptedValue(MenuNode node, InputComponent input, IMenuComponentRenderContext context)
    {
        if (node is not MenuLineNode line) return node;
        var children = line.Children.ToList();
        var at = children.FindLastIndex(child => child is MenuTextNode);
        if (at < 0 || children[at] is not MenuTextNode status
            || !string.Equals(status.Text, input.AcceptedText ?? context.Translate("menu.input.accepted"), StringComparison.Ordinal)
            || input.GetValue(context.Session) is not { Length: > 0 } value)
            return node;
        var color = context.IsEnabled ? input.ValueColor : input.DisabledColor;
        children[at] = status with { Text = value, Style = color is null ? input.Style : input.Style.WithColor(color) };
        return line with { Children = children };
    }
}
