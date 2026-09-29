using System.Security.Claims;
using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace EduHelpdesk.Pages.People;

// A one-page printout to hand to someone with a new account, or a new temporary password: where to go, what to sign in
// with, and the first few things to know. One version for technicians (the helpdesk), one for requesters (the staff
// portal). Adding an account comes straight here; the account pages link here to print it again.
//
// The password is only on it for an hour after it was set, and only while it is still the account's password
// (TemporaryPasswords). After that the sheet has a line to write one on.
public class QuickStartModel(HelpdeskStore store, TemporaryPasswords passwords) : PageModel
{
    private const string JustAddedKey = "QuickStartAdded";

    public bool ForTechnician { get; private set; }
    public string Name { get; private set; } = "";
    public string Email { get; private set; } = "";
    public string? Password { get; private set; }
    public bool MustChange { get; private set; }
    public bool JustAdded { get; private set; }
    public TechnicianRecord? Technician { get; private set; }
    public UserRecord? Person { get; private set; }
    public string Address { get; private set; } = "";
    // True when no address has been set in Settings, so the one on the sheet was worked out from this request.
    public bool AddressGuessed { get; private set; }
    public bool CanSetAddress => store.UserCan(User, Modules.Settings, ModulePermission.Edit);
    public bool TwoFactorRequired => store.RequireTwoFactor;
    public BrandingSettings Branding => store.Branding;
    public string? LogoVersion => store.LogoVersion;
    public string SignInAddress => Address + (ForTechnician ? "Login" : "Portal");
    public string BackUrl => ForTechnician
        ? JustAdded ? Url.Page("/People", new { tab = "technicians" })! : Url.Page("/People/Technician", new { id = Technician!.Id })!
        : JustAdded ? Url.Page("/People")! : Url.Page("/User", new { id = Person!.Id })!;

    // Adding an account marks it here, so a role that can add accounts but not edit them still sees the password it just
    // made, once. Anything else needs Edit.
    public static void MarkJustAdded(ITempDataDictionary tempData, Guid id) => tempData[JustAddedKey] = id.ToString();

    public IActionResult OnGet(Guid? technician, Guid? user)
    {
        // TempData hands a GUID back as a Guid, not the string it was given, so both sides are compared as text.
        JustAdded = (technician ?? user) is { } requested && TempData[JustAddedKey]?.ToString() == requested.ToString();
        if (technician is { } technicianId)
        {
            if (!store.UserCanAny(User, Modules.StaffAccounts, ModulePermission.New | ModulePermission.Edit)) return Forbid();
            Technician = store.Technicians.FirstOrDefault(x => x.Id == technicianId);
            if (Technician is null) return NotFound();
            // The same rule as the account page: only an Administrator deals with an Administrator's account.
            if (Technician.Role == StaffRoles.Administrator && !string.Equals(User.FindFirst(ClaimTypes.Role)?.Value, StaffRoles.Administrator, StringComparison.OrdinalIgnoreCase)) return Forbid();
            ForTechnician = true;
            (Name, Email, MustChange) = (Technician.Name, Technician.Email, Technician.RequirePasswordChange);
            if (JustAdded || store.UserCan(User, Modules.StaffAccounts, ModulePermission.Edit)) Password = passwords.Recall(Technician.Id, Technician.PasswordHash);
        }
        else if (user is { } userId)
        {
            if (!store.UserCanAny(User, Modules.Requesters, ModulePermission.New | ModulePermission.Edit)) return Forbid();
            Person = store.Users.FirstOrDefault(x => x.Id == userId);
            if (Person is null || Person.AnonymisedAt is not null) return NotFound();
            (Name, Email, MustChange) = (Person.Name, Person.Email, Person.RequirePasswordChange);
            if (JustAdded || store.UserCan(User, Modules.Requesters, ModulePermission.Edit)) Password = passwords.Recall(Person.Id, Person.PasswordHash);
        }
        else return NotFound();

        Address = store.SiteAddress;
        if (Address.Length == 0)
        {
            AddressGuessed = true;
            Address = GuessAddress(Request);
        }
        return Page();
    }

    // The address this page was opened from - unless that is this computer's own name for itself, which is no use on
    // anyone else's, in which case the computer's network name.
    public static string GuessAddress(HttpRequest request)
    {
        var host = request.Host;
        var name = host.Host;
        if (name is "localhost" or "127.0.0.1" or "[::1]" or "::1") name = Environment.MachineName.ToLowerInvariant();
        var defaultPort = host.Port is null || (request.IsHttps ? host.Port == 443 : host.Port == 80);
        return $"{request.Scheme}://{name}{(defaultPort ? "" : ":" + host.Port)}{request.PathBase}/";
    }
}
