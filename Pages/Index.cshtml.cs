using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

// The overview: what needs doing, rather than how big the directory is. Your own open tickets, what is overdue or
// about to be, what nobody has picked up, requesters waiting on a reply, your projects, and the asset and parts
// review lists. Each panel only appears for a role allowed the module it summarises.
public class IndexModel(HelpdeskStore store) : PageModel
{
    // How many rows each panel shows before pointing at the full list.
    public const int PanelSize = 6;
    public const int MyTicketsSize = 8;

    public BrandingSettings Branding => store.Branding;
    public DateTime Now { get; } = DateTime.UtcNow;
    public int DueSoonHours => store.TicketDueSoonHours;
    [TempData] public string? Message { get; set; }

    // "Me" as the ticket list sees it, so the overview's My tickets and the list's My tickets queue always agree.
    public TechnicianRecord? Me { get; private set; }
    public IReadOnlyList<TicketRecord> MyTickets { get; private set; } = [];
    public int MyOverdueCount { get; private set; }
    public IReadOnlyList<TicketRecord> DueTickets { get; private set; } = [];
    public int OverdueCount { get; private set; }
    public int DueSoonCount { get; private set; }
    public IReadOnlyList<TicketRecord> UnassignedTickets { get; private set; } = [];
    public IReadOnlyList<TicketRecord> RepliesWaiting { get; private set; } = [];
    public int RepliesForMe { get; private set; }
    public IReadOnlyList<ProjectRecord> MyProjects { get; private set; } = [];
    public int ProjectsAwaitingAssignment { get; private set; }
    public DateOnly Today { get; } = DateOnly.FromDateTime(DateTime.Now);
    public IReadOnlyDictionary<Guid, string> UserNames { get; private set; } = new Dictionary<Guid, string>();
    public IReadOnlyDictionary<Guid, string> TechnicianNames { get; private set; } = new Dictionary<Guid, string>();

    private IReadOnlyList<AssetInsights.ReviewItem>? _assetsToReview;
    // Warranty ending or replacement due within the review window (or already past), and overdue loans, most urgent first.
    public IReadOnlyList<AssetInsights.ReviewItem> AssetsToReview => _assetsToReview ??= AssetInsights.ReviewItems(store.Assets, store.AssetTypeLifespans, store.AssetReviewDays, AssetInsights.Today);
    public int ReviewWindowDays => store.AssetReviewDays;
    private IReadOnlyList<PartRecord>? _partsToReview;
    // Parts at or below their reorder threshold, lowest quantity first.
    public IReadOnlyList<PartRecord> PartsToReview => _partsToReview ??= PartInsights.LowStock(store.Parts, store.PartsDefaultReorderThreshold);
    public int PartsReorderThreshold => store.PartsDefaultReorderThreshold;
    public string? HolderName(AssetRecord asset) => asset.AssignedUserId is { } id ? UserNames.GetValueOrDefault(id) : null;

    // The overview is open to anyone signed in, so each panel asks whether this role is allowed the module it summarises
    // - otherwise it would hand out asset tags, part names and ticket titles the role cannot reach anywhere else.
    public bool CanSeeAssets => store.UserCan(User, Modules.Assets, ModulePermission.Access);
    public bool CanOpenAsset => store.UserCan(User, Modules.Assets, ModulePermission.View);
    public bool CanSeeParts => store.UserCan(User, Modules.Parts, ModulePermission.Access);
    public bool CanEditParts => store.UserCan(User, Modules.Parts, ModulePermission.Edit);
    public bool CanSeeTickets => store.UserCan(User, Modules.Tickets, ModulePermission.Access);
    public bool CanOpenTicket => store.UserCan(User, Modules.Tickets, ModulePermission.View);
    public bool CanSeeProjects => store.UserCan(User, Modules.Projects, ModulePermission.Access);
    public bool CanOpenProject => store.UserCan(User, Modules.Projects, ModulePermission.View);
    public bool CanSeeAssetReport => store.UserHasFlag(User, Modules.Flags.ReportAssets);
    public bool CanSeePartsReport => store.UserHasFlag(User, Modules.Flags.ReportParts);
    public bool CanChangeWorkingAs => store.UserHasFlag(User, Modules.Flags.WorkingAs);
    // Shown only to people who can do something about it (Settings: Edit opens Backups & data).
    public HelpdeskStore.BackupSettings? BackupWarning =>
        store.UserCan(User, Modules.Settings, ModulePermission.Edit) && store.Backups is { } backups && backups.NeedsAttention(DateTime.UtcNow) ? backups : null;

