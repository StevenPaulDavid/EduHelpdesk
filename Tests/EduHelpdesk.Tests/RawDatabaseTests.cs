namespace EduHelpdesk.Tests;

// Settings → Database: the raw tables. It has to show everything as stored while keeping sign-in secrets from anyone
// but an Administrator - including through search and filters - and never take a table name it wasn't given by the
// database itself.
public class RawDatabaseTests
{
    private static RawDatabase.Query Everything => new(null, null, "equals", null, false);

    [Fact]
    public void Every_table_has_a_description()
    {
        using var test = new TestStore();
        var raw = new RawDatabase(test.Store);
        Assert.Empty(raw.Tables().Where(x => x.Description.Length == 0).Select(x => x.Name));
    }

    [Fact]
    public void Sign_in_secrets_are_hidden_from_everyone_but_an_Administrator_even_through_search_and_filters()
    {
        using var test = new TestStore();
        var raw = new RawDatabase(test.Store);
        var hash = test.Store.Technicians.First(x => x.PasswordHash is not null).PasswordHash!;

        var hidden = raw.Read("Technicians", Everything, showSecrets: false, page: 1)!;
        var column = hidden.Columns.ToList().FindIndex(x => x.Name == "PasswordHash");
        Assert.True(hidden.Columns[column].Secret);
        Assert.Contains(hidden.Rows, row => row[column] is RawDatabase.Hidden);
        Assert.DoesNotContain(hidden.Rows, row => Equals(row[column], hash));

        // A search is a way of guessing, so secret columns aren't searched for anyone who can't see them...
        Assert.Equal(0, raw.Read("Technicians", Everything with { Search = hash[..20] }, showSecrets: false, page: 1)!.Total);
        // ...nor filtered on.
        var filtered = raw.Read("Technicians", Everything with { Column = "PasswordHash", Operator = "contains", Value = hash[..20] }, showSecrets: false, page: 1)!;
        Assert.NotNull(filtered.Problem);
        Assert.Equal(test.Store.Technicians.Count, filtered.Total);

        var shown = raw.Read("technicians", Everything with { Search = hash[..20] }, showSecrets: true, page: 1)!;
        Assert.Equal(1, shown.Total);
        Assert.Contains(shown.Rows, row => Equals(row[column], hash));
    }

    [Fact]
    public void Only_real_tables_and_columns_are_read()
    {
        using var test = new TestStore();
        var raw = new RawDatabase(test.Store);
        Assert.Null(raw.Read("Tickets; DROP TABLE Tickets", Everything, false, 1));
        Assert.Null(raw.Read("NoSuchTable", Everything, false, 1));
        Assert.NotNull(raw.Read("Tickets", Everything with { Column = "Nope\" OR 1=1 --" }, false, 1)!.Problem);
        Assert.NotEmpty(test.Store.Tickets);
    }

    [Fact]
    public void A_ticket_s_rows_are_found_in_every_table_that_mentions_it_and_the_look_is_audited_once()
    {
        using var test = new TestStore();
        var person = test.AddRequester("Priya Shah");
        var number = test.AddTicket(person.Id, "Printer jammed");
        test.Store.AddTicketComment(number, "Cleared the tray.");
        var raw = new RawDatabase(test.Store);

        var found = raw.ForRecord(RawDatabase.FindKind("ticket")!, number.ToString(), showSecrets: false);
        Assert.Equal(["Tickets", "TicketComments"], found.Select(x => x.Table).Where(x => x is "Tickets" or "TicketComments"));
        Assert.Contains(found.Single(x => x.Table == "TicketComments").Rows, row => row.Contains("Cleared the tray."));

        // Every kind's places are real SQL against real tables.
        foreach (var kind in RawDatabase.Kinds) raw.ForRecord(kind, "x", showSecrets: false);

        raw.RecordLook("Viewed raw data", $"Ticket {number}", "2 rows");
        raw.RecordLook("Viewed raw data", $"Ticket {number}", "2 rows");
        Assert.Single(test.Store.GetAuditEntries(), x => x.Area == "Database" && x.Entity == $"Ticket {number}");
    }
}
