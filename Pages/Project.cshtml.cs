using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

// One project, helpdesk side. The page needs Projects: View to open (Program.cs); every change is checked here, because
// tidying up, assigning and deleting are three different permissions on the same page.
public class ProjectModel(HelpdeskStore store, ILogger<ProjectModel> logger) : PageModel
{
    public ProjectRecord? Project { get; private set; }
    public UserRecord? Requester { get; private set; }
    public TechnicianRecord? Technician { get; private set; }
    public IReadOnlyList<ProjectWorkload> Workloads { get; private set; } = [];
    public IReadOnlyList<string> RequirementOptions { get; private set; } = [];
    public bool CanEdit => store.UserCan(User, Modules.Projects, ModulePermission.Edit);
    public bool CanDelete => store.UserCan(User, Modules.Projects, ModulePermission.Delete);
    public bool CanAssign => store.UserHasFlag(User, Modules.Flags.AssignProjects);
    public DateOnly Today { get; } = DateOnly.FromDateTime(DateTime.Now);
    public DateTime Now { get; } = DateTime.UtcNow;
    public IReadOnlyList<SupplierRecord> Suppliers { get; private set; } = [];
    // The spending bands, for the reference panel behind the ⓘ, and where this project sits in them today.
    public IReadOnlyList<SpendingBand> Bands { get; private set; } = [];
    public bool BandsIncludeVat { get; private set; }
    public decimal BandTotal { get; private set; }
    public SpendingBand? CurrentBand { get; private set; }
    public IReadOnlyDictionary<Guid, string> SupplierNames { get; private set; } = new Dictionary<Guid, string>();
    // The helpdesk tickets this project is linked to. Links are listed for anyone who can see the project; a ticket
    // only opens for someone who can view tickets.
    public IReadOnlyList<TicketRecord> LinkedTickets { get; private set; } = [];
    public bool CanOpenTickets => store.UserCan(User, Modules.Tickets, ModulePermission.View);
    [TempData] public string? Message { get; set; }

    public IActionResult OnGet(int number)
    {
        Project = store.FindProject(number);
        if (Project is null) return NotFound();
        Requester = store.Users.FirstOrDefault(x => x.Id == Project.RequesterId);
        Technician = Project.TechnicianId is { } techId ? store.Technicians.FirstOrDefault(x => x.Id == techId) : null;
        // Everything already ticked on the project stays offered, even if it has since been taken off the Settings list.
        RequirementOptions = store.PurchasingRequirements
            .Concat(Project.PurchasingRequirements.Where(x => !store.PurchasingRequirements.Contains(x, StringComparer.OrdinalIgnoreCase)))
            .ToList();
        if (CanAssign) Workloads = store.ProjectWorkloads();
        Suppliers = store.Suppliers;
        SupplierNames = Suppliers.ToDictionary(x => x.Id, x => x.Name);
        Bands = store.SpendingBands;
        BandsIncludeVat = store.SpendingBandsIncludeVat;
        BandTotal = store.BandTotal(Project);
        CurrentBand = store.BandFor(BandTotal);
        LinkedTickets = store.Tickets.Where(x => Project.TicketNumbers.Contains(x.Number)).OrderBy(x => x.Number).ToList();
        return Page();
    }

    public IActionResult OnPostLinkTicket(int number, int? ticket)
    {
        if (!CanEdit) return Forbid();
        return Linked(number, ticket is null ? (false, "Enter the number of the ticket to link.") : store.LinkProjectTicket(number, ticket.Value));
    }

    public IActionResult OnPostUnlinkTicket(int number, int ticket)
    {
        if (!CanEdit) return Forbid();
        return Linked(number, store.UnlinkProjectTicket(number, ticket));
    }

    private IActionResult Linked(int number, (bool Ok, string Message) result)
    {
        Message = result.Message;
        return RedirectToPage(null, null, new { number }, "tickets");
    }

    public IActionResult OnPostDetails(int number, string? title, DateOnly? dueDate, string? itemsWanted, List<string>? requirements, string? other)
    {
        if (!CanEdit) return Forbid();
        return Done(number, store.UpdateProjectDetails(number, title, dueDate, itemsWanted, requirements, other));
    }

    public IActionResult OnPostAssign(int number, string? technicianId, int priority)
    {
        if (!CanAssign) return Forbid();
        // Blank is a deliberate "nobody yet"; anything else has to be a real id, so a mangled form can't quietly unassign.
        Guid? technician = null;
        if (!string.IsNullOrWhiteSpace(technicianId))
        {
            if (!Guid.TryParse(technicianId, out var id)) return Done(number, (false, "That technician couldn't be found."));
            technician = id;
        }
        return Done(number, store.AssignProject(number, technician, priority));
    }

