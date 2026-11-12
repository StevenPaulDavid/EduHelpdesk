using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// The planner (Pages/Jobs, show=planner): booking a ticket in for a day, and at the same time giving it to the person
// whose row it was dropped on. Both go through BulkUpdateTickets, so the ticket page's rules and history lines apply.
public sealed partial class HelpdeskStore
{
    // What a planner move does to who has the ticket: leave it, take it off everyone, or give it to someone.
    public readonly record struct PlanAssignee(bool Change, Guid? TechnicianId)
    {
        public static readonly PlanAssignee Keep = new(false, null);
        public static readonly PlanAssignee Nobody = new(true, null);
        public static PlanAssignee To(Guid technicianId) => new(true, technicianId);
    }

    // day null takes the ticket off the plan. Nothing changes unless all of it can: a person who can't have the ticket
    // (not in its team, not on its desk) is refused before the day is touched.
    public (bool Ok, string Message) PlanTicket(int number, DateOnly? day, PlanAssignee assignee)
    {
        lock (_sync)
        {
            if (_data.Tickets.FirstOrDefault(x => x.Number == number) is not { } ticket) return (false, TicketNotFoundMessage);
            var name = RefCore(ticket);
            if (day is not null && TicketInsights.IsClosed(ticket)) return (false, $"{name} is closed, so it can't be planned. Reopen it first.");
            TechnicianRecord? person = null;
            if (assignee is { Change: true, TechnicianId: { } id })
            {
                person = _data.Technicians.FirstOrDefault(x => x.Id == id);
                if (person is null) return (false, "Choose who to give it to.");
            }

            var assign = assignee.Change && ticket.TechnicianId != assignee.TechnicianId;
            if (assign)
            {
                var given = BulkUpdateTickets([number], new("technician", TechnicianId: assignee.TechnicianId));
                if (given.Error is { } error) return (false, error);
                if (given.Skipped > 0) return (false, PlanRefusal(name, person, given.SkippedReason));
            }
            var moved = ticket.PlannedFor != day;
            if (moved)
            {
                var planned = BulkUpdateTickets([number], new("plan", day?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? ""));
                if (planned.Error is { } error) return (false, error);
                if (planned.Skipped > 0) return (false, planned.SkippedReason ?? $"{name} couldn't be planned.");
            }
            if (!assign && !moved) return (true, $"{name} is already there.");

            var when = day is { } on ? on.ToString("dddd d MMMM", System.Globalization.CultureInfo.GetCultureInfo("en-GB")) : null;
            var who = !assign ? "" : person is null ? " and unassigned" : $" and given to {person.Name}";
            if (!moved) return (true, person is null ? $"{name} is now unassigned." : $"{name} given to {person.Name}.");
            return (true, when is null ? $"{name} taken off the plan{who}." : $"{name} planned for {when}{who}.");
        }
    }

    // The bulk action's reasons are worded for many tickets; this is one.
    private static string PlanRefusal(string name, TechnicianRecord? person, string? reason) =>
        person is null ? reason ?? $"{name} couldn't be moved."
        : reason is not null && reason.Contains("team", StringComparison.OrdinalIgnoreCase) ? $"{person.Name} isn't in the team {name} is assigned to, so it wasn't moved."
        : reason is not null && reason.Contains("desk", StringComparison.OrdinalIgnoreCase) ? $"{person.Name} isn't on {name}'s desk, so it wasn't moved."
        : reason ?? $"{name} couldn't be moved.";
}
