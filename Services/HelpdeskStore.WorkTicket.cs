using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// Replying, changing status or priority, and closing: from the ticket page (Pages/Job) or the phone app (PhoneEndpoints).
// One set of rules and one set of words for both, as with taking a ticket (HelpdeskStore.TakeTicket). Whether the person
// may edit tickets at all, and can see this one, is the caller's question; these are about the ticket.
public sealed partial class HelpdeskStore
{
    // Message is what to tell the person, whether it worked or not. Notified: the requester was sent a portal pop-up.
    public sealed record WorkResult(bool Ok, string Message, bool Notified = false);

    public const string TicketNotFoundMessage = "Ticket was not found.";

    // One of the ticket's desk's statuses. Moving to Closed this way still needs the checklist done, as every way of
    // closing does; the closing message is the Close button's rule, not this one's, as on the web.
    public WorkResult SetTicketStatus(int number, string? status)
    {
        if (Tickets.FirstOrDefault(x => x.Number == number) is not { } ticket) return new WorkResult(false, TicketNotFoundMessage);
        if (string.IsNullOrWhiteSpace(status)) return new WorkResult(false, "Select a value.");
        if (StatusesFor(DeskOf(ticket).Id).FirstOrDefault(x => string.Equals(x, status.Trim(), StringComparison.OrdinalIgnoreCase)) is not { } chosen)
            return new WorkResult(false, "Select a valid value.");
        if (IsBuiltInStatus(chosen) && CloseBlockedByChecklist(number) is { } blocked) return new WorkResult(false, blocked);
        UpdateTicket(ticket with { Status = chosen, ClosedAt = chosen == "Closed" ? ticket.ClosedAt ?? DateTime.UtcNow : null });
        return new WorkResult(true, "Ticket updated.");
    }

    // A new priority; the SLA and due date follow it unless they were set by hand (WithPriority).
    public WorkResult SetTicketPriority(int number, string? priority)
    {
        if (Tickets.FirstOrDefault(x => x.Number == number) is not { } ticket) return new WorkResult(false, TicketNotFoundMessage);
        if (string.IsNullOrWhiteSpace(priority)) return new WorkResult(false, "Select a value.");
        if (Priorities.FirstOrDefault(x => string.Equals(x, priority.Trim(), StringComparison.OrdinalIgnoreCase)) is not { } chosen)
            return new WorkResult(false, "Select a valid value.");
        UpdateTicket(WithPriority(ticket, chosen));
        return new WorkResult(true, "Ticket updated.");
    }

    // A reply (a comment the requester can read) or an internal note, optionally moving the ticket to another status in the
    // same step - which reopens a closed one. Closing this way still needs the checklist done; the reply isn't saved if
    // it isn't, so nothing is half done.
    public WorkResult AddTicketReply(int number, string? text, bool internalNote, bool notifyRequester, string? status)
    {
        var label = internalNote ? "Internal note" : "Reply";
        if (string.IsNullOrWhiteSpace(text)) return new WorkResult(false, internalNote ? "Enter a note before saving." : "Enter a reply before saving.");
        if (Tickets.FirstOrDefault(x => x.Number == number) is not { } current) return new WorkResult(false, TicketNotFoundMessage);

        string? newStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            newStatus = StatusesFor(DeskOf(current).Id).FirstOrDefault(x => string.Equals(x, status.Trim(), StringComparison.OrdinalIgnoreCase));
            if (newStatus is null) return new WorkResult(false, "Select a valid status.");
        }
        if (newStatus is not null && IsBuiltInStatus(newStatus) && CloseBlockedByChecklist(number) is { } blocked) return new WorkResult(false, blocked);
        if (!AddTicketComment(number, text, internalNote)) return new WorkResult(false, TicketNotFoundMessage);

        var ticket = Tickets.First(x => x.Number == number);
        // Only a comment the requester can read can tell them anything, and only on a ticket they can see in the portal
        // (an onboarding's is internal).
        var notified = notifyRequester && !internalNote && PortalTicket(ticket.RequesterId, number) is not null;
        if (notified) AddNotification(PortalAudience, ticket.RequesterId, NotificationKinds.StaffComment, number);

        string message;
        if (newStatus is null || string.Equals(newStatus, ticket.Status, StringComparison.OrdinalIgnoreCase))
        {
            message = $"{label} added.";
        }
        else
        {
            var wasClosed = ticket.Status == "Closed";
            UpdateTicket(ticket with { Status = newStatus, ClosedAt = newStatus == "Closed" ? ticket.ClosedAt ?? DateTime.UtcNow : null });
            message = newStatus == "Closed" ? $"{label} added and ticket closed."
                : wasClosed ? $"{label} added and ticket reopened."
                : $"{label} added and status changed to {newStatus}.";
        }
        if (notified) message += " The requester has been notified.";
        return new WorkResult(true, message, notified);
    }

    // The Close button: the checklist done, and a closing message where Settings asks for one on this priority or
    // category. The message is kept as a comment the requester can read.
    public WorkResult CloseTicket(int number, string? closingMessage)
    {
        if (Tickets.FirstOrDefault(x => x.Number == number) is not { } ticket) return new WorkResult(false, TicketNotFoundMessage);
        if (ticket.Status == "Closed") return new WorkResult(false, "Ticket is already closed.");
        if (CloseBlockedByChecklist(number) is { } blocked) return new WorkResult(false, blocked);
        if (RequiresCloseMessage(ticket) && string.IsNullOrWhiteSpace(closingMessage))
            return new WorkResult(false, $"A closing message is required for {ticket.Priority} priority / {ticket.Category} tickets before they can be closed.");

        if (!string.IsNullOrWhiteSpace(closingMessage)) AddTicketComment(number, closingMessage);
        var current = Tickets.FirstOrDefault(x => x.Number == number) ?? ticket;
        UpdateTicket(current with { Status = "Closed", ClosedAt = current.ClosedAt ?? DateTime.UtcNow });
        return new WorkResult(true, "Ticket closed.");
    }
}
