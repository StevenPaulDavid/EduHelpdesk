using EduHelpdesk.Models;
using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Services;

// Projects and helpdesk tickets: a purchase often starts as a ticket ("we need 30 iPads for Year 5"), and the two stay
// linked so either can be reached from the other. Links live on the project; both sides get a history line.
public sealed partial class HelpdeskStore
{
    public const int DefaultProjectLeadDays = 28;

    public IReadOnlyList<ProjectRecord> ProjectsForTicket(int ticketNumber)
    {
        lock (_sync) return _data.Projects.Where(x => x.TicketNumbers.Contains(ticketNumber)).OrderByDescending(x => x.Number).ToList();
    }

    public (bool Ok, string Message) LinkProjectTicket(int projectNumber, int ticketNumber)
    {
        lock (_sync)
        {
            var index = _data.Projects.FindIndex(x => x.Number == projectNumber);
            if (index < 0) return (false, "Project was not found.");
            var ticketIndex = _data.Tickets.FindIndex(x => x.Number == ticketNumber);
            if (ticketIndex < 0) return (false, $"There is no ticket #{ticketNumber}.");
            var project = _data.Projects[index];
            if (project.TicketNumbers.Contains(ticketNumber)) return (false, $"{project.Reference} is already linked to #{ticketNumber}.");
            var ticket = _data.Tickets[ticketIndex];
            _data.Projects[index] = WithHistory(project with { TicketNumbers = [.. project.TicketNumbers, ticketNumber] },
                "Ticket linked", $"Linked to ticket #{ticketNumber} - {ticket.Title}.");
            AddLinkHistory(ticketIndex, "Project linked", $"Linked to project {project.Reference} - {project.Title}.");
            Save();
            return (true, $"{project.Reference} linked to ticket #{ticketNumber}.");
        }
    }

    public (bool Ok, string Message) UnlinkProjectTicket(int projectNumber, int ticketNumber)
    {
        lock (_sync)
        {
            var index = _data.Projects.FindIndex(x => x.Number == projectNumber);
            if (index < 0) return (false, "Project was not found.");
            var project = _data.Projects[index];
            if (!project.TicketNumbers.Contains(ticketNumber)) return (false, $"{project.Reference} isn't linked to #{ticketNumber}.");
            _data.Projects[index] = WithHistory(project with { TicketNumbers = project.TicketNumbers.Where(x => x != ticketNumber).ToList() },
                "Ticket link removed", $"No longer linked to ticket #{ticketNumber}.");
            AddLinkHistory(_data.Tickets.FindIndex(x => x.Number == ticketNumber), "Project link removed", $"No longer linked to project {project.Reference}.");
            Save();
            return (true, $"Link between {project.Reference} and #{ticketNumber} removed.");
        }
    }

    // A technician turning a ticket into a purchasing project on the requester's behalf. It is raised for the ticket's
    // requester - it shows in their portal like one they raised - and waits for the project lead as usual: the priority
    // is a suggestion and nobody is assigned. The requester doesn't need the "Can raise projects" tick; that tick is
    // about who may ask from the portal, and here the IT team is doing the asking.
    public (bool Ok, string Message, int Number) StartProjectFromTicket(int ticketNumber, string? title, DateOnly? dueDate, string? itemsWanted, int suggestedPriority)
    {
        lock (_sync)
        {
            var ticketIndex = _data.Tickets.FindIndex(x => x.Number == ticketNumber);
            if (ticketIndex < 0) return (false, "Ticket was not found.", 0);
            var ticket = _data.Tickets[ticketIndex];
            var requester = _data.Users.FirstOrDefault(x => x.Id == ticket.RequesterId);
            if (requester is null) return (false, "This ticket's requester couldn't be found, so there is nobody to raise the project for.", 0);
            if (!requester.IsActive) return (false, $"{requester.Name}'s account is inactive. Reactivate it, or change the ticket's requester, first.", 0);
            if (dueDate is null) return (false, "Choose the date the proposal is needed by.", 0);
            if (dueDate < DateOnly.FromDateTime(DateTime.Now)) return (false, "The date the proposal is needed by can't be in the past.", 0);
            if (!ProjectPriorities.IsValid(suggestedPriority)) return (false, "Choose a priority from 1 to 5.", 0);
            if (CheckProjectText(title, itemsWanted, null) is { } error) return (false, error, 0);

            var number = NextProjectNumber();
            var now = DateTime.UtcNow;
            var actor = CurrentActor();
            var project = new ProjectRecord(number, title!.Trim(), requester.Id, dueDate.Value, itemsWanted!.Trim(), now)
            {
                SuggestedPriority = suggestedPriority,
                TicketNumbers = [ticketNumber],
                History =
                [
                    new("Project raised", $"Started from ticket #{ticketNumber} for {requester.Name} by {actor.Name}, with a suggested priority of {ProjectPriorities.Label(suggestedPriority)}. Proposal needed by {Day(dueDate.Value)}.", now) { By = actor }
                ]
            };
            _data.Projects.Add(project);
            AddLinkHistory(ticketIndex, "Project started", $"Purchasing project {project.Reference} - {project.Title} was started from this ticket.");
            Save();
            return (true, $"Project {project.Reference} started from ticket #{ticketNumber}. It is waiting for the project lead to assign it.", number);
        }
    }

