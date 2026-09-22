using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

public class IndexModel(HelpdeskStore store) : PageModel
{
    public IReadOnlyList<UserRecord> Users => store.Users;
    public IReadOnlyList<TechnicianRecord> Technicians => store.Technicians;
    public IReadOnlyList<AssetRecord> Assets => store.Assets;
    private IReadOnlyList<TicketRecord>? _tickets;
    // Most recently modified first; ticket number breaks ties.
    public IReadOnlyList<TicketRecord> Tickets => _tickets ??= store.Tickets.OrderByDescending(x => x.LastModifiedAt).ThenByDescending(x => x.Number).ToList();
    public BrandingSettings Branding => store.Branding;
    private IReadOnlyList<AssetInsights.ReviewItem>? _assetsToReview;
    // Warranty ending or replacement due within the review window (or already past), and overdue loans, most urgent first.
    public IReadOnlyList<AssetInsights.ReviewItem> AssetsToReview => _assetsToReview ??= AssetInsights.ReviewItems(store.Assets, store.AssetTypeLifespans, store.AssetReviewDays, AssetInsights.Today);
    public int ReviewWindowDays => store.AssetReviewDays;
    public string? HolderName(AssetRecord asset) => asset.AssignedUserId is { } id ? Users.FirstOrDefault(x => x.Id == id)?.Name : null;
    public int OpenTickets => Tickets.Count(x => x.Status is not "Closed");
    [TempData] public string? Message { get; set; }

    // The root page is reachable anonymously (see Program.cs) purely so it can send a signed-out visitor to the
    // staff portal instead of straight to the technician login - a technician still ends up here once signed in.
    public IActionResult OnGet() => User.Identity?.IsAuthenticated == true ? Page() : RedirectToPage("/Portal/Index");
}
