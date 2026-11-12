namespace EduHelpdesk.Tests;

// The other dates the planner shows (planner stage 4, HelpdeskStore.PlannerDates): recurring work still to come,
// cleaning rounds, loans due back, new starters, contract dates and asset checks, each on its own day.
public class PlannerDatesTests
{
    // The week of Monday 16 November 2026, seen at 10:00 on the Monday.
    private static readonly DateOnly Monday = new(2026, 11, 16);
    private static readonly DateOnly Sunday = Monday.AddDays(6);
    private static readonly DateTime Now = Monday.ToDateTime(new TimeOnly(10, 0));

    private static HelpdeskStore.RecurringTicketInput Schedule(HelpdeskStore store, Guid requesterId, Guid? technicianId = null, bool active = true, string name = "Backup check") =>
        new(name, active, "Check last night's backup", "Look at the log.", TicketTypes.Request, store.Categories[0], store.Priorities[0],
            requesterId, technicianId, null, null, null, Recurrence.Weekly, 1, new DateOnly(2026, 10, 12), null, new TimeOnly(7, 0),
            [DayOfWeek.Monday, DayOfWeek.Wednesday], 1, 0, DayOfWeek.Monday, 9, false);

    [Fact]
    public void Recurring_work_shows_only_what_is_still_to_be_logged_in_its_technicians_row()
    {
        using var test = new TestStore();
        var store = test.Store;
        var requester = test.AddRequester("Office");
        var sam = new TechnicianRecord(Guid.NewGuid(), "Sam Okoro", "sam@school.test", store.TechnicianTeams[0]);
        store.AddTechnician(sam);
        Assert.True(store.SaveRecurringTicket(null, Schedule(store, requester.Id, sam.Id), Monday.ToDateTime(new TimeOnly(6, 0))).Ok);
        Assert.True(store.SaveRecurringTicket(null, Schedule(store, requester.Id, active: false, name: "Paused"), Monday.ToDateTime(new TimeOnly(6, 0))).Ok);

        // Monday's 07:00 has been and gone by 10:00; Wednesday's is still to come. The paused one logs nothing.
        var recurring = store.PlannerDates(Monday, Sunday, Now).Where(x => x.Kind == PlannerKinds.Recurring).ToList();
        var wednesday = Assert.Single(recurring);
        Assert.Equal(Monday.AddDays(2), wednesday.Date);
        Assert.Equal("To log: Check last night's backup", wednesday.Text);
        Assert.True(wednesday.ForPerson);
        Assert.Equal(sam.Id, wednesday.TechnicianId);
        Assert.Equal(store.Desks[0].Id, wednesday.DeskId);

        // Early on the Monday, before the run, Monday's is still to come too.
        Assert.Equal(2, store.PlannerDates(Monday, Sunday, Monday.ToDateTime(new TimeOnly(6, 30))).Count(x => x.Kind == PlannerKinds.Recurring));
    }

