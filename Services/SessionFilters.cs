using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EduHelpdesk.Services;

// "Must change password" used to be a redirect straight after sign-in and nothing more: typing any other address carried
// on with the password someone else had chosen. This runs before every page, for both kinds of account.
// - A technician with RequirePasswordChange reaches only /ChangePassword and signing out. The portal is shut too,
//   since "Open staff portal" would otherwise be a way round it.
// - A requester who signed in to the portal with a password a technician set reaches only /Portal/Password and
//   signing out. (Someone using the portal through their helpdesk sign-in has no portal password to change.)
// Registered for every Razor Page in Program.cs.
public sealed class PasswordChangeFilter(HelpdeskStore store, PortalIdentity portal) : IAsyncPageFilter
{
    private static readonly HashSet<string> TechnicianAllowed = new(StringComparer.OrdinalIgnoreCase)
        { "/ChangePassword", "/Logout", "/Login", "/Error", "/AccessDenied" };

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var page = context.ActionDescriptor.ViewEnginePath;
        var handler = context.HandlerMethod?.Name;
        var isPortal = page.StartsWith("/Portal/", StringComparison.OrdinalIgnoreCase);

        if (TechnicianMustChange(context.HttpContext.User)
            && !TechnicianAllowed.Contains(page)
            // Portal pages work off the portal's own sign-in, except the one handler that turns a helpdesk sign-in
            // into a portal one.
            && (!isPortal || (page.Equals("/Portal/Index", StringComparison.OrdinalIgnoreCase) && handler == "Technician")))
        {
            context.Result = new RedirectToPageResult("/ChangePassword");
            return;
        }

        if (isPortal && !page.Equals("/Portal/Password", StringComparison.OrdinalIgnoreCase) && handler != "Switch"
            && portal.Validate(context.HttpContext.Request, store) is { Kind: not PortalIdentity.Kind.Technician } session
            && store.Users.FirstOrDefault(x => x.Id == session.UserId) is { RequirePasswordChange: true })
        {
            context.Result = new RedirectToPageResult("/Portal/Password");
            return;
        }

        await next();
    }

    private bool TechnicianMustChange(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true
        && Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id)
        && store.Technicians.FirstOrDefault(x => x.Id == id) is { RequirePasswordChange: true };
}

// Keeps a portal session's idle clock moving while it's in use, and drops a cookie that no longer signs anyone in
// (expired, password changed, requester deactivated) so the browser stops sending it. Portal pages only.
public sealed class PortalSessionFilter(HelpdeskStore store, PortalIdentity portal) : IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (portal.Validate(request, store) is { } session) portal.Touch(context.HttpContext.Response, session);
        else if (request.Cookies.ContainsKey(PortalIdentity.CookieName)) PortalIdentity.Clear(context.HttpContext.Response);
        await next();
    }
}
