using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Access;

// What access can be given to: systems (the MIS, Microsoft 365, the finance system, the firewall) and physical areas or
// keys (the server room, a master key, the alarm code). Added and changed in a drawer; retired rather than deleted once
// anyone has had access, so the history stays.
public class SystemsModel(HelpdeskStore store) : PageModel
{
    public sealed class Input
    {
        public Guid? Id { get; set; }
        public string? Name { get; set; }
        public string? Kind { get; set; }
        public string? Category { get; set; }
        public string? Owner { get; set; }
        public bool MfaRequired { get; set; }
        public bool HoldsPersonalData { get; set; }
        public Guid? ContractId { get; set; }
        public string? Notes { get; set; }
        public bool IsRetired { get; set; }
    }

    [BindProperty] public Input Form { get; set; } = new();
    [TempData] public string? Message { get; set; }
    public string? Error { get; private set; }
    // Which drawer to open again after a refused save: "add-blade" or "edit-blade-{id}".
    public string? Reopen { get; private set; }

    public bool CanAdd => store.UserCan(User, Modules.Access, ModulePermission.New);
    public bool CanEdit => store.UserCan(User, Modules.Access, ModulePermission.Edit);
    public bool CanDelete => store.UserCan(User, Modules.Access, ModulePermission.Delete);
    public bool CanSeeContracts => store.UserCan(User, Modules.Contracts, ModulePermission.Access);

    public IReadOnlyList<AccessResource> Resources => store.AccessResources;
    public IReadOnlyList<string> Categories => store.AccessCategories;
    public IReadOnlyList<ContractRecord> Contracts => store.Contracts;
    public IReadOnlyDictionary<Guid, int> LiveCounts { get; private set; } = new Dictionary<Guid, int>();
    public IReadOnlyDictionary<Guid, int> AllCounts { get; private set; } = new Dictionary<Guid, int>();

    public void OnGet() => Load();

    public IActionResult OnPostSave()
    {
        var isNew = Form.Id is null;
        if (!(isNew ? CanAdd : CanEdit)) return Forbid();
        var existing = Form.Id is { } id ? store.FindAccessResource(id) : null;
        var (ok, message, _) = store.SaveAccessResource(new AccessResource(Form.Id ?? Guid.Empty, Form.Name ?? "", existing?.CreatedAt ?? DateTime.UtcNow)
        {
            Kind = Form.Kind ?? "", Category = Form.Category ?? "", Owner = Form.Owner ?? "", MfaRequired = Form.MfaRequired,
            HoldsPersonalData = Form.HoldsPersonalData, ContractId = Form.ContractId, Notes = Form.Notes ?? "", IsRetired = Form.IsRetired
        });
        if (!ok)
        {
            Error = message;
            Reopen = isNew ? "add-blade" : $"edit-blade-{Form.Id}";
            Load();
            return Page();
        }
        Message = message;
        return RedirectToPage();
    }

    public IActionResult OnPostDelete(Guid id)
    {
        if (!CanDelete) return Forbid();
        Message = store.DeleteAccessResource(id) ?? "Deleted.";
        return RedirectToPage();
    }

    private void Load()
    {
        var today = AssetInsights.Today;
        var grants = store.AccessGrants;
        LiveCounts = grants.Where(x => x.IsActive(today)).GroupBy(x => x.ResourceId).ToDictionary(g => g.Key, g => g.Count());
        AllCounts = grants.GroupBy(x => x.ResourceId).ToDictionary(g => g.Key, g => g.Count());
    }
}
