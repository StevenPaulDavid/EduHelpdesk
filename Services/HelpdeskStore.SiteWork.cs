using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// Day-to-day site and cleaning work (stage 3 of desks): jobs logged after they were dealt with - the spill called in on
// the radio and cleaned up before anyone reached a computer - and the daily sheet a supervisor prints for the team.
public sealed partial class HelpdeskStore
{
    public sealed record DoneJob(string? Category, string? Title, string? Notes, string? Location, Guid? TechnicianId, DateTime? At);

    // Saved as a closed ticket: no SLA, no ping to anyone, the requester is whoever logs it (their staff record, made for
    // them if they have none, as the staff portal does). Returns the ticket's key, or the reason it wasn't saved.
    public (int? Number, string? Error) LogDoneJob(Guid loggedBy, DoneJob job)
    {
        var title = (job.Title ?? "").Trim();
        if (title.Length == 0) return (null, "Say in a few words what was dealt with.");
        if (title.Length > MaxTicketTitleLength) return (null, $"Keep it to {MaxTicketTitleLength} characters - put the rest in the notes.");
        var notes = (job.Notes ?? "").Trim();
        if (notes.Length > MaxTicketTextLength) return (null, $"Keep the notes under {MaxTicketTextLength} characters.");
        var (requester, problem) = PortalRequesterForTechnician(loggedBy);
        if (requester is null) return (null, problem);
        lock (_sync)
        {
            var category = _data.Categories.FirstOrDefault(x => string.Equals(x, job.Category?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (category is null) return (null, "Choose what kind of job it was.");
            var desk = DeskForCategoryCore(category);
            string? location = null;
            if (!string.IsNullOrWhiteSpace(job.Location))
            {
                location = _data.Locations.FirstOrDefault(x => string.Equals(x, job.Location.Trim(), StringComparison.OrdinalIgnoreCase));
                if (location is null) return (null, "Choose a room from the list.");
            }
            var technician = _data.Technicians.FirstOrDefault(x => x.Id == (job.TechnicianId ?? loggedBy));
            if (technician is null) return (null, "Choose who dealt with it.");
            if (!TechnicianOnDesk(technician, desk.Id)) return (null, $"{technician.Name} isn't on the {desk.Name} desk.");
            var now = DateTime.UtcNow;
            // A time typed in a form is the school's wall-clock time.
            var at = job.At is { } typed ? (typed.Kind == DateTimeKind.Utc ? typed : DateTime.SpecifyKind(typed, DateTimeKind.Local).ToUniversalTime()) : now;
            // A minute or two of clock drift between PCs is fine; next week isn't.
            if (at > now.AddMinutes(5)) return (null, "That time hasn't happened yet.");
            var team = DeskForTeamCore(technician.Team)?.Id == desk.Id ? technician.Team : null;
            var priority = _data.Priorities.FirstOrDefault(x => string.Equals(x, "Normal", StringComparison.OrdinalIgnoreCase)) ?? _data.Priorities.FirstOrDefault() ?? "Normal";
            var number = AddTicketCore(new TicketRecord(0, title, notes, requester.Id, [], technician.Id, priority, TicketInsights.ClosedStatus, category, at, at,
                null, null, false, false, team, location), $"Logged after it was dealt with by {technician.Name}.");
            Save();
            return (number, null);
        }
    }
}
