using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Portal;

public class TicketModel(HelpdeskStore store, PortalIdentity portal) : PageModel
{
    public TicketRecord? Ticket { get; private set; }
    public IReadOnlyList<TicketComment> Comments => Ticket is null ? [] : Ticket.Comments.Where(x => !x.IsInternal).OrderBy(x => x.CreatedAt).ToList();
    // Files the requester sent, and any a technician chose to share. Never the rest.
    public IReadOnlyList<TicketAttachment> Attachments => Ticket is null ? [] : store.GetRequesterAttachments(Ticket.Number);
    // Drives the note under the message box warning that replying will reopen the ticket.
    public bool IsClosed => Ticket is not null && TicketInsights.IsClosed(Ticket);
    // Closed and past the reopen window: no message box, just "report it again".
    public bool CanReply => Ticket is not null && store.RequesterCanReply(Ticket);
    public DateTime? ReplyWindowEnds => Ticket is null ? null : store.ReplyWindowEnds(Ticket);
    [BindProperty] public string Comment { get; set; } = "";
    [TempData] public string? Message { get; set; }

    // Returns NotFound for both "doesn't exist" and "not yours" - a portal visitor can't tell a stranger's ticket
    // number from a made-up one.
    public IActionResult OnGet(int number)
    {
        var id = portal.Resolve(Request, store);
        if (id is null) return RedirectToPage("/Portal/Index");
        // Their own tickets only, and never an onboarding - those are internal (HelpdeskStore.PortalTicket).
        Ticket = store.PortalTicket(id.Value, number);
        if (Ticket is null) return NotFound();
        // Clears the "New reply" flag on My tickets. Opened by the requester themselves, not a technician checking up.
        store.MarkSeenByRequester(number);
        return Page();
    }

    public IActionResult OnPostComment(int number, List<IFormFile>? files)
    {
        var id = portal.Resolve(Request, store);
        if (id is null) return RedirectToPage("/Portal/Index");
        var ticket = store.PortalTicket(id.Value, number);
        if (ticket is null) return NotFound();
        if (!store.RequesterCanReply(ticket))
        {
            Message = "This ticket was closed a while ago, so it can't take new messages. If the problem is back, report it again below.";
            return RedirectToPage(new { number });
        }
        if (string.IsNullOrWhiteSpace(Comment))
        {
            Message = "Enter a message before sending.";
            return RedirectToPage(new { number });
        }
        if (Comment.Trim().Length > HelpdeskStore.MaxTicketTextLength)
        {
            Message = $"Keep messages under {HelpdeskStore.MaxTicketTextLength} characters - send it in two parts if you need to.";
            return RedirectToPage(new { number });
        }
        var chosen = PortalFiles.Chosen(files);
        if (PortalFiles.TooMany(chosen) is { } tooMany)
        {
            Message = tooMany;
            return RedirectToPage(new { number });
        }
        var (ok, reopened) = store.AddRequesterComment(number, Comment);
        if (!ok) return NotFound();
        var problems = PortalFiles.Attach(store, number, chosen);
        Message = (reopened
            ? "Sent - and because the ticket had been closed, it has been reopened so the team pick it up again."
            : "Sent.") + (problems.Length > 0 ? $" {problems}" : "");
        return RedirectToPage(new { number });
    }

    // The requester's own files and those shared with them, served as the helpdesk serves attachments: the browser is
    // told not to guess the type and not to run anything, and only pictures are shown in the page.
    public IActionResult OnGetAttachment(int number, Guid id, bool inline)
    {
        var requester = portal.Resolve(Request, store);
        if (requester is null) return RedirectToPage("/Portal/Index");
        if (store.PortalTicket(requester.Value, number) is null) return NotFound();
        if (store.FindAttachment(number, id) is not { } found || !found.Attachment.VisibleToRequester) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; img-src 'self' data:; style-src 'unsafe-inline'; sandbox";
        if (inline && HelpdeskStore.IsInlineImage(found.Attachment))
            return PhysicalFile(found.Path, found.Attachment.ContentType);
        return PhysicalFile(found.Path, found.Attachment.ContentType, found.Attachment.FileName);
    }
}

// Files sent from the portal, shared by a new ticket and a reply. Each one is checked on its own, so one bad file
// doesn't lose the message or the others.
public static class PortalFiles
{
    public static List<IFormFile> Chosen(List<IFormFile>? files) =>
        (files ?? []).Where(x => x.Length > 0 || !string.IsNullOrEmpty(x.FileName)).ToList();

    public static string? TooMany(List<IFormFile> chosen) => chosen.Count > HelpdeskStore.MaxPortalAttachments
        ? $"You can attach up to {HelpdeskStore.MaxPortalAttachments} files at a time - choose fewer and send again."
        : null;

    // What went wrong, in a sentence or two, or "" if every file went on.
    public static string Attach(HelpdeskStore store, int number, List<IFormFile> chosen)
    {
        var problems = new List<string>();
        foreach (var file in chosen)
        {
            using var stream = file.OpenReadStream();
            if (store.AddTicketAttachment(number, file.FileName, stream, file.Length, fromRequester: true) is { } error) problems.Add(error);
        }
        if (problems.Any(x => x.Contains("not accepted") || x.Contains("no file type")))
            problems.Add("Pictures, PDFs, Office documents and text files can be attached.");
        return string.Join(" ", problems);
    }
}
