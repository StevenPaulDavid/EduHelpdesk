using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

public class IndexModel(HelpdeskStore store) : PageModel
{
    public IReadOnlyList<UserRecord> Users => store.Users;
    public IReadOnlyList<TechnicianRecord> Technicians => store.Technicians;
    // The whole register, including disposals - the ticket list below looks up the asset a ticket was about, and that
    // has to keep resolving after the kit is scrapped.
    public IReadOnlyList<AssetRecord> Assets => store.Assets;
    // What the school actually has. Kept separate from Assets so the headline metric agrees with the finance report.
    public int AssetsTracked => store.Assets.Count(x => !HelpdeskStore.IsDisposed(x));
    private IReadOnlyList<TicketRecord>? _tickets;
    // Most recently modified first; ticket number breaks ties.
    public IReadOnlyList<TicketRecord> Tickets => _tickets ??= store.Tickets.OrderByDescending(x => x.LastModifiedAt).ThenByDescending(x => x.Number).ToList();
    public BrandingSettings Branding => store.Branding;
    private IReadOnlyList<AssetInsights.ReviewItem>? _assetsToReview;
    // Warranty ending or replacement due within the review window (or already past), and overdue loans, most urgent first.
    public IReadOnlyList<AssetInsights.ReviewItem> AssetsToReview => _assetsToReview ??= AssetInsights.ReviewItems(store.Assets, store.AssetTypeLifespans, store.AssetReviewDays, AssetInsights.Today);
    public int ReviewWindowDays => store.AssetReviewDays;
    private IReadOnlyList<PartRecord>? _partsToReview;
    // Parts at or below their reorder threshold, lowest quantity first.
    public IReadOnlyList<PartRecord> PartsToReview => _partsToReview ??= PartInsights.LowStock(store.Parts, store.PartsDefaultReorderThreshold);
    public int PartsReorderThreshold => store.PartsDefaultReorderThreshold;
    public string? HolderName(AssetRecord asset) => asset.AssignedUserId is { } id ? Users.FirstOrDefault(x => x.Id == id)?.Name : null;
    public int OpenTickets => Tickets.Count(x => x.Status is not "Closed");
    [TempData] public string? Message { get; set; }

    // The overview is open to anyone signed in, so each panel asks whether this role is allowed the module it summarises
    // - otherwise it would hand out asset tags, part names and ticket titles the role cannot reach anywhere else.
    public bool CanSeeAssets => store.UserCan(User, Modules.Assets, PermissionLevel.Access);
    public bool CanOpenAsset => store.UserCan(User, Modules.Assets, PermissionLevel.View);
    public bool CanSeeParts => store.UserCan(User, Modules.Parts, PermissionLevel.Access);
    public bool CanEditParts => store.UserCan(User, Modules.Parts, PermissionLevel.Edit);
    public bool CanSeeTickets => store.UserCan(User, Modules.Tickets, PermissionLevel.Access);
    public bool CanOpenTicket => store.UserCan(User, Modules.Tickets, PermissionLevel.View);
    public bool CanSeeStaff => store.UserCan(User, Modules.StaffAccounts, PermissionLevel.Access);
    public bool CanSeeRequesters => store.UserCan(User, Modules.Requesters, PermissionLevel.Access);
    public bool CanSeeAssetReport => store.UserHasFlag(User, Modules.Flags.ReportAssets);
    public bool CanSeePartsReport => store.UserHasFlag(User, Modules.Flags.ReportParts);

    // The root page is reachable anonymously (see Program.cs) purely so it can send a signed-out visitor to the
    // staff portal instead of straight to the technician login - a technician still ends up here once signed in.
    public IActionResult OnGet() => User.Identity?.IsAuthenticated == true ? Page() : RedirectToPage("/Portal/Index");
}
