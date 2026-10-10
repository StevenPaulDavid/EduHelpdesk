using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.Extensions.Caching.Memory;

namespace EduHelpdesk.Pages.Contracts;

// Importing the contracts register - the DfE template, filled in, or this register's own export.
public class ImportModel(HelpdeskStore store, IMemoryCache cache) : RegisterImportPage(store, cache)
{
    protected override string Kind => HelpdeskStore.ContractImport;
    protected override bool Allowed => Store.UserCan(User, Modules.Contracts, ModulePermission.New) && Store.UserCan(User, Modules.Contracts, ModulePermission.Edit);
    public override string Title => "Import contracts";
    public override string Intro => "The DfE contracts register template, filled in, or an export of this register. Contracts are matched by name: new ones are added, existing ones updated. Suppliers not in the directory are added to it, and the template's example rows are left unticked.";
    public override string BackPage => "/Contracts";
    public override string BackLabel => "Back to the contracts register";
}
