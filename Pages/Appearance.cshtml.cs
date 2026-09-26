using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

// The footer's Appearance switch: remembers light, dark or "match device" in this browser, then goes back to the page
// it was pressed on. Open to anyone, including the staff portal and the sign-in page (Program.cs). "school" forgets
// the choice, so the device follows the school's default again.
public class AppearanceModel : PageModel
{
    public IActionResult OnGet() => Redirect("/");

    public IActionResult OnPost(string? theme, string? returnUrl)
    {
        if (Themes.Parse(theme) is { } chosen)
            Response.Cookies.Append(Themes.Cookie, Themes.Key(chosen), new CookieOptions
            {
                HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = Request.IsHttps, IsEssential = true,
                Expires = DateTimeOffset.UtcNow.AddYears(1)
            });
        else
            Response.Cookies.Delete(Themes.Cookie);
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
    }
}
