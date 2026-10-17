using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.Extensions.Caching.Memory;

namespace EduHelpdesk.Pages.People;

// Importing a staff list - an export from the MIS or HR system - into People, for the access control register: who
// everyone is, what type, and when they started and left.
// It is the one way to bring requesters in from a file, so it can also give the people it adds a temporary portal
// password and print their quick start guides (Pages/People/QuickStart), as adding one by hand does.
public class ImportModel(HelpdeskStore store, IMemoryCache cache, TemporaryPasswords passwords) : RegisterImportPage(store, cache)
{
    [Microsoft.AspNetCore.Mvc.BindProperty] public bool TemporaryPasswords { get; set; }
    public override bool OffersPortalPasswords => true;

    public override Microsoft.AspNetCore.Mvc.IActionResult OnPostApply(string? t, int[]? rows)
    {
        var before = Store.Users.Select(x => x.Id).ToHashSet();
        var result = base.OnPostApply(t, rows);
        var added = Store.Users.Where(x => !before.Contains(x.Id)).ToList();
        if (!TemporaryPasswords || added.Count == 0) return result;
        var batch = passwords.IssueToRequesters(Store, added, User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "");
        return RedirectToPage("/People/QuickStart", new { batch });
    }

    protected override string Kind => HelpdeskStore.StaffImport;
    protected override bool Allowed => Store.UserCan(User, Modules.Requesters, ModulePermission.New) && Store.UserCan(User, Modules.Requesters, ModulePermission.Edit);
    public override string Title => "Import a staff list";
    public override string Intro => "A list of staff from the MIS or HR system, as .xlsx or .csv, with columns such as Name, Email, Type, Department, Start date and Leave date. People are matched by email, then name: new people are added, existing ones updated. A leave date marks them as having left, so the leaver check picks up their equipment and access. Tick the box before applying to give the people it adds a temporary portal password.";
    public override string BackPage => "/People";
    public override string BackLabel => "Back to people";
}
