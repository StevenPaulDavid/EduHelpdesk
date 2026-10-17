using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

// The overview: what needs doing, rather than how big the directory is. Your own open tickets, what is overdue or
// about to be, what nobody has picked up, requesters waiting on a reply, your projects, and the asset and parts
// review lists. Each panel only appears for a role allowed the module it summarises, and - with several desks - when
// the desks being worked on (the top bar's desk switcher) use that part of the bar (Services/DeskAreas.cs).
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
    // Warranty ending or replacement due within the review window (or already past), overdue loans, and DfE checks and
    // end of support coming up, most urgent first.
    // Of the desks being worked on, as the asset list opens on them: plant on Site, kit on IT.
    public IReadOnlyList<AssetInsights.ReviewItem> AssetsToReview => _assetsToReview ??= AssetsOnShownDesks(AssetInsights.ReviewItems(store.Assets, store.AssetTypeLifespans, store.AssetReviewDays, AssetInsights.Today, store.DepartedUserIds, store.AssetCheckSettings));
    private IReadOnlyList<AssetInsights.ReviewItem> AssetsOnShownDesks(IReadOnlyList<AssetInsights.ReviewItem> items)
    {
        var desks = store.AssetDesksShown(User, null).Select(x => x.Id).ToHashSet();
        if (desks.Count == 0) return items;
        var deskOf = store.AssetDeskLookup();
        return items.Where(x => desks.Contains(deskOf(x.Asset.Type).Id)).ToList();
    }
    public int ReviewWindowDays => store.AssetReviewDays;
    private IReadOnlyList<PartRecord>? _partsToReview;
    // Parts at or below their reorder threshold, lowest quantity first.
    public IReadOnlyList<PartRecord> PartsToReview => _partsToReview ??= PartInsights.LowStock(store.Parts, store.PartsDefaultReorderThreshold);
    public int PartsReorderThreshold => store.PartsDefaultReorderThreshold;
    public string? HolderName(AssetRecord asset) => asset.AssignedUserId is { } id ? UserNames.GetValueOrDefault(id) : null;

    // The overview is open to anyone signed in, so each panel asks whether this role is allowed the module it summarises
    // - otherwise it would hand out asset tags, part names and ticket titles the role cannot reach anywhere else.
    public bool CanSeeAssets => store.UserCan(User, Modules.Assets, ModulePermission.Access) && store.AreaOn(User, DeskAreas.Assets);
    public bool CanOpenAsset => store.UserCan(User, Modules.Assets, ModulePermission.View);
    public bool CanSeeParts => store.UserCan(User, Modules.Parts, ModulePermission.Access) && store.AreaOn(User, DeskAreas.Parts);
    public bool CanEditParts => store.UserCan(User, Modules.Parts, ModulePermission.Edit);
    public bool CanSeeTickets => store.UserCan(User, Modules.Tickets, ModulePermission.Access);
    public bool CanOpenTicket => store.UserCan(User, Modules.Tickets, ModulePermission.View);
    public bool CanSeeProjects => store.UserCan(User, Modules.Projects, ModulePermission.Access) && store.AreaOn(User, DeskAreas.Projects);
    public bool CanOpenProject => store.UserCan(User, Modules.Projects, ModulePermission.View);
    // Onboarding: the officer sees it through the Onboarding module; technicians, whose IT tasks are in it, through Tickets.
    public bool CanSeeOnboarding => (store.UserCan(User, Modules.Onboarding, ModulePermission.Access) || CanSeeTickets) && store.AreaOn(User, DeskAreas.People);
    public bool CanOpenOnboarding => store.UserCan(User, Modules.Onboarding, ModulePermission.View) || CanOpenTicket;
    public bool HasOnboardingList => store.UserCan(User, Modules.Onboarding, ModulePermission.Access);
    public const int StartersWindowDays = 14;
    // Unfinished onboardings whose start date is within the next two weeks or already past, soonest first - the ones
    // to be working on now - with the starter's name.
    public IReadOnlyList<(OnboardingRecord Record, string Name)> NewStarters { get; private set; } = [];
    public int StartersSoon => NewStarters.Count(x => x.Record.StartDate >= Today);
    public int OnboardingOverdueTasks => NewStarters.Sum(x => x.Record.OverdueCount(Today));
    public bool CanSeeAssetReport => store.UserHasFlag(User, Modules.Flags.ReportAssets);
    // The contracts and access registers' due items. Assets have their own panel above, which already shows checks and
    // end of support.
    public bool CanSeeContracts => store.UserCan(User, Modules.Contracts, ModulePermission.Access) && store.AreaOn(User, DeskAreas.Compliance);
    public bool CanSeeAccess => store.UserCan(User, Modules.Access, ModulePermission.Access) && store.AreaOn(User, DeskAreas.Compliance);
    private IReadOnlyList<ComplianceFinding>? _compliance;
    public IReadOnlyList<ComplianceFinding> ComplianceDue => _compliance ??=
        ComplianceFindings.For(store, AssetInsights.Today, assets: false, contracts: CanSeeContracts, access: CanSeeAccess)
            .Where(x => x.Level >= ComplianceFinding.Levels.Warning).ToList();
    public bool CanSeePartsReport => store.UserHasFlag(User, Modules.Flags.ReportParts);
    // Slow saves or a nearly full disk (Settings → Database), for Administrators only.
    public IReadOnlyList<string> DatabaseWarnings =>
        EduHelpdesk.Services.RawDatabase.IsAdministrator(User) ? store.HealthWarnings(DateTime.UtcNow) : [];

    // Shown only to people who can do something about it (Settings: Edit opens Backups & data).
    public HelpdeskStore.BackupSettings? BackupWarning =>
        store.UserCan(User, Modules.Settings, ModulePermission.Edit) && store.Backups is { } backups && backups.NeedsAttention(DateTime.UtcNow) ? backups : null;

    public string StatusTone(string status) => store.StatusTone(status);

    // The panels with nothing in them, as one "All clear" list instead of a page of empty boxes. Each panel shows only
    // when it has something to show.
    public IReadOnlyList<string> AllClear()
    {
        var clear = new List<string>();
        if (CanSeeTickets)
        {
            if (Me is not null && MyTickets.Count == 0) clear.Add("Nothing assigned to you is open");
            if (DueTickets.Count == 0) clear.Add($"Nothing overdue or due within {DueSoonHours} hours");
            if (UnassignedTickets.Count == 0) clear.Add("Every open ticket has a technician");
            if (RepliesWaiting.Count == 0) clear.Add("Nobody is waiting on a reply");
        }
        if ((CanSeeContracts || CanSeeAccess) && ComplianceDue.Count == 0) clear.Add("Nothing due on the contracts or access registers");
        if (CanSeeAssets && AssetsToReview.Count == 0) clear.Add("No assets need review");
        if (CanSeeParts && PartsToReview.Count == 0) clear.Add("No parts need reordering");
        return clear;
    }

    // The root page is reachable anonymously (see Program.cs) purely so it can send a signed-out visitor to the
    // staff portal instead of straight to the technician login - a technician still ends up here once signed in.
    // The desks the overview covers (the desk switcher's), empty for every desk, and each ticket's number as shown (SITE-1043).
    public IReadOnlyList<string> ShownDesks { get; private set; } = [];
    // "The IT and Site desks, as chosen in the top bar." Null for every desk.
    public string? DeskSummary => ShownDesks.Count switch
    {
        0 => null,
        1 => $"The {ShownDesks[0]} desk, as chosen in the top bar.",
        _ => $"The {string.Join(", ", ShownDesks.Take(ShownDesks.Count - 1))} and {ShownDesks[^1]} desks, as chosen in the top bar."
    };
    private Func<string?, Desk> _deskOf = _ => null!;
    public string Ref(TicketRecord ticket) => HelpdeskStore.RefFor(_deskOf(ticket.Category), ticket);

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated != true) return RedirectToPage("/Portal/Index");

        UserNames = store.Users.ToDictionary(x => x.Id, x => x.Name);
        TechnicianNames = store.Technicians.ToDictionary(x => x.Id, x => x.Name);

        if (CanSeeOnboarding)
            NewStarters = store.Onboardings
                .Where(x => !x.IsCancelled && !x.AllDone && x.StartDate <= Today.AddDays(StartersWindowDays))
                .OrderBy(x => x.StartDate).ThenBy(x => x.TicketNumber)
                .Select(x => (x, UserNames.GetValueOrDefault(x.StarterId) ?? "Unknown"))
                .ToList();

        if (CanSeeTickets)
        {
            var meId = SignedInTechnician.Id(store, User);
            Me = meId is { } id ? store.Technicians.FirstOrDefault(x => x.Id == id) : null;
            // The overview is of the desks the top bar's desk switcher is on (HelpdeskStore.DeskSwitch), as the ticket
            // list opens on them; tickets assigned to them count wherever they are.
            _deskOf = store.DeskLookup();
            var myDesks = store.WorkingDesks(User);
            ShownDesks = myDesks.Select(x => x.Name).ToList();
            var open = store.TicketsFor(User).Where(x => !TicketInsights.IsClosed(x)
                && (myDesks.Count == 0 || myDesks.Any(d => d.Id == _deskOf(x.Category).Id) || (meId is { } assigned && x.TechnicianId == assigned))).ToList();

            // As the ticket list's My tickets: assigned to them, or an onboarding with an IT task given to them.
            var onboardings = store.Onboardings.ToDictionary(x => x.TicketNumber);
            var mine = open.Where(x => meId is { } me && (x.TechnicianId == me
                || onboardings.TryGetValue(x.Number, out var onboarding) && onboarding.ItTasks.Any(t => !t.IsDone && t.TechnicianId == me))).ToList();
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

        if (CanSeeProjects && SignedInTechnician.Id(store, User) is { } projectMe)
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
