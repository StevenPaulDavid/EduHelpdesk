using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// Light or dark is chosen per device, not for the whole school: the Appearance switch in the footer stores the choice
// in a cookie, and a device that has never chosen gets the school's default (Settings → Branding & logo). "Device"
// follows the computer's or phone's own light/dark setting. The layout writes the result on <html data-theme>, and
// site.css holds both palettes, so there is no flash of the wrong one while a page loads.
public static class Themes
{
    public const string Cookie = "eh_theme";

    public static readonly Appearance[] All = [Appearance.Light, Appearance.Dark, Appearance.Device];

    public static string Key(Appearance appearance) => appearance switch
    {
        Appearance.Dark => "dark",
        Appearance.Device => "device",
        _ => "light"
    };

    public static string Label(Appearance appearance) => appearance switch
    {
        Appearance.Dark => "Dark",
        Appearance.Device => "Match device",
        _ => "Light"
    };

    public static Appearance? Parse(string? value) => All.Cast<Appearance?>().FirstOrDefault(x => string.Equals(Key(x!.Value), value, StringComparison.OrdinalIgnoreCase));

    // This device's own choice, if it has made one.
    public static Appearance? Chosen(HttpRequest request) => Parse(request.Cookies[Cookie]);

    public static Appearance For(HttpRequest request, BrandingSettings branding) => Chosen(request) ?? branding.DefaultAppearance;
}
