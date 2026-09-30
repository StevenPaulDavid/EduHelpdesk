using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

public class JobModel(HelpdeskStore store) : PageModel
{
    // The page opens at Tickets: View, and every POST on it changes the ticket - fifteen handlers and counting. One
    // guard here rather than fifteen identical lines means a handler added later is covered by default, which is the
    // right way round for a permission check. Delete and merge need more than Edit and check for themselves.
    public override void OnPageHandlerExecuting(Microsoft.AspNetCore.Mvc.Filters.PageHandlerExecutingContext context)
    {
        if (HttpMethods.IsPost(Request.Method) && !store.UserCan(User, Modules.Tickets, ModulePermission.Edit))
            context.Result = Forbid();
    }

    public TicketRecord? Ticket { get; private set; }
    public UserRecord? Requester { get; private set; }
    public TechnicianRecord? Technician { get; private set; }
    public string? TeamName => Ticket?.TeamName;
    public IReadOnlyList<AssetRecord> LinkedAssets { get; private set; } = [];
    // The picker leaves out disposed assets, but keeps any already linked to this ticket so an existing link can still
    // be seen and removed - the repair history of a scrapped device stays readable.
    public IReadOnlyList<AssetRecord> Assets => store.Assets
        .Where(x => !HelpdeskStore.IsDisposed(x) || (Ticket?.AssetIds.Contains(x.Id) ?? false)).ToList();
    public IReadOnlyList<TechnicianRecord> Technicians => store.Technicians;
    public IReadOnlyList<TechnicianRecord> TeamTechnicians => Ticket is null ? [] : store.GetTechniciansForTeam(Ticket.TeamName);
    public IReadOnlyList<string> TechnicianTeams => store.TechnicianTeams;
    public IReadOnlyList<string> Statuses => store.Statuses;
    public IReadOnlyDictionary<string, string> StatusDescriptions => store.StatusDescriptions;
    public IReadOnlyList<string> Priorities => store.Priorities;
    public IReadOnlyList<string> Categories => store.Categories;
    public IReadOnlyList<string> Locations => store.Locations;
    public bool RequiresCloseMessage => Ticket is not null && store.RequiresCloseMessage(Ticket);
    public IReadOnlyList<SlaDefinition> Slas => store.Slas;
    public IReadOnlyList<TicketAttributeDefinition> TicketAttributes => Ticket is null ? [] : store.GetTicketAttributes(Ticket.Category);
    public IReadOnlyDictionary<Guid, string> TicketAttributeValues => Ticket is null ? new Dictionary<Guid, string>() : store.GetTicketAttributeValues(Ticket.Number);
    public IReadOnlyList<(PartRecord Part, int Quantity)> TicketParts => Ticket is null ? [] : store.GetTicketParts(Ticket.Number);
    public IReadOnlyList<PartRecord> Parts => store.Parts;
    public IReadOnlyList<TicketRecord> MergeCandidates => Ticket is null ? [] : store.Tickets.Where(x => x.Number != Ticket.Number).OrderByDescending(x => x.Number).ToList();
    public IReadOnlyList<TicketAttachment> Attachments => Ticket is null ? [] : store.GetTicketAttachments(Ticket.Number);
    public IReadOnlyList<HelpdeskStore.TicketRelation> Relations => Ticket is null ? [] : store.GetTicketRelations(Ticket.Number);
    public string TemplateHtml { get; private set; } = string.Empty;
    [TempData] public string? Message { get; set; }

    // Overdue, due soon or paused, for the flag beside the due date.
    public DueState DueState => Ticket is null ? DueState.None : TicketInsights.DueStateOf(Ticket, DateTime.UtcNow, store.TicketDueSoonHours);
    public string DueText => Ticket is null ? "" : TicketInsights.DueText(Ticket, DateTime.UtcNow);
    public bool StatusPausesSla(string status) => store.PausesSla(status);
    // Back to the ticket list as it was last seen (filters, sort, page), or the plain list. Only ever a /Jobs address.
    public string BackUrl => Request.Cookies[JobsModel.BackCookie] is { } back
        && (back == "/Jobs" || back.StartsWith("/Jobs?", StringComparison.Ordinal)) && Url.IsLocalUrl(back) ? back : Url.Page("/Jobs")!;