    public IActionResult OnPostStatus(int number, string? status)
    {
        if (!CanEdit) return Forbid();
        return Done(number, store.SetProjectStatus(number, status));
    }

    public IActionResult OnPostClose(int number, string? outcome, string? note)
    {
        if (!CanEdit) return Forbid();
        return Done(number, store.CloseProject(number, outcome, note));
    }

    public IActionResult OnPostNote(int number, string? text, bool isInternal)
    {
        if (!CanEdit) return Forbid();
        return Done(number, store.AddProjectNote(number, text, isInternal));
    }

    public IActionResult OnPostDelete(int number)
    {
        if (!CanDelete) return Forbid();
        var (ok, message) = store.DeleteProject(number);
        Message = message;
        return ok ? RedirectToPage("/Projects") : RedirectToPage(new { number });
    }

    // Items, sub-items and the suppliers quoting for them. All are Projects: Edit - they are the technician's day-to-day
    // work on the project. Adding a brand-new supplier from here is included: asking a new firm for a quote is part of
    // the job, and the Supplier directory's own permissions still govern editing and deleting them afterwards.
    // A new item opens straight away - adding its sub-items and suppliers is what comes next.
    public IActionResult OnPostAddItem(int number, string? name, int quantity)
    {
        if (!CanEdit) return Forbid();
        var (ok, message) = store.AddProjectItem(number, name, quantity);
        Message = message;
        var added = ok ? store.FindProject(number)?.Items.LastOrDefault() : null;
        if (added is not null) OpenItem = $"{added.Id:N}";
        return RedirectToPage(null, null, new { number }, added is null ? "items" : $"item-{added.Id:N}");
    }
    public IActionResult OnPostItemsFromRequest(int number) => Edit(number, null, () => store.AddItemsFromRequest(number));
    public IActionResult OnPostUpdateItem(int number, Guid itemId, string? name, int quantity) => Edit(number, itemId, () => store.UpdateProjectItem(number, itemId, name, quantity));
    public IActionResult OnPostDeleteItem(int number, Guid itemId) => Edit(number, null, () => store.DeleteProjectItem(number, itemId));
    public IActionResult OnPostMoveItem(int number, Guid itemId, int direction) => Edit(number, itemId, () => store.MoveProjectItem(number, itemId, direction));
    public IActionResult OnPostAddSubItem(int number, Guid itemId, string? name, int quantity) => Edit(number, itemId, () => store.AddSubItem(number, itemId, name, quantity));
    public IActionResult OnPostUpdateSubItem(int number, Guid itemId, Guid subItemId, string? name, int quantity) => Edit(number, itemId, () => store.UpdateSubItem(number, itemId, subItemId, name, quantity));
    public IActionResult OnPostDeleteSubItem(int number, Guid itemId, Guid subItemId) => Edit(number, itemId, () => store.DeleteSubItem(number, itemId, subItemId));
    public IActionResult OnPostAddSupplier(int number, Guid itemId, string? supplierId, string? newName, string? newEmail, bool everyItem) => Edit(number, itemId, () =>
        Guid.TryParse(supplierId, out var id) ? store.AddItemSupplier(number, itemId, id, everyItem)
        : !string.IsNullOrWhiteSpace(newName) ? store.QuickAddItemSupplier(number, itemId, newName, newEmail, everyItem)
        : (false, "Choose a supplier from the list, or type a new one's name."));
    public IActionResult OnPostQuoteStatus(int number, Guid itemId, Guid supplierId, string? status) => Edit(number, itemId, () => store.SetQuoteStatus(number, itemId, supplierId, status));
    public IActionResult OnPostValidUntil(int number, Guid itemId, Guid supplierId, DateOnly? validUntil) => Edit(number, itemId, () => store.SetQuoteValidUntil(number, itemId, supplierId, validUntil));
    public IActionResult OnPostRemoveSupplier(int number, Guid itemId, Guid supplierId) => Edit(number, itemId, () => store.RemoveItemSupplier(number, itemId, supplierId));

