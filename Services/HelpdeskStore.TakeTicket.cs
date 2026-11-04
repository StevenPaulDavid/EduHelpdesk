using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// Assign to me and On my way: taking a ticket for yourself, from the ticket page (Pages/Job) or the phone app
// (PhoneEndpoints). One set of rules and one set of words for both, so the phone never allows what the web page refuses.
// Whether the person may edit tickets at all is the caller's question (Tickets: Edit); these are about the ticket.
public sealed partial class HelpdeskStore
{
    // Message is what to tell the person, whether it worked or not. Assigned: the ticket changed hands.
    public sealed record TakeResult(bool Ok, string Message, bool Assigned = false);

    public const string NotATechnicianMessage = "Your account isn't a technician account, so tickets can't be assigned to it.";

    // Why this technician can't take this ticket, or null if they can. Team before desk, as the page always asked.
    public string? TakeRefusal(TicketRecord ticket, TechnicianRecord technician, bool onMyWay)
    {
        if (onMyWay && TicketInsights.IsClosed(ticket)) return "This ticket is closed. Reopen it first if you're going back to it.";
        if (onMyWay && PortalTicket(ticket.RequesterId, ticket.Number) is null)
            return "The requester can't see this ticket in the staff portal, so they can't be told you're on your way.";
        if (!TechnicianInTeam(technician, ticket.TeamName))
            return $"This ticket belongs to the {ticket.TeamName} team, which you're not in. Change the team first if you're taking it on.";
        if (DeskOf(ticket) is var desk && !TechnicianOnDesk(technician, desk.Id))
            return $"This ticket is on the {desk.Name} desk, which you're not on. Move it to your desk first if you're taking it on.";
        return null;
    }

    public TakeResult AssignToMe(int number, Guid technicianId)
    {
        if (Tickets.FirstOrDefault(x => x.Number == number) is not { } ticket) return new TakeResult(false, "That ticket doesn't exist.");
        if (Technicians.FirstOrDefault(x => x.Id == technicianId) is not { } technician) return new TakeResult(false, NotATechnicianMessage);
        if (ticket.TechnicianId == technicianId) return new TakeResult(false, "This ticket is already assigned to you.");
        if (TakeRefusal(ticket, technician, onMyWay: false) is { } refusal) return new TakeResult(false, refusal);
        UpdateTicket(ticket with { TechnicianId = technicianId });
        return new TakeResult(true, "Assigned to you.", Assigned: true);
    }

    // Takes the ticket, as Assign to me does, and tells the requester who is coming: a pop-up in their staff portal and a
    // comment on the ticket, which is where clicking the pop-up lands and which keeps a record of when it was said.
    public TakeResult SayOnMyWay(int number, Guid technicianId)
    {
        if (Tickets.FirstOrDefault(x => x.Number == number) is not { } ticket) return new TakeResult(false, "That ticket doesn't exist.");
        if (Technicians.FirstOrDefault(x => x.Id == technicianId) is not { } technician) return new TakeResult(false, NotATechnicianMessage);
        if (TakeRefusal(ticket, technician, onMyWay: true) is { } refusal) return new TakeResult(false, refusal);
        var assigned = ticket.TechnicianId != technicianId;
        if (assigned) UpdateTicket(ticket with { TechnicianId = technicianId });
        AddTicketComment(number, $"{technician.Name} is on their way.");
        var notified = AddNotification(PortalAudience, ticket.RequesterId, NotificationKinds.OnMyWay, number, technicianId) != 0;
        return new TakeResult(true, (assigned ? "Assigned to you. " : "")
            + (notified ? "The requester has been told you're on your way." : "A comment says you're on your way, but requester notifications are switched off in Settings, so no pop-up was sent."), assigned);
    }
}