    public bool CanEdit => store.UserCan(User, Modules.Tickets, ModulePermission.Edit);
    public bool CanDelete => store.UserCan(User, Modules.Tickets, ModulePermission.Delete);
    // The signed-in account, not the Jobs list's "Working as" choice: taking a ticket is something you do for yourself.
    public Guid? SignedInTechnicianId =>
        Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id) && store.Technicians.Any(x => x.Id == id) ? id : null;
    // Offered on an open ticket that is not already yours. A team mismatch still shows the button, and the handler
    // explains why it was refused - a missing button would leave people wondering where it went.
    public bool CanAssignToMe => CanEdit && Ticket is { Status: not "Closed" } ticket && SignedInTechnicianId is { } me && ticket.TechnicianId != me;

    public IActionResult OnGet(int number)
    {
        // An onboarding is worked from its own page: the checklist, with the ticket's notes and history below it.
        if (store.IsOnboardingTicket(number)) return RedirectToPage("/Onboarding/Details", new { number });
        Ticket = store.Tickets.FirstOrDefault(x => x.Number == number);
        if (Ticket is null) return NotFound();
        Requester = store.Users.FirstOrDefault(x => x.Id == Ticket.RequesterId);
        Technician = store.Technicians.FirstOrDefault(x => x.Id == Ticket.TechnicianId);
        LinkedAssets = store.Assets.Where(x => Ticket.AssetIds.Contains(x.Id)).ToList();
        TemplateHtml = store.RenderPrintTemplate(Ticket, Requester, Technician, LinkedAssets);
        return Page();
    }

    public IActionResult OnPostUpdate(int number, string field, string? value)
    {
        var ticket = store.Tickets.FirstOrDefault(x => x.Number == number);
        if (ticket is null) return NotFound();
        string? successMessage = null;
        if (field is "status" or "priority" or "category")
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                Message = "Select a value.";
                return RedirectToPage(new { number });
            }

            var allowed = field switch
            {
                "status" => store.Statuses,
                "priority" => store.Priorities,
                _ => store.Categories
            };
            if (!allowed.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                Message = "Select a valid value.";
                return RedirectToPage(new { number });
            }

            ticket = field switch
            {
                "status" => ticket with { Status = value, ClosedAt = value == "Closed" ? ticket.ClosedAt ?? DateTime.UtcNow : null },
                "priority" => store.WithPriority(ticket, value),
                _ => store.WithCategory(ticket, value)
            };
        }
        else if (field == "type")
        {
            var type = TicketTypes.All.FirstOrDefault(x => string.Equals(x, value?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (type is null)
            {
                Message = "Select a valid type.";
                return RedirectToPage(new { number });
            }
            ticket = ticket with { Type = type };
        }
        else if (field == "technician")
        {
            Guid? technicianId = string.IsNullOrWhiteSpace(value) ? null : Guid.TryParse(value, out var parsedTechnicianId) ? parsedTechnicianId : null;
            if (technicianId.HasValue && !store.Technicians.Any(x => x.Id == technicianId.Value))
            {
                Message = "Select a valid technician.";
                return RedirectToPage(new { number });
            }
            if (technicianId.HasValue && store.Technicians.FirstOrDefault(x => x.Id == technicianId.Value) is { } selectedTechnician && !HelpdeskStore.TechnicianInTeam(selectedTechnician, ticket.TeamName))
            {
                Message = $"Select a technician from the {ticket.TeamName} team.";
                return RedirectToPage(new { number });
            }
            ticket = ticket with { TechnicianId = technicianId };
        }
        else if (field == "team")
        {
            var team = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (team is not null && !store.TechnicianTeams.Contains(team, StringComparer.OrdinalIgnoreCase)) { Message = "Select a valid team."; return RedirectToPage(new { number }); }
            var keepTechnician = ticket.TechnicianId is not { } currentTechnicianId || store.Technicians.FirstOrDefault(x => x.Id == currentTechnicianId) is not { } currentTechnician || HelpdeskStore.TechnicianInTeam(currentTechnician, team);
            ticket = ticket with { TeamName = team, TechnicianId = keepTechnician ? ticket.TechnicianId : null };
            if (!keepTechnician) successMessage = "Team updated. The technician was unassigned because they are not in that team.";
        }
        else if (field == "sla")
        {
            Guid? slaId = string.IsNullOrWhiteSpace(value) ? null : Guid.TryParse(value, out var parsedSlaId) ? parsedSlaId : null;
            if (slaId.HasValue && !store.Slas.Any(x => x.Id == slaId.Value))
            {
                Message = "Select a valid SLA.";
                return RedirectToPage(new { number });
            }
            ticket = ticket with { SlaId = slaId, SlaOverridden = slaId.HasValue, DueDate = ticket.DueDateOverridden ? ticket.DueDate : store.CalculateDueDate(slaId, ticket.CreatedAt, ticket.SlaPauses), DueDateOverridden = ticket.DueDateOverridden };
        }

        else if (field == "location")
        {
            var location = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (location is not null && !store.Locations.Contains(location, StringComparer.OrdinalIgnoreCase)) { Message = "Select a valid location."; return RedirectToPage(new { number }); }
            ticket = ticket with { Location = location };
        }
        else if (field == "dueDate")
        {
            DateTime? dueDate = string.IsNullOrWhiteSpace(value) ? null : DateTime.TryParse(value, out var parsedDueDate) ? parsedDueDate.ToUniversalTime() : null;
            if (!string.IsNullOrWhiteSpace(value) && !dueDate.HasValue)
            {
                Message = "Enter a valid due date.";
                return RedirectToPage(new { number });
            }
            ticket = ticket with { DueDate = dueDate, DueDateOverridden = dueDate.HasValue };
        }
        else
        {
            Message = "Unknown job field.";
            return RedirectToPage(new { number });
        }
        store.UpdateTicket(ticket);
        Message = successMessage ?? "Job updated.";
        return RedirectToPage(new { number });
    }

    // One click instead of finding yourself in the technician dropdown. Same team rule as choosing from the list.
    public IActionResult OnPostAssignToMe(int number)
    {
        var ticket = store.Tickets.FirstOrDefault(x => x.Number == number);
        if (ticket is null) return NotFound();
        if (SignedInTechnicianId is not { } me || store.Technicians.FirstOrDefault(x => x.Id == me) is not { } technician)
        {
            Message = "Your account isn't a technician account, so tickets can't be assigned to it.";
            return RedirectToPage(new { number });
        }
        if (ticket.TechnicianId == me)
        {
            Message = "This ticket is already assigned to you.";
            return RedirectToPage(new { number });
        }
        if (!HelpdeskStore.TechnicianInTeam(technician, ticket.TeamName))
        {
            Message = $"This ticket belongs to the {ticket.TeamName} team, which you're not in. Change the team first if you're taking it on.";
            return RedirectToPage(new { number });
        }
        store.UpdateTicket(ticket with { TechnicianId = me });
        Message = "Assigned to you.";
        return RedirectToPage(new { number });
    }

    public IActionResult OnPostUpdateAssets(int number, Guid[]? assetIds)
    {
        var ticket = store.Tickets.FirstOrDefault(x => x.Number == number);
        if (ticket is null) return NotFound();
        var validAssetIds = (assetIds ?? []).Where(id => store.Assets.Any(x => x.Id == id)).Distinct().ToList();
        store.UpdateTicket(ticket with { AssetIds = validAssetIds });
        Message = "Linked assets updated.";
        return RedirectToPage(new { number });
    }

    public IActionResult OnPostAssignPart(int number, Guid partId, int quantity)
    {
        if (partId == Guid.Empty || quantity <= 0)
        {
            Message = "Select a part and a quantity to assign.";
            return RedirectToPage(new { number });
        }
        Message = store.SetTicketPartQuantity(number, partId, quantity) ?? "Part assigned.";
        return RedirectToPage(new { number });
    }

    public IActionResult OnPostUpdatePartQuantity(int number, Guid partId, int quantity)
    {
        Message = store.SetTicketPartQuantity(number, partId, quantity) ?? (quantity <= 0 ? "Part removed." : "Part quantity updated.");
        return RedirectToPage(new { number });
    }

    public IActionResult OnPostUpdateAttributes(int number, Dictionary<Guid, string>? customAttributes)
    {
        var ticket = store.Tickets.FirstOrDefault(x => x.Number == number);
        if (ticket is null) return NotFound();
        store.UpdateTicketAttributeValues(number, ticket.Category, customAttributes);
        Message = "Custom attributes updated.";
        return RedirectToPage(new { number });
    }

    public IActionResult OnPostUpdateContent(int number, string? title, string? description)
    {
        var ticket = store.Tickets.FirstOrDefault(x => x.Number == number);
        if (ticket is null) return NotFound();
        if (string.IsNullOrWhiteSpace(title))
        {
            Message = "Enter a ticket title.";
            return RedirectToPage(new { number });
        }

        store.UpdateTicket(ticket with
        {
            Title = title.Trim(),
            Description = (description ?? string.Empty).Trim()
        });
        Message = "Ticket details updated.";
        return RedirectToPage(new { number });
    }

    public IActionResult OnPostAddComment(int number, string? comment, string? status, bool internalNote)
    {
        var label = internalNote ? "Internal note" : "Comment";
        if (string.IsNullOrWhiteSpace(comment))
        {
            Message = internalNote ? "Enter a note before saving." : "Enter a comment before saving.";
            return RedirectToPage(new { number });
        }

        string? newStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            newStatus = store.Statuses.FirstOrDefault(x => string.Equals(x, status.Trim(), StringComparison.OrdinalIgnoreCase));
            if (newStatus is null)
            {
                Message = "Select a valid status.";
                return RedirectToPage(new { number });
            }
        }

        if (!store.AddTicketComment(number, comment, internalNote))
        {
            Message = "Ticket was not found.";
            return RedirectToPage(new { number });
        }

        var ticket = store.Tickets.FirstOrDefault(x => x.Number == number);
        if (ticket is null) return NotFound();

        if (newStatus is null || string.Equals(newStatus, ticket.Status, StringComparison.OrdinalIgnoreCase))
        {
            Message = $"{label} added.";
        }
        else
        {
            var wasClosed = ticket.Status == "Closed";
            store.UpdateTicket(ticket with { Status = newStatus, ClosedAt = newStatus == "Closed" ? ticket.ClosedAt ?? DateTime.UtcNow : null });
            Message = newStatus == "Closed" ? $"{label} added and ticket closed."
                : wasClosed ? $"{label} added and ticket reopened."
                : $"{label} added and status changed to {newStatus}.";
        }

        return RedirectToPage(new { number });
    }

    public bool CanRemoveComment(TicketComment comment) => store.CanRemoveComment(User, Modules.Tickets, comment.By);

    public IActionResult OnPostRemoveComment(int number, long comment)
    {
        if (store.FindTicketComment(number, comment) is not { } found)
        {
            Message = "That comment has already been removed.";
            return RedirectToPage(new { number });
        }
        if (!CanRemoveComment(found)) return Forbid();
        Message = store.RemoveTicketComment(number, comment).Message;
        return RedirectToPage(new { number });
    }

    // Uploads one or more files. Each is checked on its own, so one bad file does not stop the others.
    public IActionResult OnPostUploadAttachments(int number, List<IFormFile>? files, bool shareWithRequester)
    {
        if (store.Tickets.All(x => x.Number != number)) return NotFound();
        var chosen = (files ?? []).Where(x => x.Length > 0 || !string.IsNullOrEmpty(x.FileName)).ToList();
        if (chosen.Count == 0)
        {
            Message = "Choose a file to attach.";
            return RedirectToPage(new { number });
        }
        var added = 0;
        var problems = new List<string>();
        foreach (var file in chosen.Take(HelpdeskStore.MaxAttachmentsPerUpload))
        {
            using var stream = file.OpenReadStream();
            var error = store.AddTicketAttachment(number, file.FileName, stream, file.Length, visibleToRequester: shareWithRequester);
            if (error is null) added++; else problems.Add(error);
        }
        if (problems.Any(x => x.Contains("not accepted") || x.Contains("no file type")))
            problems.Add($"Accepted types: {HelpdeskStore.AllowedAttachmentExtensions}.");
        if (chosen.Count > HelpdeskStore.MaxAttachmentsPerUpload)
            problems.Add($"Only {HelpdeskStore.MaxAttachmentsPerUpload} files can be attached at a time, so {chosen.Count - HelpdeskStore.MaxAttachmentsPerUpload} {(chosen.Count - HelpdeskStore.MaxAttachmentsPerUpload == 1 ? "was" : "were")} skipped.");
        Message = string.Join(" ", new[] { added > 0 ? $"{added} file{(added == 1 ? "" : "s")} attached." : null }.Concat(problems).Where(x => x is not null));
        return RedirectToPage(new { number });
    }

    public IActionResult OnPostShareAttachment(int number, Guid id, bool visible)
    {
        Message = store.SetAttachmentVisibleToRequester(number, id, visible)
            ?? (visible ? "The requester can now see that file in the staff portal." : "That file is no longer shown to the requester.");
        return RedirectToPage(new { number });
    }

    public IActionResult OnPostRemoveAttachment(int number, Guid id)
    {
        Message = store.RemoveTicketAttachment(number, id) ?? "Attachment removed.";
        return RedirectToPage(new { number });
    }

    // Pictures are shown in the page (inline); everything else is downloaded. Either way the browser is told not to guess the type
    // and not to run anything in it.
    public IActionResult OnGetAttachment(int number, Guid id, bool inline)
    {
        if (store.FindAttachment(number, id) is not { } found) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; img-src 'self' data:; style-src 'unsafe-inline'; sandbox";
        if (inline && HelpdeskStore.IsInlineImage(found.Attachment))
            return PhysicalFile(found.Path, found.Attachment.ContentType);
        return PhysicalFile(found.Path, found.Attachment.ContentType, found.Attachment.FileName);
    }

    public IActionResult OnPostLinkRelated(int number, int? other)
    {
        Message = other is null ? "Enter the number of the ticket to link." : store.LinkRelatedTickets(number, other.Value) ?? $"Linked to #{other}.";
        return RedirectToPage(new { number });
    }

    public IActionResult OnPostUnlinkTicket(int number, int other)
    {
        Message = store.UnlinkTickets(number, other) ?? $"Link to #{other} removed.";
        return RedirectToPage(new { number });
    }

    // Purchasing projects linked to this ticket. The block shows for anyone who can reach the projects list; linking needs
    // Projects: Edit and starting one Projects: New, on top of the Tickets: Edit every post on this page already needs.
    public bool CanSeeProjects => store.UserCan(User, Modules.Projects, ModulePermission.Access);
    public bool CanOpenProjects => store.UserCan(User, Modules.Projects, ModulePermission.View);
    public bool CanLinkProjects => store.UserCan(User, Modules.Projects, ModulePermission.Edit);
    public bool CanStartProject => store.UserCan(User, Modules.Projects, ModulePermission.New);
    public IReadOnlyList<ProjectRecord> LinkedProjects => Ticket is null || !CanSeeProjects ? [] : store.ProjectsForTicket(Ticket.Number);
    // Open projects not already linked, for the picker. A closed one can still be linked from its own page by ticket number.
    public IReadOnlyList<ProjectRecord> LinkableProjects => Ticket is null ? [] : store.Projects.Where(x => x.IsActive && !x.TicketNumbers.Contains(Ticket.Number)).ToList();
    public DateOnly DefaultProjectDueDate => DateOnly.FromDateTime(DateTime.Now).AddDays(HelpdeskStore.DefaultProjectLeadDays);

    public IActionResult OnPostLinkProject(int number, int? project)
    {
        if (!CanLinkProjects) return Forbid();
        Message = project is null ? "Choose the project to link." : store.LinkProjectTicket(project.Value, number).Message;
        return RedirectToPage(new { number });
    }

    public IActionResult OnPostUnlinkProject(int number, int project)
    {
        if (!CanLinkProjects) return Forbid();
        Message = store.UnlinkProjectTicket(project, number).Message;
        return RedirectToPage(new { number });
    }

    public IActionResult OnPostStartProject(int number, string? title, DateOnly? dueDate, string? itemsWanted, int priority)
    {
        if (!CanStartProject) return Forbid();
        var (ok, message, projectNumber) = store.StartProjectFromTicket(number, title, dueDate, itemsWanted, priority);
        Message = message;
        // Straight to the new project if it can be opened; otherwise back to the ticket, which now lists it.
        return ok && CanOpenProjects ? RedirectToPage("/Project", new { number = projectNumber }) : RedirectToPage(new { number });
    }

    public IActionResult OnPostCreateFollowUp(int number)
    {
        var (followUp, error) = store.CreateFollowUpTicket(number);
        if (followUp is null)
        {
            Message = error;
            return RedirectToPage(new { number });
        }
        Message = $"Follow-up ticket #{followUp} created from #{number}. Edit its details below.";
        return RedirectToPage(new { number = followUp.Value });
    }

    public IActionResult OnPostClose(int number, string? closingMessage)
    {
        var ticket = store.Tickets.FirstOrDefault(x => x.Number == number);
        if (ticket is null) return NotFound();

        if (ticket.Status == "Closed")
        {
            Message = "Job is already closed.";
            return RedirectToPage(new { number });
        }

        if (store.RequiresCloseMessage(ticket) && string.IsNullOrWhiteSpace(closingMessage))
        {
            Message = $"A closing message is required for {ticket.Priority} priority / {ticket.Category} tickets before they can be closed.";
            return RedirectToPage(new { number });
        }

        if (!string.IsNullOrWhiteSpace(closingMessage))
            store.AddTicketComment(number, closingMessage);

        var current = store.Tickets.FirstOrDefault(x => x.Number == number) ?? ticket;
        store.UpdateTicket(current with
        {
            Status = "Closed",
            ClosedAt = current.ClosedAt ?? DateTime.UtcNow
        });
        Message = "Job closed.";
        return RedirectToPage(new { number });
    }

    public IActionResult OnPostDelete(int number)
    {
        if (!store.UserCan(User, Modules.Tickets, ModulePermission.Delete)) return Forbid();
        var message = store.DeleteTicket(number);
        if (message is not null)
        {
            Message = message;
            return RedirectToPage(new { number });
        }
        Message = $"Ticket #{number} deleted.";
        return RedirectToPage("/Jobs");
    }

    public IActionResult OnPostMerge(int number, int targetNumber)
    {
        if (!store.UserCan(User, Modules.Tickets, ModulePermission.Delete)) return Forbid();
        var message = store.MergeTicket(number, targetNumber);
        if (message is not null)
        {
            Message = message;
            return RedirectToPage(new { number });
        }
        Message = $"Ticket #{number} was merged into ticket #{targetNumber}.";
        return RedirectToPage(new { number = targetNumber });
    }
}
