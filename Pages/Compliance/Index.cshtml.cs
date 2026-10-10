using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Compliance;

// The three DfE registers in one place: what needs acting on across them (ComplianceFindings), how each stands, and the
// registers to download in the DfE templates' own layout or to import. Each register shows only for someone who can
// reach it, and opening the page needs at least one of them.
public class IndexModel(HelpdeskStore store) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "register")] public string? Register { get; set; }
    [BindProperty(SupportsGet = true, Name = "level")] public string? Level { get; set; }

    public bool SeesAssets => store.UserCan(User, Modules.Assets, ModulePermission.Access);
    public bool SeesContracts => store.UserCan(User, Modules.Contracts, ModulePermission.Access);
    public bool SeesAccess => store.UserCan(User, Modules.Access, ModulePermission.Access);
    public bool CanExportAssets => store.UserCan(User, Modules.Assets, ModulePermission.View);
    public bool CanExportContracts => store.UserCan(User, Modules.Contracts, ModulePermission.View);
    public bool CanExportAccess => store.UserCan(User, Modules.Access, ModulePermission.View);
    public bool CanImportAssets => store.UserCan(User, Modules.Assets, ModulePermission.New);
    public bool CanImportContracts => store.UserCan(User, Modules.Contracts, ModulePermission.New) && store.UserCan(User, Modules.Contracts, ModulePermission.Edit);
    public bool CanImportStaff => store.UserCan(User, Modules.Requesters, ModulePermission.New) && store.UserCan(User, Modules.Requesters, ModulePermission.Edit);

    public DateOnly Today { get; } = AssetInsights.Today;
    public IReadOnlyList<ComplianceFinding> All { get; private set; } = [];
    public IReadOnlyList<ComplianceFinding> Shown { get; private set; } = [];
    public sealed record RegisterSummary(string Name, string Key, string Page, int Records, string RecordsLabel, int Danger, int Warning, string Note);
    public IReadOnlyList<RegisterSummary> Registers { get; private set; } = [];

    public static readonly (string Key, string Name)[] RegisterKeys =
        [("assets", ComplianceFindings.Assets), ("contracts", ComplianceFindings.Contracts), ("access", ComplianceFindings.Access)];

    public IActionResult OnGet()
    {
        if (!SeesAssets && !SeesContracts && !SeesAccess) return Forbid();
        All = ComplianceFindings.For(store, Today, SeesAssets, SeesContracts, SeesAccess);
        int Count(string register, ComplianceFinding.Levels level) => All.Count(x => x.Register == register && x.Level == level);

        var registers = new List<RegisterSummary>();
        if (SeesAssets)
        {
            var live = store.Assets.Where(x => !HelpdeskStore.IsDisposed(x)).ToList();
            var noCheck = live.Count(x => x.NextCheckDate is null);
            registers.Add(new(ComplianceFindings.Assets, "assets", "/Assets", live.Count, "assets in use", Count(ComplianceFindings.Assets, ComplianceFinding.Levels.Danger),
                Count(ComplianceFindings.Assets, ComplianceFinding.Levels.Warning), noCheck == 0 ? "Every asset has a next check date." : $"{noCheck} with no next check date."));
        }
        if (SeesContracts)
        {
            var live = store.Contracts.Where(ContractRules.IsLive).ToList();
            registers.Add(new(ComplianceFindings.Contracts, "contracts", "/Contracts", live.Count, "live contracts", Count(ComplianceFindings.Contracts, ComplianceFinding.Levels.Danger),
                Count(ComplianceFindings.Contracts, ComplianceFinding.Levels.Warning), $"{HelpdeskStore.FormatMoney(live.Sum(x => ContractRules.AnnualCost(x) ?? 0m))} a year."));
        }
        if (SeesAccess)
        {
            var live = store.AccessGrants.Count(x => x.IsActive(Today));
            var last = store.AccessReviews.FirstOrDefault();
            registers.Add(new(ComplianceFindings.Access, "access", "/Access/Index", live, "live access records", Count(ComplianceFindings.Access, ComplianceFinding.Levels.Danger),
                Count(ComplianceFindings.Access, ComplianceFinding.Levels.Warning), last is null ? "No access review recorded yet." : $"Last reviewed {AssetInsights.Format(last.ReviewedOn)} with {last.ReviewedWith}."));
        }
        Registers = registers;

        var registerName = RegisterKeys.FirstOrDefault(x => x.Key == Register).Name;
        if (registerName is null) Register = null;
        if (Level is not ("danger" or "warning")) Level = null;
        Shown = All.Where(x => registerName is null || x.Register == registerName)
            .Where(x => Level switch { "danger" => x.Level == ComplianceFinding.Levels.Danger, "warning" => x.Level == ComplianceFinding.Levels.Warning, _ => true })
            .ToList();
        return Page();
    }

    // A register in the DfE template's own layout, for the template, the Trust or an auditor.
    public IActionResult OnGetExport(string? register, string? format)
    {
        var (table, name, allowed) = register switch
        {
            "assets" => (RegisterExports.AssetRegister(store, Today), "asset-register", CanExportAssets),
            "contracts" => (RegisterExports.ContractsRegister(store, Today), "contracts-register", CanExportContracts),
            "access" => (RegisterExports.AccessRegister(store, Today), "access-control-register", CanExportAccess),
            _ => (null, "", false)
        };
        if (table is null) return NotFound();
        if (!allowed) return Forbid();
        return format == "csv"
            ? File(RegisterExports.ToCsv(table), "text/csv; charset=utf-8", RegisterExports.FileName(name, "csv"))
            : File(RegisterExports.ToXlsx(table), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", RegisterExports.FileName(name, "xlsx"));
    }

    public string? FilterUrl(string? register, string? level) => Url.Page("/Compliance/Index", new { register, level });
}
