using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace EduHelpdesk.Tests;

// Reading Spiceworks Cloud Help Desk's data export and previewing what it would become. The workbook here is made up,
// in the shape of the real export (same sheet names and column headings) - real exports hold real people's tickets and
// never belong in the tests.
public class SpiceworksImportTests
{
    // One sheet: a heading row, then rows. Numbers are written as numbers, the rest as text, as Excel would hold them.
    private static byte[] Workbook(params (string Sheet, string[] Headings, object?[][] Rows)[] sheets)
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbook = document.AddWorkbookPart();
            workbook.Workbook = new Workbook(new Sheets());
            uint id = 1;
            foreach (var (name, headings, rows) in sheets)
            {
                var part = workbook.AddNewPart<WorksheetPart>();
                var data = new SheetData();
                foreach (var cells in new[] { headings.Cast<object?>().ToArray() }.Concat(rows))
                {
                    var row = new Row();
                    for (var i = 0; i < cells.Length; i++)
                    {
                        var reference = $"{(char)('A' + i)}{data.ChildElements.Count + 1}";
                        row.Append(cells[i] switch
                        {
                            null => new Cell { CellReference = reference },
                            int or long => new Cell { CellReference = reference, CellValue = new CellValue(Convert.ToString(cells[i], System.Globalization.CultureInfo.InvariantCulture)!), DataType = CellValues.Number },
                            _ => new Cell { CellReference = reference, CellValue = new CellValue(cells[i]!.ToString()!), DataType = CellValues.String }
                        });
                    }
                    data.Append(row);
                }
                part.Worksheet = new Worksheet(data);
                workbook.Workbook.Sheets!.Append(new Sheet { Id = workbook.GetIdOfPart(part), SheetId = id++, Name = name });
            }
        }
        return stream.ToArray();
    }

    private const string Monday = "2025-09-01T09:00:00+00:00";

    private static byte[] Sample() => Workbook(
        // Columns deliberately in a different order from the real export: they are found by heading.
        ("Tickets", ["summary", "ticket_number", "ticket_id", "status", "priority", "ticket_category_id", "assignee_id", "end_user_id", "creator_id", "description", "created_at", "last_closed_at", "due_at", "master_ticket_number"],
        [
            ["Printer jammed", 11, 900011L, "Closed", "2", 51, 1, 101, null, "Tray 2 jams.", Monday, "2025-09-02T09:00:00+00:00", null, null],
            ["Board frozen", 12, 900012L, "Waiting", "1", 52, 2, 102, null, "Room 4.", Monday, null, "2025-09-05T15:00:00Z", null],
            ["Laptop battery", 13, 900013L, "Open", "3", null, 9, null, 1, "Logged by IT.", Monday, null, null, 11],
            ["Odd one", 14, 900014L, "Open", "2", 99, null, 103, null, "Unknown category and requester.", Monday, null, null, 999]
        ]),
        ("Ticket Comments", ["ticket_comment_id", "ticket_id", "body", "private", "creator_id", "end_user_id", "created_at", "deleted"],
        [
            [1L, 900011L, "Cleared the tray.", "false", 1, null, Monday, "false"],
            [2L, 900011L, "Fuser worn.", "true", 1, null, Monday, "false"],
            [3L, 900012L, "Still frozen", "false", null, 102, Monday, "false"],
            [4L, 900012L, "", "false", 2, null, Monday, "false"],
            [5L, 900012L, "Deleted in Spiceworks", "false", 2, null, Monday, "true"],
            [6L, 777L, "For a ticket not in the export", "false", 1, null, Monday, "false"]
        ]),
        ("Ticket Changes", ["ticket_change_id", "ticket_id", "action", "body", "creator_id", "created_at"],
        [
            [1L, 900011L, "ticket updated", "Assigned to Ros Bowstead", 1, Monday],
            [2L, 900011L, "ticket updated", "Ros Bowstead changed status from Open to Closed", 1, Monday]
        ]),
        ("Users", ["user_id", "first_name", "last_name", "email"],
        [
            [1, "Ros", "Bowstead", "ros.bowstead@school.example"],
            [2, "Eric", "Richardson", "somebody.else@school.example"]
        ]),
        ("End Users", ["end_user_id", "first_name", "last_name", "email"],
        [
            [101, "Priya", "Shah", "PRIYA.SHAH@test.example"],
            [102, "", "", "new.person@test.example"]
        ]),
        ("Ticket Categories", ["ticket_category_id", "name"], [[51, "Printer"], [52, "Board Issue"]]),
        ("Custom Attributes", ["custom_attribute_id", "name", "label"], [[7, "asset_tag", "Asset Tag"]]),
        ("Tickets to Custom Attributes", ["ticket_id", "custom_attribute_id", "name", "value"], [[900013L, 7, "asset_tag", "AS1234"], [900013L, 8, null, "orphan"]]),
        ("Labors", ["labor_id", "ticket_id", "user_id", "duration", "body", "created_at"], [[1L, 900011L, 1, 15, "Ros Bowstead worked for 15m", Monday]]),
        ("Organizations", ["organization_id", "name"], [[1, "Test School"]]));

    private static SpiceworksExport Read(byte[] bytes)
    {
        var (export, error) = SpiceworksExport.Read(new MemoryStream(bytes));
        Assert.Null(error);
        return export!;
    }

    [Fact]
    public void The_export_is_read_by_column_heading_and_deleted_comments_are_left_out()
    {
        var export = Read(Sample());
        Assert.Equal("Test School", export.OrganizationName);
        Assert.Equal([11, 12, 13, 14], export.Tickets.Select(x => x.Number));
        var board = export.Tickets.Single(x => x.Number == 12);
        Assert.Equal(("Board frozen", "Waiting", "1", 102L, 52L), (board.Summary, board.Status, board.Priority, board.EndUserId, board.CategoryId));
        Assert.Equal(new DateTimeOffset(2025, 9, 5, 15, 0, 0, TimeSpan.Zero), board.DueAt);
        Assert.Equal(11, export.Tickets.Single(x => x.Number == 13).MasterNumber);
        Assert.DoesNotContain(export.Comments, x => x.Body == "Deleted in Spiceworks");
        Assert.True(export.Comments.Single(x => x.Id == 2).Private);
        Assert.Equal(2, export.Changes.Count);
        Assert.Equal(15, export.Labors.Single().Minutes);
    }

    [Fact]
    public void A_file_that_is_not_a_Spiceworks_export_is_turned_away()
    {
        Assert.NotNull(SpiceworksExport.Read(new MemoryStream("Name,Email\r\nJo,jo@x"u8.ToArray())).Error);
        Assert.NotNull(SpiceworksExport.Read(new MemoryStream(Workbook(("Sheet1", ["a"], [["b"]])))).Error);
    }

    [Fact]
    public void The_preview_matches_people_suggests_values_and_names_what_cannot_come_across()
    {
        using var test = new TestStore();
        test.AddRequester("Priya Shah");   // priya.shah@test.example - matched whatever the case of the email
        var plan = test.Store.PlanSpiceworksImport(Read(Sample()), new Dictionary<string, string>());

        Assert.Equal(4, plan.Tickets);
        Assert.Equal(3, plan.OpenTickets);
        Assert.Equal((3, 1, 1, 1), (plan.Comments, plan.InternalNotes, plan.RequesterReplies, plan.EmptyComments));
        Assert.Equal((2, 2, 1, 1), (plan.HistoryLines, plan.Merges, plan.TimeEntries, plan.DueDates));

        Assert.Equal("Priya Shah", plan.Requesters.Single(x => x.Email.Equals("priya.shah@test.example", StringComparison.OrdinalIgnoreCase)).ExistingName);
        Assert.Null(plan.Requesters.Single(x => x.Email == "new.person@test.example").ExistingName);
        Assert.Equal("new.person", plan.Requesters.Single(x => x.Email == "new.person@test.example").Name);
        // No requester (logged by IT), and one naming an end user the export doesn't have.
        Assert.Equal(2, plan.PlaceholderTickets);

        // Neither Spiceworks technician has an account here: one's email is new, the other's name and email both are.
        Assert.All(plan.Technicians, x => Assert.Null(x.MatchedTo));
        Assert.Equal(4, plan.UnassignedTickets);

        string Chosen(string kind, string value) => plan.Values.Single(x => x.Kind == kind && x.Value == value).Chosen;
        Assert.Equal("High", Chosen("Priority", "1"));
        Assert.Equal("Normal", Chosen("Priority", "2"));
        Assert.Equal("Low", Chosen("Priority", "3"));
        Assert.Equal("On Hold", Chosen("Status", "Waiting"));
        Assert.Equal("Closed", Chosen("Status", "Closed"));
        Assert.Equal(HelpdeskStore.SpiceworksNewValue, Chosen("Category", "Printer"));
        Assert.NotEqual(HelpdeskStore.SpiceworksNewValue, Chosen("Category", HelpdeskStore.SpiceworksNoCategory));

        Assert.Equal("Asset Tag", plan.Attributes.Single().Label);
        Assert.Contains(plan.Problems, x => x.Contains("deleted in Spiceworks"));
        Assert.Contains(plan.Problems, x => x.Contains("aren't in the Tickets sheet"));
        Assert.Contains(plan.Problems, x => x.Contains("isn't in the Ticket Categories sheet"));
        Assert.Contains(plan.Problems, x => x.Contains("isn't in the export"));
    }

    [Fact]
    public void Importing_brings_tickets_comments_history_and_fields_across_and_a_second_import_duplicates_nothing()
    {
        using var test = new TestStore();
        var store = test.Store;
        var priya = test.AddRequester("Priya Shah");
        var ros = new TechnicianRecord(Guid.NewGuid(), "Ros Bowstead", "ros.bowstead@school.example", "", "Technician", null, false, true);
        store.AddTechnician(ros);
        var export = Read(Sample());
        var choices = new Dictionary<string, string> { [HelpdeskStore.ChoiceKey("Category", "Board Issue")] = store.Categories[0] };
        var peopleBefore = store.Users.Count;
        var ticketsBefore = store.Tickets.Count;

        var (ok, message, result) = store.ApplySpiceworksImport(export, choices, "export.xlsx");
        Assert.True(ok, message);
        Assert.Equal(4, result!.Tickets);
        Assert.Contains(store.ListBackups(), x => x.Name.Contains("before-spiceworks"));
        Assert.Equal(ticketsBefore + 4, store.Tickets.Count);
        // new.person, plus the placeholder for the tickets with no requester that can be brought across.
        Assert.Equal(peopleBefore + 2, store.Users.Count);
        Assert.False(store.Users.Single(x => x.Name == HelpdeskStore.SpiceworksPlaceholderName).IsActive);

        var spiceworksNumber = store.TicketAttributeDefinitions.Single(x => x.Name == HelpdeskStore.SpiceworksNumberAttribute).Id;
        TicketRecord Imported(int spiceworks) => store.Tickets.Single(t => store.GetTicketAttributeValues(t.Number).GetValueOrDefault(spiceworksNumber) == spiceworks.ToString());

        var printer = Imported(11);
        Assert.Equal(priya.Id, printer.RequesterId);
        Assert.Equal(ros.Id, printer.TechnicianId);
        Assert.Equal(("Closed", "Normal", "Printer"), (printer.Status, printer.Priority, printer.Category));
        Assert.Equal(new DateTime(2025, 9, 2, 9, 0, 0, DateTimeKind.Utc), printer.ClosedAt);
        Assert.Equal(["Cleared the tray.", "Fuser worn."], printer.Comments.Select(x => x.Text));
        Assert.True(printer.Comments[1].IsInternal);
        Assert.Equal("Ros Bowstead", printer.Comments[0].By?.Name);
        Assert.Contains(printer.History, x => x.Details == "Assigned to Ros Bowstead" && x.Action == "Updated in Spiceworks");
        Assert.Contains(printer.History, x => x.Action == "Time logged in Spiceworks");
        Assert.Contains(printer.History, x => x.Action == "Imported from Spiceworks");
        Assert.False(HelpdeskStore.HasUpdateForRequester(printer));

        var board = Imported(12);
        Assert.Equal(("On Hold", "High", store.Categories[0]), (board.Status, board.Priority, board.Category));
        Assert.Null(board.ClosedAt);
        Assert.True(board.DueDateOverridden);
        Assert.Equal("new.person@test.example", store.Users.Single(x => x.Id == board.RequesterId).Email);
        // The requester's own reply is marked as theirs; the empty comment (an attachment in Spiceworks) isn't there.
        Assert.True(board.Comments.Single().FromRequester);
        // Eric has no account here, so the ticket is unassigned and its history says who had it.
        Assert.Null(board.TechnicianId);
        Assert.Contains(board.History, x => x.Details.Contains("Was assigned to Eric Richardson"));

        var laptop = Imported(13);
        Assert.Equal(HelpdeskStore.SpiceworksPlaceholderName, store.Users.Single(x => x.Id == laptop.RequesterId).Name);
        var assetTag = store.TicketAttributeDefinitions.Single(x => x.Name == "Asset Tag").Id;
        Assert.Equal("AS1234", store.GetTicketAttributeValues(laptop.Number)[assetTag]);
        // Merged into #11 in Spiceworks: linked to it here.
        Assert.Contains(store.GetTicketRelations(laptop.Number), x => x.Number == printer.Number);

        Assert.Contains(store.GetAuditEntries(), x => x.Action == "Imported from Spiceworks");
        Assert.Null(store.CompareDatabaseWithMemory());

        // The same file again: recognised, nothing duplicated.
        var again = test.Reopen().ApplySpiceworksImport(Read(Sample()), choices, "export.xlsx");
        Assert.True(again.Ok, again.Message);
        Assert.Equal((0, 4, 0), (again.Result!.Tickets, again.Result.AlreadyImported, again.Result.PeopleAdded));
        Assert.Equal(ticketsBefore + 4, test.Store.Tickets.Count);
        Assert.Equal(2, test.Store.SpiceworksImports.Count);
    }

    [Fact]
    public void The_ticket_list_search_finds_a_ticket_by_its_Spiceworks_number()
    {
        using var test = new TestStore();
        Assert.True(test.Store.ApplySpiceworksImport(Read(Sample()), new Dictionary<string, string>(), "export.xlsx").Ok);
        var store = test.Store;
        var context = new TicketContext(store.Users, store.Technicians, store.Assets, store.Priorities, store.Statuses, DateTime.UtcNow, 24, null, null, store.TicketAttributeAnswers());
        var found = new TicketListQuery { View = "all", Search = "AS1234" }.Run(store.Tickets, context);
        Assert.Equal("Laptop battery", Assert.Single(found).Title);
    }

    [Fact]
    public void Choices_made_in_the_preview_are_used_and_ones_that_are_not_real_values_are_ignored()
    {
        using var test = new TestStore();
        var export = Read(Sample());
        var category = test.Store.Categories[0];
        var plan = test.Store.PlanSpiceworksImport(export, new Dictionary<string, string>
        {
            [HelpdeskStore.ChoiceKey("Category", "Printer")] = category,
            [HelpdeskStore.ChoiceKey("Priority", "1")] = "Urgent",
            [HelpdeskStore.ChoiceKey("Status", "Waiting")] = "Not a status",
            [HelpdeskStore.ChoiceKey("Category", HelpdeskStore.SpiceworksNoCategory)] = HelpdeskStore.SpiceworksNewValue
        });
        string Chosen(string kind, string value) => plan.Values.Single(x => x.Kind == kind && x.Value == value).Chosen;
        Assert.Equal(category, Chosen("Category", "Printer"));
        Assert.Equal("Urgent", Chosen("Priority", "1"));
        Assert.Equal("On Hold", Chosen("Status", "Waiting"));
        // "No category" can't be added as a new category - it keeps the suggestion.
        Assert.NotEqual(HelpdeskStore.SpiceworksNewValue, Chosen("Category", HelpdeskStore.SpiceworksNoCategory));
    }
}
