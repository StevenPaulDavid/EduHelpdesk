using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace EduHelpdesk.Services;

// Spiceworks Cloud Help Desk's data export: one Excel workbook with a sheet per table (Tickets, Ticket Comments,
// Ticket Changes, Users, End Users, Ticket Categories, Custom Attributes, Tickets to Custom Attributes, Labors), linked by
// Spiceworks' own ids. This reads it into plain records and nothing else - what they become in EduHelpdesk is
// HelpdeskStore.SpiceworksImport.cs. Columns are found by their heading, never by position, so a column added or
// reordered in a later export doesn't shift everything.
public sealed class SpiceworksExport
{
    public sealed record Person(long Id, string FirstName, string LastName, string Email)
    {
        // "Jo Bloggs", or the part of the email before the @ when Spiceworks has no name.
        public string Name => $"{FirstName} {LastName}".Trim() is { Length: > 0 } name ? name : Email.Split('@')[0];
    }

    public sealed record Ticket(long Id, int Number, string Summary, string Description, string Priority, string Status,
        long? CategoryId, long? CreatorId, long? AssigneeId, long? EndUserId,
        DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, DateTimeOffset? ClosedAt, DateTimeOffset? DueAt, int? MasterNumber);

    public sealed record Comment(long Id, long TicketId, string Body, bool Private, long? CreatorId, long? EndUserId, DateTimeOffset CreatedAt);
    public sealed record Change(long Id, long TicketId, string Action, string Body, long? CreatorId, DateTimeOffset CreatedAt);
    public sealed record Labor(long Id, long TicketId, long? UserId, int Minutes, string Body, DateTimeOffset CreatedAt);
    public sealed record Category(long Id, string Name);
    public sealed record Attribute(long Id, string Name, string Label);
    public sealed record AttributeValue(long TicketId, long AttributeId, string Value);

    public List<Ticket> Tickets { get; } = [];
    public List<Comment> Comments { get; } = [];
    public List<Change> Changes { get; } = [];
    public List<Labor> Labors { get; } = [];
    public List<Person> Technicians { get; } = [];
    public List<Person> EndUsers { get; } = [];
    public List<Category> Categories { get; } = [];
    public List<Attribute> Attributes { get; } = [];
    public List<AttributeValue> AttributeValues { get; } = [];
    public string? OrganizationName { get; private set; }

    // Rows that couldn't be read, and sheets that weren't there. Reading carries on past them.
    public List<string> Problems { get; } = [];