    [Fact]
    public void Rounds_loans_starters_contracts_and_asset_checks_fall_on_their_days()
    {
        using var test = new TestStore();
        var store = test.Store;

        store.AddManagedOption("Locations", "Hall");
        Assert.True(store.SaveCleaningRound(null, "Hall round", ["Hall"], [DayOfWeek.Tuesday], "", true, new DateOnly(2026, 11, 1)).Ok);

        // The demo data has a kit and a loan of its own, so these are named apart from it.
        Assert.True(store.AddLoanKit("Planner kit", null, null).Ok);
        var kit = store.LoanKits.Single(x => x.Name == "Planner kit");
        Assert.True(store.IssueKit(kit.Id, null, "Supply teacher", store.LoanReasons[0], Monday.AddDays(3), "Tess", null).Ok);

        var template = store.OnboardingTemplates.Single(x => x.Name == "Teacher");
        var starter = store.StartOnboarding(new HelpdeskStore.OnboardingDetails("Jane Smith", null, "Teacher of Science", null, null, Monday.AddDays(4), null), template.Id, null);
        Assert.True(starter.Ok, starter.Message);

        var renewal = Monday.AddDays(5);
        Assert.True(store.AddContract(new ContractRecord(Guid.Empty, "Microsoft 365", DateTime.UtcNow) { NextRenewalDate = renewal, EndDate = renewal }).Ok);
        Assert.True(store.AddContract(new ContractRecord(Guid.Empty, "Old filtering", DateTime.UtcNow) { NextRenewalDate = renewal, Status = ContractStatusDefaults.Expired }).Ok);
        Assert.True(store.AddContract(new ContractRecord(Guid.Empty, "Broadband", DateTime.UtcNow) { NextRenewalDate = Monday.AddMonths(1).AddDays(2), NoticeMonths = 1 }).Ok);

        AssetRecord Laptop(string tag) => new(Guid.NewGuid(), tag, "Dell", "Latitude 5440", "Laptop", "SN-" + tag, "", null);
        store.AddAsset(Laptop("LT-1") with { NextCheckDate = Monday.AddDays(2) });
        store.AddAsset(Laptop("LT-2") with { NextCheckDate = Monday.AddDays(2) });
        store.AddAsset(Laptop("LT-3") with { NextCheckDate = Monday.AddDays(2), Status = HelpdeskStore.DisposedStatus });
        store.AddAsset(Laptop("LT-4") with { EndOfSupport = Monday.AddDays(3) });

        var dates = store.PlannerDates(Monday, Sunday, Now);
        string On(int day, string kind) => string.Join(" | ", dates.Where(x => x.Date == Monday.AddDays(day) && x.Kind == kind).Select(x => x.Text));

        Assert.Equal("Round: Hall round", On(1, PlannerKinds.Rounds));
        Assert.Contains("Back: Planner kit", On(3, PlannerKinds.Loans));
        Assert.Equal("Starts: Jane Smith", On(4, PlannerKinds.Starters));
        Assert.Equal(starter.Number, dates.Single(x => x.Kind == PlannerKinds.Starters).Number);
        // The renewal and the end on the same day are one line; an expired contract isn't on the planner.
        Assert.Equal("Renews: Microsoft 365", On(5, PlannerKinds.Contracts));
        Assert.Equal("Notice by: Broadband", On(2, PlannerKinds.Contracts));
        // Two checks the same day are one line; a disposed asset has none.
        Assert.Equal("2 asset checks due", On(2, PlannerKinds.Checks));
        Assert.Equal(2, dates.Single(x => x.Kind == PlannerKinds.Checks && x.Date == Monday.AddDays(2)).Count);
        Assert.Equal("Support ends: LT-4", On(3, PlannerKinds.Checks));

        // Nothing from the week before or after.
        Assert.All(dates, x => Assert.InRange(x.Date, Monday, Sunday));
        Assert.Empty(store.PlannerDates(Sunday, Monday, Now));
    }

    [Fact]
    public void A_loan_or_check_already_late_is_marked()
    {
        using var test = new TestStore();
        var store = test.Store;
        Assert.True(store.AddLoanKit("Planner iPads", null, null).Ok);
        Assert.True(store.IssueKit(store.LoanKits.Single(x => x.Name == "Planner iPads").Id, null, "Year 9", store.LoanReasons[0], Monday.AddDays(-2), "Tess", null).Ok);
        store.AddAsset(new AssetRecord(Guid.NewGuid(), "SW-1", "HP", "Aruba", "Networking", "SN-SW1", "", null) { NextCheckDate = Monday.AddDays(-3) });

        var dates = store.PlannerDates(Monday.AddDays(-7), Sunday, Now);
        Assert.True(dates.Single(x => x.Text == "Back: Planner iPads").Warning);
        Assert.True(dates.Single(x => x.Text == "Check due: SW-1").Warning);
    }
}