    // A deleted ticket drops out of every project's links. Called from RemoveTicketExtras, inside the lock.
    private void RemoveProjectTicketLinks(int ticketNumber)
    {
        for (var i = 0; i < _data.Projects.Count; i++)
            if (_data.Projects[i].TicketNumbers.Contains(ticketNumber))
                _data.Projects[i] = WithHistory(_data.Projects[i] with { TicketNumbers = _data.Projects[i].TicketNumbers.Where(x => x != ticketNumber).ToList() },
                    "Ticket link removed", $"Ticket #{ticketNumber} was deleted.");
    }

    // A ticket merged into another hands its project links to the ticket it was merged into.
    private void MoveProjectTicketLinks(int sourceNumber, int targetNumber)
    {
        for (var i = 0; i < _data.Projects.Count; i++)
        {
            var project = _data.Projects[i];
            if (!project.TicketNumbers.Contains(sourceNumber)) continue;
            var numbers = project.TicketNumbers.Select(x => x == sourceNumber ? targetNumber : x).Distinct().ToList();
            _data.Projects[i] = WithHistory(project with { TicketNumbers = numbers }, "Ticket link moved", $"Ticket #{sourceNumber} was merged into #{targetNumber}, so the project is now linked to #{targetNumber}.");
        }
    }

    // When the project was first marked ready - the moment a proposal existed. Read from its history, which records every
    // status change; null if it never has been.
    public static DateTime? ProposalReadyAt(ProjectRecord project) =>
        project.History.FirstOrDefault(x =>
            x.Details.Contains($"→ {ProjectStatuses.ProposalReady}.", StringComparison.Ordinal)
            || x.Details.StartsWith($"Reopened as {ProjectStatuses.ProposalReady}.", StringComparison.Ordinal))?.CreatedAt;

    private static void EnsureProjectTicketSchema(SqliteConnection connection)
    {
        // No foreign key to Tickets: WriteData inserts projects before tickets. WriteProjectTickets skips any number that
        // isn't a ticket, which is the same protection.
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ProjectTickets (ProjectNumber INTEGER NOT NULL, TicketNumber INTEGER NOT NULL, Position INTEGER NOT NULL,
                PRIMARY KEY (ProjectNumber, TicketNumber), FOREIGN KEY (ProjectNumber) REFERENCES Projects(Number) ON DELETE CASCADE);
            """;
        command.ExecuteNonQuery();
    }

    private static void ReadProjectTickets(SqliteConnection connection, StoreData data)
    {
        var byNumber = data.Projects.ToDictionary(x => x.Number);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ProjectNumber, TicketNumber FROM ProjectTickets ORDER BY ProjectNumber, Position;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            if (byNumber.TryGetValue(reader.GetInt32(0), out var project)) project.TicketNumbers.Add(reader.GetInt32(1));
    }

    private static void WriteProjectTickets(SqliteConnection connection, SqliteTransaction transaction, StoreData data)
    {
        var ticketNumbers = data.Tickets.Select(x => x.Number).ToHashSet();
        foreach (var project in data.Projects)
        {
            var position = 0;
            foreach (var ticket in project.TicketNumbers.Distinct().Where(ticketNumbers.Contains))
                Execute(connection, transaction, "INSERT INTO ProjectTickets (ProjectNumber, TicketNumber, Position) VALUES ($project,$ticket,$position);",
                    ("$project", project.Number), ("$ticket", ticket), ("$position", position++));
        }
    }
}