    // Null, with the reason, when the file isn't a Spiceworks export at all.
    public static (SpiceworksExport? Export, string? Error) Read(Stream stream)
    {
        SpreadsheetDocument document;
        try { document = SpreadsheetDocument.Open(stream, false); }
        catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException or IOException or ArgumentException or FormatException)
        {
            return (null, "That isn't an Excel workbook (.xlsx). Upload the file Spiceworks exported, as it is.");
        }
        using (document)
        {
            var workbook = new Workbook(document);
            if (!workbook.Has("Tickets")) return (null, "That workbook has no Tickets sheet, so it doesn't look like a Spiceworks export.");
            var export = new SpiceworksExport();
            export.Load(workbook);
            return (export, null);
        }
    }

    private void Load(Workbook book)
    {
        foreach (var row in book.Rows("Organizations", required: false, Problems)) OrganizationName ??= row.Text("name");
        foreach (var row in book.Rows("Users", required: false, Problems))
            if (row.Id("user_id") is { } id) Technicians.Add(new Person(id, row.Text("first_name"), row.Text("last_name"), row.Text("email")));
        foreach (var row in book.Rows("End Users", required: false, Problems))
            if (row.Id("end_user_id") is { } id) EndUsers.Add(new Person(id, row.Text("first_name"), row.Text("last_name"), row.Text("email")));
        foreach (var row in book.Rows("Ticket Categories", required: false, Problems))
            if (row.Id("ticket_category_id") is { } id) Categories.Add(new Category(id, row.Text("name")));
        foreach (var row in book.Rows("Custom Attributes", required: false, Problems))
            if (row.Id("custom_attribute_id") is { } id) Attributes.Add(new Attribute(id, row.Text("name"), row.Text("label") is { Length: > 0 } label ? label : row.Text("name")));
        foreach (var row in book.Rows("Tickets to Custom Attributes", required: false, Problems))
            if (row.Id("ticket_id") is { } ticket && row.Id("custom_attribute_id") is { } attribute && row.Text("value") is { Length: > 0 } value)
                AttributeValues.Add(new AttributeValue(ticket, attribute, value));

        foreach (var row in book.Rows("Tickets", required: true, Problems))
        {
            if (row.Id("ticket_id") is not { } id || row.Id("ticket_number") is not { } number || row.When("created_at") is not { } created)
            {
                Problems.Add($"Tickets row {row.Number}: no ticket id, number or created date, so it was left out.");
                continue;
            }
            Tickets.Add(new Ticket(id, (int)number, row.Text("summary"), row.Text("description"), row.Text("priority"), row.Text("status"),
                row.Id("ticket_category_id"), row.Id("creator_id"), row.Id("assignee_id"), row.Id("end_user_id"),
                created, row.When("updated_at"), row.When("last_closed_at"), row.When("due_at"), (int?)row.Id("master_ticket_number")));
        }
        foreach (var row in book.Rows("Ticket Comments", required: false, Problems))
        {
            if (row.Id("ticket_comment_id") is not { } id || row.Id("ticket_id") is not { } ticket || row.When("created_at") is not { } created) continue;
            // A comment deleted in Spiceworks stays deleted.
            if (row.Text("deleted") == "true" || row.Text("deleted_at").Length > 0) continue;
            Comments.Add(new Comment(id, ticket, row.Text("body"), row.Text("private") == "true", row.Id("creator_id"), row.Id("end_user_id"), created));
        }
        foreach (var row in book.Rows("Ticket Changes", required: false, Problems))
            if (row.Id("ticket_change_id") is { } id && row.Id("ticket_id") is { } ticket && row.When("created_at") is { } created)
                Changes.Add(new Change(id, ticket, row.Text("action"), row.Text("body"), row.Id("creator_id"), created));
        foreach (var row in book.Rows("Labors", required: false, Problems))
            if (row.Id("labor_id") is { } id && row.Id("ticket_id") is { } ticket && row.When("created_at") is { } created)
                Labors.Add(new Labor(id, ticket, row.Id("user_id"), (int)(row.Id("duration") ?? 0), row.Text("body"), created));
    }

    // ---- Reading the workbook ----

    private sealed class Workbook
    {
        private readonly WorkbookPart _part;
        private readonly List<string> _strings;
        private readonly Dictionary<string, WorksheetPart> _sheets = new(StringComparer.OrdinalIgnoreCase);

        public Workbook(SpreadsheetDocument document)
        {
            _part = document.WorkbookPart ?? throw new InvalidDataException("no workbook");
            _strings = _part.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().Select(x => x.InnerText).ToList() ?? [];
            foreach (var sheet in _part.Workbook?.Sheets?.Elements<Sheet>() ?? [])
                if (sheet.Name?.Value is { } name && sheet.Id?.Value is { } id && _part.GetPartById(id) is WorksheetPart part) _sheets[name] = part;
        }

        public bool Has(string sheet) => _sheets.ContainsKey(sheet);

        public IEnumerable<SheetRow> Rows(string sheet, bool required, List<string> problems)
        {
            if (!_sheets.TryGetValue(sheet, out var part))
            {
                if (required) problems.Add($"There is no {sheet} sheet.");
                yield break;
            }
            Dictionary<string, int>? columns = null;
            foreach (var row in part.Worksheet?.Descendants<Row>() ?? [])
            {
                var cells = new Dictionary<int, string>();
                foreach (var cell in row.Elements<Cell>())
                    if (cell.CellReference?.Value is { } reference) cells[Column(reference)] = Value(cell);
                if (columns is null)
                {
                    columns = cells.Where(x => x.Value.Length > 0).GroupBy(x => x.Value.Trim(), StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First().Key, StringComparer.OrdinalIgnoreCase);
                    continue;
                }
                if (cells.Values.All(x => x.Length == 0)) continue;
                yield return new SheetRow((int)(row.RowIndex?.Value ?? 0), columns, cells);
            }
        }

        private string Value(Cell cell)
        {
            var raw = cell.CellValue?.Text ?? "";
            if (cell.DataType?.Value == CellValues.SharedString) return int.TryParse(raw, out var index) && index < _strings.Count ? _strings[index] : "";
            if (cell.DataType?.Value == CellValues.InlineString) return cell.InlineString?.InnerText ?? "";
            if (cell.DataType?.Value == CellValues.Boolean) return raw == "1" ? "true" : "false";
            return raw;
        }

        private static int Column(string reference)
        {
            var number = 0;
            foreach (var ch in reference.TakeWhile(char.IsLetter)) number = number * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
            return number - 1;
        }
    }

    private sealed record SheetRow(int Number, Dictionary<string, int> Columns, Dictionary<int, string> Cells)
    {
        public string Text(string column) => Columns.TryGetValue(column, out var index) && Cells.TryGetValue(index, out var value) ? value.Replace("\r\n", "\n").Trim() : "";

        // Ids and numbers: Excel may hold them as numbers ("702554" or "702554.0") or as text.
        public long? Id(string column) =>
            decimal.TryParse(Text(column), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) && value == Math.Floor(value) ? (long)value : null;

        public DateTimeOffset? When(string column) =>
            DateTimeOffset.TryParse(Text(column), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at : null;
    }
}
