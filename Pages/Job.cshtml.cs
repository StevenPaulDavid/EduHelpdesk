using System.Security.Claims;
using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

public class JobModel(HelpdeskStore store, EduNotifyClient eduNotify, ILogger<JobModel> logger) : PageModel
{
    // The page opens at Tickets: View, and every POST on it changes the ticket - fifteen handlers and counting. One
    // guard here rather than fifteen identical lines means a handler added later is covered by default, which is the
    // right way round for a permission check. Delete and merge need more than Edit and check for themselves.
    public override void OnPageHandlerExecuting(Microsoft.AspNetCore.Mvc.Filters.PageHandlerExecutingContext context)
    {
        // Another desk's ticket is not there, for anyone not on that desk (HelpdeskStore.Desks) - opening, printing,
        // its files and every change alike. Not found rather than refused, so its existence isn't given away either.
        if (context.HandlerArguments.TryGetValue("number", out var value) && value is int asked && !store.CanSeeTicket(User, asked)
            && store.Tickets.Any(x => x.Number == asked))
        {
            context.Result = NotFound();
            return;
        }
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
    // Everything offered on the ticket belongs to its desk (HelpdeskStore.Desks): its teams, its people, its statuses
    // and its categories. Another desk's categories are offered separately, as moving the ticket.
    public Desk? Desk => Ticket is null ? null : store.DeskOf(Ticket);
    public string Reference => Ticket is null ? "" : HelpdeskStore.RefFor(Desk, Ticket);
    public bool HasSeveralDesks => store.HasSeveralDesks;
    public IReadOnlyList<TechnicianRecord> TeamTechnicians => Ticket is null || Desk is null ? [] : store.GetTechniciansForTeam(Ticket.TeamName).Where(x => HelpdeskStore.TechnicianOnDesk(x, Desk.Id)).ToList();
    public IReadOnlyList<string> TechnicianTeams => Desk is null ? store.TechnicianTeams : store.TeamsFor(Desk.Id);
    public IReadOnlyList<string> Statuses => Desk is null ? store.Statuses : store.StatusesFor(Desk.Id);
    public IReadOnlyDictionary<string, string> StatusDescriptions => store.StatusDescriptions;
    public IReadOnlyList<string> Priorities => store.Priorities;
    public IReadOnlyList<string> Categories => Desk is null ? store.Categories : store.CategoriesFor(Desk.Id);
    public IReadOnlyList<(Desk Desk, IReadOnlyList<string> Categories)> OtherDesks => Desk is null ? []
        : store.Desks.Where(x => x.Id != Desk.Id).Select(x => (x, store.CategoriesFor(x.Id))).Where(x => x.Item2.Count > 0).ToList();
    public IReadOnlyList<string> Locations => store.Locations;
    public bool RequiresCloseMessage => Ticket is not null && store.RequiresCloseMessage(Ticket);
    public IReadOnlyList<SlaDefinition> Slas => store.Slas;
    public IReadOnlyList<TicketAttributeDefinition> TicketAttributes => Ticket is null ? [] : store.GetTicketAttributes(Ticket.Category);
    public IReadOnlyDictionary<Guid, string> TicketAttributeValues => Ticket is null ? new Dictionary<Guid, string>() : store.GetTicketAttributeValues(Ticket.Number);
    public IReadOnlyList<(PartRecord Part, int Quantity)> TicketParts => Ticket is null ? [] : store.GetTicketParts(Ticket.Number);
    public IReadOnlyList<PartRecord> Parts => store.Parts;
    // What the job cost (HelpdeskStore.Costs), for roles with "See and add job costs".
    public bool CanSeeCosts => store.UserHasFlag(User, Modules.Flags.Costs);
    // The parts of a ticket that belong to a feature the school can switch off (Services/Features.cs).
    public bool ShowAssets => store.IsFeatureOn(Features.Assets);
    public bool ShowParts => store.IsFeatureOn(Features.Parts);
    public IReadOnlyList<TicketCost> Costs => Ticket is null || !CanSeeCosts ? [] : store.CostsFor(Ticket.Number);
    public IReadOnlyList<SupplierRecord> Suppliers => store.Suppliers;
    public IReadOnlyList<string> BudgetCodes => store.BudgetCodes;
    public IReadOnlyList<TicketRecord> MergeCandidates => Ticket is null ? [] : store.TicketsFor(User).Where(x => x.Number != Ticket.Number).OrderByDescending(x => x.Number).ToList();
    public IReadOnlyList<TicketAttachment> Attachments => Ticket is null ? [] : store.GetTicketAttachments(Ticket.Number);
    // A link to another desk's ticket stays, but only those on that desk see it.
    public IReadOnlyList<HelpdeskStore.TicketRelation> Relations => Ticket is null ? [] : store.GetTicketRelations(Ticket.Number).Where(x => store.CanSeeTicket(User, x.Number)).ToList();
    public string TemplateHtml { get; private set; } = string.Empty;
    [TempData] public string? Message { get; set; }

    // Overdue, due soon or paused, for the flag beside the due date.
    // The requester's name links to their page for whoever can open it.
    public bool CanOpenRequester => store.UserCan(User, Modules.Requesters, ModulePermission.View);
    public DueState DueState => Ticket is null ? DueState.None : TicketInsights.DueStateOf(Ticket, DateTime.UtcNow, store.TicketDueSoonHours);
    public string DueText => Ticket is null ? "" : TicketInsights.DueText(Ticket, DateTime.UtcNow);
    // Back to the ticket list as it was last seen (filters, sort, page), or the plain list. Only ever a /Jobs address.
    public string BackUrl => Request.Cookies[JobsModel.BackCookie] is { } back
        && (back == "/Jobs" || back.StartsWith("/Jobs?", StringComparison.Ordinal)) && Url.IsLocalUrl(back) ? back : Url.Page("/Jobs")!;

    public bool CanEdit => store.UserCan(User, Modules.Tickets, ModulePermission.Edit);
    public bool CanDelete => store.UserCan(User, Modules.Tickets, ModulePermission.Delete);
    // The signed-in account: taking a ticket is something you do for yourself.
    public Guid? SignedInTechnicianId =>
        Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id) && store.Technicians.Any(x => x.Id == id) ? id : null;
    // Offered on an open ticket that is not already yours. A team mismatch still shows the button, and the handler
    // explains why it was refused - a missing button would leave people wondering where it went.
    public bool CanAssignToMe => CanEdit && Ticket is { Status: not "Closed" } ticket && SignedInTechnicianId is { } me && ticket.TechnicianId != me;
    // On an open ticket whose requester can see it in the staff portal, even one already yours: "assigned" earlier and
    // "walking over now" are different moments.
    public bool CanSayOnMyWay => CanEdit && Ticket is { Status: not "Closed" } ticket && SignedInTechnicianId is not null && store.PortalTicket(ticket.RequesterId, ticket.Number) is not null;

