using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.Extensions.Caching.Memory;

namespace EduHelpdesk.Pages.People;

// Importing a staff list - an export from the MIS or HR system - into People, for the access control register: who
// everyone is, what type, and when they started and left.
public class ImportModel(HelpdeskStore store, IMemoryCache cache) : RegisterImportPage(store, cache)
{
    protected override string Kind => HelpdeskStore.StaffImport;
    protected override bool Allowed => Store.UserCan(User, Modules.Requesters, ModulePermission.New) && Store.UserCan(User, Modules.Requesters, ModulePermission.Edit);
    public override string Title => "Import a staff list";
    public override string Intro => "A list of staff from the MIS or HR system, as .xlsx or .csv, with columns such as Name, Email, Type, Department, Start date and Leave date. People are matched by email, then name: new people are added, existing ones updated. A leave date marks them as having left, so the leaver check picks up their equipment and access. Nobody is given a portal password.";
    public override string BackPage => "/People";
    public override string BackLabel => "Back to people";
}
