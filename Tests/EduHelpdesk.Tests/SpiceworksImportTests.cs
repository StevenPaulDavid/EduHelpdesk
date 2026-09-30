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
