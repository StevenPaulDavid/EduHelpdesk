using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

// Shown in place of any page that failed. Open to everyone (Program.cs), because a portal visitor or a signed-out one
// can hit an error too, and never shows the error itself - the reference ties it to the error log instead. The
// exception handler re-runs the request that failed, POSTs included, so both verbs are answered.
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public class ErrorModel : PageModel
{
    public string RequestId { get; private set; } = "";

    public void OnGet()
    {
        RequestId = HttpContext.TraceIdentifier;
        Response.StatusCode = StatusCodes.Status500InternalServerError;
    }

    public void OnPost() => OnGet();
}