    public string StatusTone(string status) => store.StatusTone(status);

    // The root page is reachable anonymously (see Program.cs) purely so it can send a signed-out visitor to the
    // staff portal instead of straight to the technician login - a technician still ends up here once signed in.
    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated != true) return RedirectToPage("/Portal/Index");

        UserNames = store.Users.ToDictionary(x => x.Id, x => x.Name);
        TechnicianNames = store.Technicians.ToDictionary(x => x.Id, x => x.Name);

        if (CanSeeTickets)
        {
            var meId = WorkingAs.Resolve(store, User, Request);
            Me = meId is { } id ? store.Technicians.FirstOrDefault(x => x.Id == id) : null;
            var open = store.Tickets.Where(x => !TicketInsights.IsClosed(x)).ToList();

            var mine = open.Where(x => meId is { } me && x.TechnicianId == me).ToList();
            MyOverdueCount = mine.Count(x => TicketInsights.IsOverdue(x, Now));
            MyTickets = ByUrgency(mine).ToList();

            var due = open.Where(x => TicketInsights.DueStateOf(x, Now, DueSoonHours) is DueState.Overdue or DueState.Soon).ToList();
            OverdueCount = due.Count(x => TicketInsights.IsOverdue(x, Now));
            DueSoonCount = due.Count - OverdueCount;
            DueTickets = due.OrderBy(x => x.DueDate).ThenBy(x => x.Number).ToList();

            // Most urgent first, then longest waiting - nobody has picked these up yet.
            UnassignedTickets = ByUrgency(open.Where(x => x.TechnicianId is null)).ThenBy(x => x.CreatedAt).ToList();

            // The requester had the last word in the portal. Yours first, then the longest waiting.
            var replies = open.Where(HelpdeskStore.AwaitingTechnician).ToList();
            RepliesForMe = replies.Count(x => meId is { } me && x.TechnicianId == me);
            RepliesWaiting = replies.OrderBy(x => meId is { } me && x.TechnicianId == me ? 0 : 1).ThenBy(x => x.LastModifiedAt).ToList();
        }

        if (CanSeeProjects && WorkingAs.SignedInTechnicianId(store, User) is { } projectMe)
        {
            var active = store.Projects.Where(x => x.IsActive).ToList();
            MyProjects = active.Where(x => x.TechnicianId == projectMe).OrderBy(x => x.DueDate).ThenBy(x => x.EffectivePriority).ToList();
            ProjectsAwaitingAssignment = active.Count(x => x.TechnicianId is null);
        }
        else if (CanSeeProjects)
            ProjectsAwaitingAssignment = store.Projects.Count(x => x.IsActive && x.TechnicianId is null);

        return Page();
    }

    // Overdue first, then soonest due (no due date last), then most urgent priority.
    private IOrderedEnumerable<TicketRecord> ByUrgency(IEnumerable<TicketRecord> tickets) => tickets
        .OrderBy(x => TicketInsights.IsOverdue(x, Now) ? 0 : 1)
        .ThenBy(x => x.DueDate is null || x.IsSlaPaused ? 1 : 0)
        .ThenBy(x => x.DueDate)
        .ThenBy(x => PriorityRank(x.Priority));

    private static int PriorityRank(string priority) =>
        Array.FindIndex(["Urgent", "High", "Normal", "Low"], x => string.Equals(x, priority, StringComparison.OrdinalIgnoreCase)) is var i and >= 0 ? i : 4;
}
