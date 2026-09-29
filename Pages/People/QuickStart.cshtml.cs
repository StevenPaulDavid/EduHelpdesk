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
// A batch - an import, or everyone still without a password (TemporaryPasswords.Issue) - prints one guide a page.
//
// The password is only on it for an hour after it was set, and only while it is still the account's password
// (TemporaryPasswords). After that the sheet has a line to write one on.
public class QuickStartModel(HelpdeskStore store, TemporaryPasswords passwords) : PageModel
{
    private const string JustAddedKey = "QuickStartAdded";

    public sealed record Sheet(string Name, string Email, string? Password, bool MustChange, TechnicianRecord? Technician, UserRecord? Person)
    {
        public bool ForTechnician => Technician is not null;
    }

    public IReadOnlyList<Sheet> Sheets { get; private set; } = [];
    public bool ForTechnicians { get; private set; }
    public bool IsBatch { get; private set; }
    public bool JustAdded { get; private set; }
    public string Address { get; private set; } = "";
    // True when no address has been set in Settings, so the one on the sheet was worked out from this request.
    public bool AddressGuessed { get; private set; }
    public bool CanSetAddress => store.UserCan(User, Modules.Settings, ModulePermission.Edit);
    public bool TwoFactorRequired => store.RequireTwoFactor;
    public bool RecoveryKeysAllowed => store.AllowRecoveryKeys;
    public BrandingSettings Branding => store.Branding;
    public string? LogoVersion => store.LogoVersion;
    public string SignInAddress => Address + (ForTechnicians ? "Login" : "Portal");
    public string BackUrl => IsBatch || JustAdded || Sheets.Count != 1
        ? Url.Page("/People", new { tab = ForTechnicians ? "technicians" : "users" })!
        : ForTechnicians ? Url.Page("/People/Technician", new { id = Sheets[0].Technician!.Id })! : Url.Page("/User", new { id = Sheets[0].Person!.Id })!;

    // Adding an account marks it here, so a role that can add accounts but not edit them still sees the password it just
    // made, once. Anything else needs Edit.
    public static void MarkJustAdded(ITempDataDictionary tempData, Guid id) => tempData[JustAddedKey] = id.ToString();

    public IActionResult OnGet(Guid? technician, Guid? user, Guid? batch)
    {
        // TempData hands a GUID back as a Guid, not the string it was given, so both sides are compared as text.
        JustAdded = (technician ?? user) is { } requested && TempData[JustAddedKey]?.ToString() == requested.ToString();
        if (batch is { } batchId)
        {
            if (passwords.RecallBatch(batchId, User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "") is not { } issued)
            {
                TempData["Message"] = "Those quick start guides can't be printed with their passwords any more: they are only kept for an hour, by whoever issued them. Give the people concerned new temporary passwords to print fresh ones.";
                return RedirectToPage("/People");
            }
            IsBatch = true;
            ForTechnicians = issued.Technicians;
            Sheets = issued.Technicians
                ? issued.AccountIds.Select(id => store.Technicians.FirstOrDefault(x => x.Id == id)).OfType<TechnicianRecord>().Select(x => TechnicianSheet(x, true)).ToList()
                : issued.AccountIds.Select(id => store.Users.FirstOrDefault(x => x.Id == id)).OfType<UserRecord>().Where(x => x.AnonymisedAt is null).Select(x => RequesterSheet(x, true)).ToList();
        }
        else if (technician is { } technicianId)
        {
            if (!store.UserCanAny(User, Modules.StaffAccounts, ModulePermission.New | ModulePermission.Edit)) return Forbid();
            if (store.Technicians.FirstOrDefault(x => x.Id == technicianId) is not { } account) return NotFound();
            // The same rule as the account page: only an Administrator deals with an Administrator's account.
            if (account.Role == StaffRoles.Administrator && !string.Equals(User.FindFirst(ClaimTypes.Role)?.Value, StaffRoles.Administrator, StringComparison.OrdinalIgnoreCase)) return Forbid();
            ForTechnicians = true;
            Sheets = [TechnicianSheet(account, JustAdded || store.UserCan(User, Modules.StaffAccounts, ModulePermission.Edit))];
        }
        else if (user is { } userId)
        {
            if (!store.UserCanAny(User, Modules.Requesters, ModulePermission.New | ModulePermission.Edit)) return Forbid();
            if (store.Users.FirstOrDefault(x => x.Id == userId) is not { AnonymisedAt: null } account) return NotFound();
            Sheets = [RequesterSheet(account, JustAdded || store.UserCan(User, Modules.Requesters, ModulePermission.Edit))];
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

    private Sheet TechnicianSheet(TechnicianRecord x, bool showPassword) =>
        new(x.Name, x.Email, showPassword ? passwords.Recall(x.Id, x.PasswordHash) : null, x.RequirePasswordChange, x, null);

    private Sheet RequesterSheet(UserRecord x, bool showPassword) =>
        new(x.Name, x.Email, showPassword ? passwords.Recall(x.Id, x.PasswordHash) : null, x.RequirePasswordChange, null, x);

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
