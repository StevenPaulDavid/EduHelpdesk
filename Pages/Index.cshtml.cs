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
    public IReadOnlyList<TicketRecord> Tickets => store.Tickets;
    public BrandingSettings Branding => store.Branding;
    public int OpenTickets => Tickets.Count(x => x.Status is not "Closed");
    [TempData] public string? Message { get; set; }

    public void OnGet() { }
}
