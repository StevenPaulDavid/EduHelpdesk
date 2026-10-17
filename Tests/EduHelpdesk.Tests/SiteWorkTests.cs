namespace EduHelpdesk.Tests;

// Stage 3 of desks: a desk's own working hours for its SLA clocks, jobs logged after they were dealt with, and what jobs
// cost.
public class SiteWorkTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly DayOfWeek[] Weekdays = SlaClock.DefaultSchoolDays;
    private static readonly SchoolPeriod[] Shifts = [new("Morning", new(6, 0), new(9, 0)), new("Afternoon", new(15, 0), new(18, 0))];
    // A Wednesday.
    private static DateTime At(int hour, int minute = 0, int day = 15) => new(2026, 10, day, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public void Minutes_and_hours_count_only_inside_a_desks_working_windows()
    {
        // Inside a window: plain time.
        Assert.Equal(At(7, 30), SlaClock.Due(At(7, 0), 30, SlaUnits.Minutes, Weekdays, Shifts, Utc, windowed: true));
        // Near the end of the day: the rest carries over to the next morning's window.
        Assert.Equal(At(6, 25, 16), SlaClock.Due(At(17, 55), 30, SlaUnits.Minutes, Weekdays, Shifts, Utc, windowed: true));
        // Between windows the clock waits for the next one: 2 hours from 11:00 is 17:00.
        Assert.Equal(At(17, 0), SlaClock.Due(At(11, 0), 2, SlaUnits.Hours, Weekdays, Shifts, Utc, windowed: true));
        // Friday evening to Monday morning.
        Assert.Equal(At(7, 0, 19), SlaClock.Due(At(19, 0, 16), 1, SlaUnits.Hours, Weekdays, Shifts, Utc, windowed: true));
        // Without windows, plain clock time as before.
        Assert.Equal(At(18, 25), SlaClock.Due(At(17, 55), 30, SlaUnits.Minutes, Weekdays, Shifts, Utc));
        // A pause inside a window comes back; one outside doesn't.
        Assert.Equal(At(8, 30), SlaClock.Extend(At(8, 0), At(7, 0), At(7, 30), SlaUnits.Minutes, Weekdays, Shifts, Utc, windowed: true));
        Assert.Equal(At(8, 0), SlaClock.Extend(At(8, 0), At(10, 0), At(12, 0), SlaUnits.Minutes, Weekdays, Shifts, Utc, windowed: true));
    }

    [Fact]
    public void A_desk_with_its_own_hours_works_its_tickets_out_in_them()
    {
        using var test = new TestStore();
        var store = test.Store;
        Assert.True(store.AddDesk("Cleaning", "CLN", "").Ok);
        var cleaning = store.Desks.Single(x => x.Name == "Cleaning");
        store.AddTicketOption("Category", "Spill", cleaning.Id);
        Assert.False(store.SetDeskHours(cleaning.Id, true, [], "06:00-09:00").Ok);
        Assert.False(store.SetDeskHours(cleaning.Id, true, Weekdays, "09:00-06:00").Ok);
        Assert.False(store.SetDeskHours(cleaning.Id, true, Weekdays, "06:00-09:00, 08:00-10:00").Ok);
        Assert.False(store.SetDeskHours(cleaning.Id, true, Weekdays, "breakfast").Ok);
        Assert.True(store.SetDeskHours(cleaning.Id, true, Weekdays, "15:00-18:00, 06:00-09:00").Ok);
        Assert.Equal("06:00-09:00, 15:00-18:00", HelpdeskStore.DescribeWindows(store.Desks.Single(x => x.Id == cleaning.Id).WorkWindows));

        store.AddSla("Spills", 30, SlaUnits.Minutes, null, [], ["Spill"]);
        var sla = store.Slas.Single(x => x.Name == "Spills").Id;
        var friday = new DateTime(2026, 10, 16, 21, 0, 0, DateTimeKind.Local).ToUniversalTime();
        var due = store.CalculateDueDate(sla, friday, "Spill")!.Value.ToLocalTime();
        Assert.Equal(new DateTime(2026, 10, 19, 6, 30, 0), due);
        // The same SLA on a category of a desk that follows the school day is plain clock time.
        Assert.Equal(friday.AddMinutes(30), store.CalculateDueDate(sla, friday, "Hardware"));

        var reopened = test.Reopen();
        Assert.True(reopened.Desks.Single(x => x.Id == cleaning.Id).HasOwnHours);
        Assert.Equal(Weekdays.Order(), reopened.Desks.Single(x => x.Id == cleaning.Id).WorkDays.Order());
        Assert.True(reopened.SetDeskHours(cleaning.Id, false, null, null).Ok);
        Assert.False(reopened.Desks.Single(x => x.Id == cleaning.Id).HasOwnHours);
        Assert.Null(reopened.CompareDatabaseWithMemory());
    }

    [Fact]
    public void A_job_already_dealt_with_is_saved_closed_and_on_the_right_desk()
    {
        using var test = new TestStore();
        var store = test.Store;
        Assert.True(store.AddDesk("Site", "SITE", "").Ok);
        var site = store.Desks.Single(x => x.Name == "Site");
        store.AddTicketOption("Category", "Heating", site.Id);
        store.AddTechnicianTeam("Caretakers", site.Id);
        store.AddTechnician(new TechnicianRecord(Guid.NewGuid(), "Sam Hughes", "sam@test.example", "Caretakers"));
        var caretaker = store.Technicians.Single(x => x.Name == "Sam Hughes");
        var admin = store.Technicians.First(x => x.Role == StaffRoles.Administrator);
        var when = DateTime.Now.AddHours(-2);

        Assert.NotNull(store.LogDoneJob(admin.Id, new("Heating", "", null, null, caretaker.Id, when)).Error);
        Assert.NotNull(store.LogDoneJob(admin.Id, new("Nonsense", "Radiator bled", null, null, caretaker.Id, when)).Error);
        Assert.NotNull(store.LogDoneJob(admin.Id, new("Heating", "Radiator bled", null, null, caretaker.Id, DateTime.Now.AddDays(1))).Error);
        // The demo IT technician isn't on the Site desk.
        Assert.NotNull(store.LogDoneJob(admin.Id, new("Heating", "Radiator bled", null, null, store.Technicians.First(x => x.Name.Contains("(demo)")).Id, when)).Error);

        var (number, error) = store.LogDoneJob(admin.Id, new("Heating", "Radiator bled in the hall", "Took ten minutes.", null, caretaker.Id, when));
        Assert.Null(error);
        var ticket = store.Tickets.Single(x => x.Number == number);
        Assert.True(TicketInsights.IsClosed(ticket));
        Assert.Equal(caretaker.Id, ticket.TechnicianId);
        Assert.Equal("Caretakers", ticket.TeamName);
        Assert.Null(ticket.DueDate);
        Assert.Equal(when.ToUniversalTime(), ticket.CreatedAt, TimeSpan.FromSeconds(1));
        Assert.Equal("SITE-1", store.Ref(ticket));
        Assert.Contains(ticket.History, x => x.Details.Contains("Logged after it was dealt with by Sam Hughes"));
        Assert.Null(store.CompareDatabaseWithMemory());
    }

    [Fact]
    public void Cost_lines_add_up_follow_merges_and_survive_a_restart()
    {
        using var test = new TestStore();
        var store = test.Store;
        Assert.Contains("Reactive maintenance", store.BudgetCodes);
        var requester = test.AddRequester("Priya Shah");
        var job = test.AddTicket(requester.Id);
        var other = test.AddTicket(requester.Id);

        Assert.False(store.AddTicketCost(job, new(null, "", null, "10", null, "")).Ok);
        Assert.False(store.AddTicketCost(job, new(null, "Call-out", null, "ten", null, "")).Ok);
        Assert.False(store.AddTicketCost(job, new(null, "Call-out", null, "-5", null, "")).Ok);
        Assert.False(store.AddTicketCost(job, new(null, "Call-out", null, "10", null, "Made up")).Ok);
        Assert.True(store.AddTicketCost(job, new(new DateOnly(2026, 10, 1), "Plumber call-out", null, "£1,200.505", "240", "reactive maintenance")).Ok);
        Assert.True(store.AddTicketCost(other, new(null, "Washers", null, "3.20", "", "")).Ok);

        var line = Assert.Single(store.CostsFor(job));
        Assert.Equal((1200.51m, 240m, 1440.51m, "Reactive maintenance"), (line.Net, line.Vat, line.Gross, line.BudgetCode));
        // The ticket's history says a cost line came, not what it cost - that is for people who can see costs.
        Assert.Contains(store.Tickets.Single(x => x.Number == job).History, x => x.Action == "Cost added" && !x.Details.Contains("1,200"));

        // A budget code in use can't go, and renaming one follows through.
        Assert.Contains("cannot be deleted", store.DeleteManagedOption("BudgetCodes", "Reactive maintenance"));
        store.UpdateManagedOption("BudgetCodes", "Reactive maintenance", "Reactive repairs");
        Assert.Equal("Reactive repairs", store.CostsFor(job)[0].BudgetCode);

        Assert.Null(store.MergeTicket(other, job));
        Assert.Equal(2, store.CostsFor(job).Count);
        Assert.Null(store.CompareDatabaseWithMemory());

        store = test.Reopen();
        Assert.Equal(1443.71m, store.CostsFor(job).Sum(x => x.Gross));
        Assert.True(store.RemoveTicketCost(job, store.CostsFor(job).First(x => x.Description == "Washers").Id).Ok);
        Assert.Null(store.DeleteTicket(job));
        Assert.Empty(store.TicketCosts);
        Assert.Null(store.CompareDatabaseWithMemory());
    }
}
