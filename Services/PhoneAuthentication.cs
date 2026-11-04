using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace EduHelpdesk.Services;

// Signing in as a paired phone: "Authorization: Bearer ehp_…" on the phone app's calls (PhoneEndpoints), checked against
// HelpdeskStore.Phones on every request. The account comes out with the same claims as the sign-in cookie, so
// UserCan, the desks someone sees and the name on history lines all work exactly as they do in the browser - the phone
// can do only what the role allows. Used by /api/mobile/v1 alone: the rest of the helpdesk never accepts a phone token,
// and these endpoints never accept the cookie.
public sealed class PhoneAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, HelpdeskStore store)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "Phone";
    // Which paired phone the request came from, for Sign out.
    public const string PhoneClaim = "eduhelpdesk:phone";
    private const string RefusalItem = "EduHelpdesk.PhoneRefusal";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return Task.FromResult(AuthenticateResult.NoResult());
        var check = store.CheckPhoneToken(header[7..].Trim(), Context.Connection.RemoteIpAddress);
        if (check is { Access: HelpdeskStore.PhoneAccess.Ok, Technician: { } technician, Phone: { } phone })
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, technician.Id.ToString()),
                new Claim(ClaimTypes.Name, technician.Name),
                new Claim(ClaimTypes.Email, technician.Email),
                new Claim(ClaimTypes.Role, technician.Role),
                new Claim(PhoneClaim, phone.Id.ToString())
            ], Scheme));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme)));
        }
        // Turned off for the school, or not on the role: still paired, and working again once that changes.
        if (check.Technician is { } refused && store.PhoneRefusal(refused) is { } why) Context.Items[RefusalItem] = why;
        return Task.FromResult(AuthenticateResult.Fail("This phone isn't signed in."));
    }

    // 401 means "pair again" to the app; 403 means "not now", and the phone stays paired.
    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        Context.Items[RefusalItem] is string refusal
            ? Answer(StatusCodes.Status403Forbidden, refusal)
            : Answer(StatusCodes.Status401Unauthorized, "This phone has been signed out. Pair it again to carry on.");

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        Answer(StatusCodes.Status403Forbidden, "Your role doesn't allow that.");

    private Task Answer(int status, string message)
    {
        Response.StatusCode = status;
        return Response.WriteAsJsonAsync(new { message });
    }
}
