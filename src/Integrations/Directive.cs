using System.Globalization;
using System.Text.RegularExpressions;

namespace CustomHUD.Integrations;

// Values written into directives (docs/PluginIntegration.md, "What a Page Sends").
internal static partial class Directive
{
    // A value runs to the next ';', and a tab starts the next column.
    public static string Clean(string? text) => (text ?? string.Empty).Replace(';', ',').Replace('\t', ' ');

    // "4 812": the pages group thousands with a space whatever the server's culture.
    public static string Number(long value) => value.ToString("#,0", Grouped);

    // A price: grouped like Number, keeping up to two decimals ("2.5").
    public static string Money(decimal value) => value.ToString("#,0.##", Grouped);

    public static string Decimal(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    // A share for a meter, 0-100.
    public static int Bar(double percent) => (int)Math.Clamp(Math.Round(percent), 0, 100);

    public static double Percent(long part, long whole) => whole <= 0 ? 0 : Math.Round(part * 100.0 / whole, 1);

    // What the composer accepts where a value becomes part of a class name.
    public static bool IsToken(string value) => PlainToken().IsMatch(value);

    // A lower-case map name with a prefix (de_dust2), the shape FrameComposer reads a label as a map by.
    public static bool IsMapName(string value) => MapName().IsMatch(value);

    private static readonly NumberFormatInfo Grouped = new() { NumberGroupSeparator = " " };

    [GeneratedRegex("^[a-z0-9-]{1,24}$")]
    private static partial Regex PlainToken();

    [GeneratedRegex("^[a-z0-9]+_[a-z0-9_]+$")]
    private static partial Regex MapName();
}