    // A supplier's quote: its files, its prices, and whether it is the one the item goes with.
    public IActionResult OnPostUploadQuote(int number, Guid itemId, Guid supplierId, List<IFormFile>? files, bool keepPrevious) => EditQuote(number, itemId, supplierId, () =>
    {
        var streams = (files ?? []).Where(x => x is not null).Select(x => ((string?)x.FileName, x.OpenReadStream(), x.Length)).ToList();
        try { return store.UploadQuoteDocuments(number, itemId, supplierId, streams, keepPrevious); }
        finally { foreach (var (_, stream, _) in streams) stream.Dispose(); }
    });
    public IActionResult OnPostRemoveQuoteFile(int number, Guid itemId, Guid supplierId, Guid documentId) => EditQuote(number, itemId, supplierId, () => store.RemoveQuoteDocument(number, itemId, supplierId, documentId));
    public IActionResult OnPostDeleteQuoteVersion(int number, Guid itemId, Guid supplierId, Guid versionId) => EditQuote(number, itemId, supplierId, () => store.DeleteQuoteVersion(number, itemId, supplierId, versionId));
    public IActionResult OnPostQuoteReference(int number, Guid itemId, Guid supplierId, string? reference) => EditQuote(number, itemId, supplierId, () => store.SetQuoteReference(number, itemId, supplierId, reference));
    public IActionResult OnPostAddPaymentLine(int number, Guid itemId, Guid supplierId, string? description, string? amount, string? frequency, int termYears, string? vat) =>
        EditQuote(number, itemId, supplierId, () => store.AddPaymentLine(number, itemId, supplierId, description, amount, frequency, termYears, vat));
    public IActionResult OnPostUpdatePaymentLine(int number, Guid itemId, Guid supplierId, Guid lineId, string? description, string? amount, string? frequency, int termYears, string? vat) =>
        EditQuote(number, itemId, supplierId, () => store.UpdatePaymentLine(number, itemId, supplierId, lineId, description, amount, frequency, termYears, vat));
    public IActionResult OnPostDeletePaymentLine(int number, Guid itemId, Guid supplierId, Guid lineId) => EditQuote(number, itemId, supplierId, () => store.DeletePaymentLine(number, itemId, supplierId, lineId));
    public IActionResult OnPostChooseQuote(int number, Guid itemId, string? supplierId) =>
        Edit(number, itemId, () => store.ChooseQuote(number, itemId, Guid.TryParse(supplierId, out var id) ? id : null));

    // Quote files open for anyone who can view the project. Sent the same way as ticket attachments: never sniffed,
    // sandboxed, and only pictures shown in the page - everything else downloads.
    public IActionResult OnGetQuoteFile(int number, Guid itemId, Guid supplierId, Guid documentId, bool inline)
    {
        if (store.FindQuoteDocument(number, itemId, supplierId, documentId) is not { } found) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; img-src 'self' data:; style-src 'unsafe-inline'; sandbox";
        if (inline && HelpdeskStore.IsInlineImage(found.Document)) return PhysicalFile(found.Path, found.Document.ContentType);
        return PhysicalFile(found.Path, found.Document.ContentType, found.Document.FileName);
    }

    // The proposal PDF, at any stage: before the project is marked ready it is how the technician checks it, and it
    // says "draft" on every page. Anyone who can view the project can download it.
    public IActionResult OnGetProposal(int number)
    {
        if (store.ProposalFor(number) is not { } input) return NotFound();
        try
        {
            return File(ProposalPdf.Build(input), "application/pdf", ProposalPdf.FileName(input.Project));
        }
        catch (ProposalException ex)
        {
            Message = ex.Message;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Couldn't make the proposal for project {Number}", number);
            Message = "The proposal couldn't be made. The error has been logged.";
        }
        return RedirectToPage(new { number });
    }

    // Which quote panel to open again after the page reloads, so adding a price doesn't mean hunting for the panel.
    [TempData] public string? OpenQuote { get; set; }
    // Likewise the item card, which is collapsed by default once a project has more than one.
    [TempData] public string? OpenItem { get; set; }

    private IActionResult EditQuote(int number, Guid itemId, Guid supplierId, Func<(bool Ok, string Message)> change)
    {
        if (!CanEdit) return Forbid();
        Message = change().Message;
        OpenQuote = $"{itemId:N}-{supplierId:N}";
        OpenItem = $"{itemId:N}";
        return RedirectToPage(null, null, new { number }, $"quote-{OpenQuote}");
    }

    // Back to the item that was just changed, rather than the top of a long page.
    private IActionResult Edit(int number, Guid? itemId, Func<(bool Ok, string Message)> change)
    {
        if (!CanEdit) return Forbid();
        Message = change().Message;
        if (itemId is { } open) OpenItem = $"{open:N}";
        return RedirectToPage(null, null, new { number }, itemId is { } id ? $"item-{id:N}" : "items");
    }

    private IActionResult Done(int number, (bool Ok, string Message) result)
    {
        Message = result.Message;
        return RedirectToPage(new { number });
    }
}