    public IActionResult OnGet(int number)
    {
        // An onboarding is worked from its own page: the checklist, with the ticket's notes and history below it.
        // With onboarding switched off (Services/Features.cs) it is an ordinary ticket.
        if (store.IsFeatureOn(Features.Onboarding) && store.IsOnboardingTicket(number)) return RedirectToPage("/Onboarding/Details", new { number });
        Ticket = store.Tickets.FirstOrDefault(x => x.Number == number);
        if (Ticket is null) return NotFound();
        // Whatever the bell was holding about this ticket has now been seen, however the technician got here.
        if (Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var viewerId)) store.MarkTicketNotificationsRead(viewerId, number);
        Requester = store.Users.FirstOrDefault(x => x.Id == Ticket.RequesterId);
        Technician = store.Technicians.FirstOrDefault(x => x.Id == Ticket.TechnicianId);
        LinkedAssets = store.Assets.Where(x => Ticket.AssetIds.Contains(x.Id)).ToList();
        TemplateHtml = store.RenderPrintTemplate(Ticket, Requester, Technician, LinkedAssets);
        return Page();
    }

    // The whole ticket as a PDF to print or keep: every field, the conversation, the history and the attachments merged
    // in (Services/TicketReportPdf). Opened in the browser rather than downloaded, so it can go straight to the printer.
    // internalNotes=false is the copy that can be handed to the requester or someone outside the IT team: no internal
    // notes, no change history, and no files kept for the IT team.
    public IActionResult OnGetReport(int number, bool internalNotes = true)
    {
        var me = SignedInTechnicianId is { } id ? store.Technicians.FirstOrDefault(x => x.Id == id)?.Name : null;
        if (store.TicketReportFor(number, internalNotes, me) is not { } input) return NotFound();
        try
        {
            var pdf = TicketReportPdf.Build(input);
            // The page policy's object-src 'none' stops Chrome showing a PDF in the tab; this response is no page.
            Response.Headers.ContentSecurityPolicy = "frame-ancestors 'none'";
            Response.Headers.ContentDisposition = $"inline; filename=\"{TicketReportPdf.FileName(input.Ticket)}\"";
            return File(pdf, "application/pdf");
        }
        catch (ProposalException ex)
        {
            Message = ex.Message;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Couldn't make the report for ticket {Number}", number);
            Message = "The report couldn't be made. The error has been logged.";
        }
        return RedirectToPage(new { number });
    }

    public IActionResult OnPostUpdate(int number, string field, string? value)
    {
        var ticket = store.Tickets.FirstOrDefault(x => x.Number == number);
        if (ticket is null) return NotFound();
        string? successMessage = null;
        // A category on another desk moves the ticket there, and the store says what came off it on the way.
        if (field == "category")
        {
            var (_, moved) = store.ChangeTicketCategory(number, value);
            Message = moved;
            return RedirectToPage(new { number });
        }
        var desk = store.DeskOf(ticket);
        if (field is "status" or "priority")
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                Message = "Select a value.";
                return RedirectToPage(new { number });
            }

            var allowed = field switch
            {
                "status" => store.StatusesFor(desk.Id),
                _ => store.Priorities
            };
            if (!allowed.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                Message = "Select a valid value.";
                return RedirectToPage(new { number });
            }

            if (field == "status" && HelpdeskStore.IsBuiltInStatus(value) && store.CloseBlockedByChecklist(number) is { } blocked)
            {
                Message = blocked;
                return RedirectToPage(new { number });
            }
            ticket = field switch
            {
                "status" => ticket with { Status = value, ClosedAt = value == "Closed" ? ticket.ClosedAt ?? DateTime.UtcNow : null },
                _ => store.WithPriority(ticket, value)
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
            if (technicianId.HasValue && store.Technicians.FirstOrDefault(x => x.Id == technicianId.Value) is { } onDesk && !HelpdeskStore.TechnicianOnDesk(onDesk, desk.Id))
            {
                Message = $"{onDesk.Name} isn't on the {desk.Name} desk. Add them to it on their technician account, or move the ticket first.";
                return RedirectToPage(new { number });
            }
            ticket = ticket with { TechnicianId = technicianId };
        }
        else if (field == "team")
        {
            var team = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (team is not null && !store.TechnicianTeams.Contains(team, StringComparer.OrdinalIgnoreCase)) { Message = "Select a valid team."; return RedirectToPage(new { number }); }
            if (team is not null && store.DeskForTeam(team) is { } teamDesk && teamDesk.Id != desk.Id) { Message = $"The {team} team is on the {teamDesk.Name} desk. Move the ticket there first."; return RedirectToPage(new { number }); }
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
            ticket = ticket with { SlaId = slaId, SlaOverridden = slaId.HasValue, DueDate = ticket.DueDateOverridden ? ticket.DueDate : store.CalculateDueDate(slaId, ticket.CreatedAt, ticket.SlaPauses, ticket.Category), DueDateOverridden = ticket.DueDateOverridden };
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
            Message = "Unknown ticket field.";
            return RedirectToPage(new { number });
        }
        store.UpdateTicket(ticket);
        Message = successMessage ?? "Ticket updated.";
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
        }        if (store.DeskOf(ticket) is var ticketDesk && !HelpdeskStore.TechnicianOnDesk(technician, ticketDesk.Id))
        {
            Message = $"This ticket is on the {ticketDesk.Name} desk, which you're not on. Move it to your desk first if you're taking it on.";
            return RedirectToPage(new { number });
        }
        store.UpdateTicket(ticket with { TechnicianId = me });
        Message = "Assigned to you.";
        return RedirectToPage(new { number });
    }

    // Takes the ticket, as Assign to me does, and tells the requester who is coming: a pop-up in their staff portal and a
    // comment on the ticket, which is where clicking the pop-up lands and which keeps a record of when it was said.
    public IActionResult OnPostOnMyWay(int number)
    {
        var ticket = store.Tickets.FirstOrDefault(x => x.Number == number);
        if (ticket is null) return NotFound();
        if (SignedInTechnicianId is not { } me || store.Technicians.FirstOrDefault(x => x.Id == me) is not { } technician)
        {
            Message = "Your account isn't a technician account, so tickets can't be assigned to it.";
            return RedirectToPage(new { number });
        }
        if (ticket.Status == "Closed")
        {
            Message = "This ticket is closed. Reopen it first if you're going back to it.";
            return RedirectToPage(new { number });
        }
        if (store.PortalTicket(ticket.RequesterId, number) is null)
        {
            Message = "The requester can't see this ticket in the staff portal, so they can't be told you're on your way.";
            return RedirectToPage(new { number });
        }
        if (!HelpdeskStore.TechnicianInTeam(technician, ticket.TeamName))
        {
            Message = $"This ticket belongs to the {ticket.TeamName} team, which you're not in. Change the team first if you're taking it on.";
            return RedirectToPage(new { number });
        }        if (store.DeskOf(ticket) is var ticketDesk && !HelpdeskStore.TechnicianOnDesk(technician, ticketDesk.Id))
        {
            Message = $"This ticket is on the {ticketDesk.Name} desk, which you're not on. Move it to your desk first if you're taking it on.";
            return RedirectToPage(new { number });
        }
        var assigned = ticket.TechnicianId != me;
        if (assigned) store.UpdateTicket(ticket with { TechnicianId = me });
        store.AddTicketComment(number, $"{technician.Name} is on their way.");
        var notified = store.AddNotification(HelpdeskStore.PortalAudience, ticket.RequesterId, HelpdeskStore.NotificationKinds.OnMyWay, number, me) != 0;
        Message = (assigned ? "Assigned to you. " : "")
            + (notified ? "The requester has been told you're on your way." : "A comment says you're on your way, but requester notifications are switched off in Settings, so no pop-up was sent.");
        return RedirectToPage(new { number });
    }

    public IActionResult OnPostUpdateAssets(int number, Guid[]? assetIds)
    {
        if (!ShowAssets) return NotFound();
        var ticket = store.Tickets.FirstOrDefault(x => x.Number == number);
        if (ticket is null) return NotFound();
        var validAssetIds = (assetIds ?? []).Where(id => store.Assets.Any(x => x.Id == id)).Distinct().ToList();
        store.UpdateTicket(ticket with { AssetIds = validAssetIds });
        Message = "Linked assets updated.";
        return RedirectToPage(null, null, new { number }, "assets-tab");
    }

    public IActionResult OnPostAssignPart(int number, Guid partId, int quantity)
    {
        if (!ShowParts) return NotFound();
        if (partId == Guid.Empty || quantity <= 0)
        {
            Message = "Select a part and a quantity to assign.";
            return RedirectToPage(null, null, new { number }, "parts-tab");
        }
        Message = store.SetTicketPartQuantity(number, partId, quantity) ?? "Part assigned.";
        return RedirectToPage(null, null, new { number }, "parts-tab");
    }

    public IActionResult OnPostUpdatePartQuantity(int number, Guid partId, int quantity)
    {
        if (!ShowParts) return NotFound();
        Message = store.SetTicketPartQuantity(number, partId, quantity) ?? (quantity <= 0 ? "Part removed." : "Part quantity updated.");
        return RedirectToPage(null, null, new { number }, "parts-tab");
    }

    // The premises checklist on this ticket (HelpdeskStore.Checks), if it came from a recurring ticket that has one.
    // None while premises checks are switched off.
    public IReadOnlyList<CheckLine> Checklist => Ticket is null || !store.IsFeatureOn(Features.Checks) ? [] : store.ChecklistFor(Ticket.Number);

    // Every line's boxes are result-<id>, reading-<id> and note-<id>, so the form is read here rather than bound.
    public IActionResult OnPostSaveChecklist(int number)
    {
        if (!store.IsFeatureOn(Features.Checks)) return NotFound();
        var answers = store.ChecklistFor(number).ToDictionary(x => x.Id, x => new HelpdeskStore.CheckAnswer(
            Request.Form[$"result-{x.Id}"].FirstOrDefault(), Request.Form[$"reading-{x.Id}"].FirstOrDefault(), Request.Form[$"note-{x.Id}"].FirstOrDefault()));
        Message = store.SaveChecklist(number, answers).Message;
        return RedirectToPage(null, null, new { number }, "checklist");
    }

    public IActionResult OnPostAddCost(int number, DateOnly? date, string? description, Guid? supplierId, string? net, string? vat, string? budgetCode)
    {
        if (!CanSeeCosts) return Forbid();
        Message = store.AddTicketCost(number, new HelpdeskStore.CostInput(date, description, supplierId, net, vat, budgetCode)).Message;
        return RedirectToPage(null, null, new { number }, "costs-tab");
    }

    public IActionResult OnPostRemoveCost(int number, Guid id)
    {
        if (!CanSeeCosts) return Forbid();
        Message = store.RemoveTicketCost(number, id).Message;
        return RedirectToPage(null, null, new { number }, "costs-tab");
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

    public IActionResult OnPostAddComment(int number, string? comment, string? status, bool internalNote, bool notifyRequester)
    {
        // "Reply" as the page's own switch calls it: a comment the requester can read.
        var label = internalNote ? "Internal note" : "Reply";
        if (string.IsNullOrWhiteSpace(comment))
        {
            Message = internalNote ? "Enter a note before saving." : "Enter a reply before saving.";
            return RedirectToPage(new { number });
        }

        string? newStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            newStatus = store.Tickets.FirstOrDefault(x => x.Number == number) is { } current
                ? store.StatusesFor(store.DeskOf(current).Id).FirstOrDefault(x => string.Equals(x, status.Trim(), StringComparison.OrdinalIgnoreCase))
                : null;
            if (newStatus is null)
            {
                Message = "Select a valid status.";
                return RedirectToPage(new { number });
            }
        }

        // Closing with a reply still needs the checklist done; the reply isn't saved, so nothing is half done.
        if (newStatus is not null && HelpdeskStore.IsBuiltInStatus(newStatus) && store.CloseBlockedByChecklist(number) is { } blocked)
        {
            Message = blocked;
            return RedirectToPage(new { number });
        }
        if (!store.AddTicketComment(number, comment, internalNote))
        {
            Message = "Ticket was not found.";
            return RedirectToPage(new { number });
        }

        var ticket = store.Tickets.FirstOrDefault(x => x.Number == number);
        if (ticket is null) return NotFound();

        // Only a comment the requester can read can tell them anything, and only on a ticket they can see in the portal
        // (an onboarding's is internal). The box is left off the form for a note, but the server decides regardless.
        var notified = notifyRequester && !internalNote && store.PortalTicket(ticket.RequesterId, number) is not null;
        if (notified) store.AddNotification(HelpdeskStore.PortalAudience, ticket.RequesterId, HelpdeskStore.NotificationKinds.StaffComment, number);

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
        if (notified) Message += " The requester has been notified.";

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
            return RedirectToPage(null, null, new { number }, "attachments-tab");
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
        return RedirectToPage(null, null, new { number }, "attachments-tab");
    }

    public IActionResult OnPostShareAttachment(int number, Guid id, bool visible)
    {
        Message = store.SetAttachmentVisibleToRequester(number, id, visible)
            ?? (visible ? "The requester can now see that file in the staff portal." : "That file is no longer shown to the requester.");
        return RedirectToPage(null, null, new { number }, "attachments-tab");
    }

    public IActionResult OnPostRemoveAttachment(int number, Guid id)
    {
        Message = store.RemoveTicketAttachment(number, id) ?? "Attachment removed.";
        return RedirectToPage(null, null, new { number }, "attachments-tab");
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

    // Typed as people see it - SITE-5 - and looked up (HelpdeskStore.FindTicketByRef).
    public IActionResult OnPostLinkRelated(int number, string? other)
    {
        var (found, error) = store.FindTicketByRef(other, store.TicketVisibility(User));
        Message = found is { } otherNumber ? store.LinkRelatedTickets(number, otherNumber) ?? $"Linked to {store.Ref(otherNumber)}." : error;
        return RedirectToPage(new { number });
    }

    public IActionResult OnPostUnlinkTicket(int number, int other)
    {
        Message = store.UnlinkTickets(number, other) ?? $"Link to {store.Ref(other)} removed.";
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

    // Telling staff through EduNotify (Settings → EduNotify). Offered to someone with the Tell staff permission once
    // EduNotify is set up and switched on; a notice already showing is listed for anyone who can see the ticket.
    public bool CanTellStaff => CanEdit && store.UserHasFlag(User, Modules.Flags.NotifyStaff) && store.EduNotifySettings.Ready;
    public HelpdeskStore.StaffNotice? StaffNotice => Ticket is null ? null : store.StaffNoticeFor(Ticket.Number);
    public EduNotify.Catalogue? NoticeCatalogue => store.EduNotifyCatalogue;

    // Sends the notice, or replaces the one showing (EduNotify swaps a live message with the same reference). Only fields
    // the template lets a caller change are sent; blank ones keep the template's own.
    public async Task<IActionResult> OnPostTellStaffAsync(int number, string? templateId, bool everyone, string[]? groups, string? title, string? body, CancellationToken cancel)
    {
        if (!store.UserHasFlag(User, Modules.Flags.NotifyStaff)) return Forbid();
        var ticket = store.Tickets.FirstOrDefault(x => x.Number == number);
        if (ticket is null) return NotFound();
        if (!store.EduNotifySettings.Ready) { Message = "EduNotify isn't set up, or sending is switched off, in Settings → EduNotify."; return RedirectToPage(new { number }); }
        if (TicketInsights.IsClosed(ticket)) { Message = "This ticket is closed, so there's nothing to tell staff about. Reopen it first."; return RedirectToPage(new { number }); }

        var catalogue = store.EduNotifyCatalogue;
        if (catalogue is null)
        {
            var refresh = await eduNotify.RefreshAsync(cancel);
            catalogue = store.EduNotifyCatalogue;
            if (catalogue is null) { Message = $"Staff weren't told. {refresh.Message}"; return RedirectToPage(new { number }); }
        }
        if (catalogue.Sendable.FirstOrDefault(x => x.Id == templateId) is not { } template)
        {
            Message = "Choose a template. If the one you want isn't listed, EduNotify's key isn't allowed it.";
            return RedirectToPage(new { number });
        }
        var chosen = (groups ?? []).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var known = chosen.Select(x => catalogue.Groups.FirstOrDefault(g => string.Equals(g.Name, x, StringComparison.OrdinalIgnoreCase))?.Name).ToList();
        if (known.Any(x => x is null)) { Message = "One of the groups chosen no longer exists in EduNotify. Choose again."; return RedirectToPage(new { number }); }
        if (!everyone && chosen.Count == 0) { Message = "Choose which groups of PCs to tell, or Everyone."; return RedirectToPage(new { number }); }

        title = template.CanChangeTitle && !string.IsNullOrWhiteSpace(title) ? title.Trim() : null;
        body = template.CanChangeBody && !string.IsNullOrWhiteSpace(body) ? body.Trim() : null;
        if (title?.Length > EduNotify.MaxTitle) { Message = $"The title can be at most {EduNotify.MaxTitle} characters."; return RedirectToPage(new { number }); }
        if (body?.Length > EduNotify.MaxBody) { Message = $"The message can be at most {EduNotify.MaxBody} characters."; return RedirectToPage(new { number }); }

        List<string> groupNames = everyone ? [] : known.Select(x => x!).ToList();
        var (outcome, sent) = await eduNotify.SendAsync(new EduNotify.Send(template.Id, everyone, groupNames, title, body, EduNotify.ReferenceFor(number)), cancel);
        if (sent is null) { Message = $"Staff weren't told. {outcome.Message}"; return RedirectToPage(new { number }); }

        var updating = store.StaffNoticeFor(number) is not null;
        store.RecordStaffNoticeSent(new HelpdeskStore.StaffNotice(number, sent.Id, template.Id, template.Name, everyone, groupNames,
            title ?? template.Title, body ?? template.Body, DateTime.UtcNow, store.CurrentActor().Name, sent.Online, sent.Targeted));
        Message = $"{(updating ? "The notice was updated." : "Staff have been told.")} {outcome.Message}";
        return RedirectToPage(new { number });
    }

    public async Task<IActionResult> OnPostRecallStaffNoticeAsync(int number, CancellationToken cancel)
    {
        if (!store.UserHasFlag(User, Modules.Flags.NotifyStaff)) return Forbid();
        if (store.StaffNoticeFor(number) is null) { Message = "This ticket has no notice showing."; return RedirectToPage(new { number }); }
        var outcome = await eduNotify.RecallAsync(number, cancel);
        if (outcome.Ok) store.RecordStaffNoticeRecalled(number, outcome.NothingLive ? "from the ticket (it had already gone from EduNotify)" : "from the ticket");
        Message = outcome.Ok ? "The notice was taken off staff PCs." : $"The notice couldn't be recalled. {outcome.Message}";
        return RedirectToPage(new { number });
    }

    public IActionResult OnPostCreateFollowUp(int number)
    {
        var (followUp, error) = store.CreateFollowUpTicket(number);
        if (followUp is null)
        {
            Message = error;
            return RedirectToPage(new { number });
        }
        Message = $"Follow-up ticket {store.Ref(followUp.Value)} created from {store.Ref(number)}. Edit its details below.";
        return RedirectToPage(new { number = followUp.Value });
    }

    public IActionResult OnPostClose(int number, string? closingMessage)
    {
        var ticket = store.Tickets.FirstOrDefault(x => x.Number == number);
        if (ticket is null) return NotFound();

        if (ticket.Status == "Closed")
        {
            Message = "Ticket is already closed.";
            return RedirectToPage(new { number });
        }

        if (store.CloseBlockedByChecklist(number) is { } blocked)
        {
            Message = blocked;
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
        Message = "Ticket closed.";
        return RedirectToPage(new { number });
    }

    public IActionResult OnPostDelete(int number)
    {
        if (!store.UserCan(User, Modules.Tickets, ModulePermission.Delete)) return Forbid();
        // Worked out first: once it's gone there is nothing to look its number up from.
        var shown = store.Ref(number);
        var message = store.DeleteTicket(number);
        if (message is not null)
        {
            Message = message;
            return RedirectToPage(new { number });
        }
        Message = $"Ticket {shown} deleted.";
        return RedirectToPage("/Jobs");
    }

    public IActionResult OnPostMerge(int number, int targetNumber)
    {
        if (!store.UserCan(User, Modules.Tickets, ModulePermission.Delete)) return Forbid();
        if (!store.CanSeeTicket(User, targetNumber)) return NotFound();
        var message = store.MergeTicket(number, targetNumber);
        if (message is not null)
        {
            Message = message;
            return RedirectToPage(new { number });
        }
        Message = $"Ticket {store.Ref(number)} was merged into ticket {store.Ref(targetNumber)}.";
        return RedirectToPage(new { number = targetNumber });
    }
}
