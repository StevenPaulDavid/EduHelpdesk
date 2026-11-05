namespace EduHelpdesk.Tests;

// Settings → Go live & reset: emptying one part of the helpdesk (HelpdeskStore.ModuleResets) and leaving the rest. A new
// store comes with demo tickets, assets, a kit, a part and a project, which most of these reset.
public class ModuleResetTests
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    private static int NewTicket(TestStore test, string title = "Projector flickers") =>
        test.Store.AddTicket(new TicketRecord(0, title, "Then nothing.", test.Store.Users[0].Id, [], null, "Normal", "Open", test.Store.Categories[0], DateTime.UtcNow, null));

    [Fact]
    public void Nothing_changes_without_the_module_name_typed()
    {
        using var test = new TestStore();
        var before = test.Store.Tickets.Count;
        Assert.True(before > 0);
        Assert.False(test.Store.ResetModule(HelpdeskStore.ResetModules.Tickets, "DELETE", keepBackup: false).Ok);
        Assert.False(test.Store.ResetModule(HelpdeskStore.ResetModules.Tickets, "", keepBackup: false).Ok);
        Assert.False(test.Store.ResetModule("nonsense", "Tickets", keepBackup: false).Ok);
        Assert.Equal(before, test.Store.Tickets.Count);
    }

    [Fact]
    public void Tickets_reset_keeps_the_set_up_and_numbers_from_1_again()
    {
        using var test = new TestStore();
        for (var i = 0; i < 3; i++) NewTicket(test, $"Ticket {i}");
        var highestKey = test.Store.Tickets.Max(x => x.Number);
        var templates = test.Store.TicketTemplates.Count;
        var categories = test.Store.Categories.Count;
        var parts = test.Store.Parts.Sum(x => x.QuantityOnHand);
        Assert.True(test.Store.ResetCounts()[HelpdeskStore.ResetModules.Tickets] >= 3);

        var (ok, message) = test.Store.ResetModule(HelpdeskStore.ResetModules.Tickets, " tickets ", keepBackup: true);
        Assert.True(ok, message);
        Assert.Empty(test.Store.Tickets);
        Assert.Equal(templates, test.Store.TicketTemplates.Count);
        Assert.Equal(categories, test.Store.Categories.Count);
        Assert.Equal(parts, test.Store.Parts.Sum(x => x.QuantityOnHand));
        Assert.Contains(test.Store.ListBackups(), x => x.Name.Contains("before-reset-tickets"));
        Assert.False(test.Store.HasDemoData && test.Store.DemoDataSummary().Any(x => x.Kind == "Ticket"));

        // The ticket's own number starts again; the key behind it never goes back, so old links can't open it.
        var number = NewTicket(test);
        Assert.True(number > highestKey);
        var ticket = test.Store.Tickets.Single();
        Assert.Equal(1, ticket.DeskNumber);

        test.Reopen();
        Assert.Single(test.Store.Tickets);
        Assert.Contains(test.Store.GetAuditEntries(), x => x.Action == "Tickets reset");
    }

    [Fact]
    public void Tickets_reset_takes_onboarding_records_with_their_tickets()
    {
        using var test = new TestStore();
        var (ok, _, _) = test.Store.StartOnboarding(new HelpdeskStore.OnboardingDetails("Sam Price", null, "Teacher", null, null, Today.AddDays(7), null), null, null);
        Assert.True(ok);
        Assert.NotEmpty(test.Store.Onboardings);
        Assert.True(test.Store.ResetModule(HelpdeskStore.ResetModules.Tickets, "Tickets", keepBackup: false).Ok);
        Assert.Empty(test.Store.Onboardings);
    }

    [Fact]
    public void Onboarding_reset_removes_only_new_starter_tickets()
    {
        using var test = new TestStore();
        var other = NewTicket(test);
        var (ok, _, starter) = test.Store.StartOnboarding(new HelpdeskStore.OnboardingDetails("Sam Price", null, "Teacher", null, null, Today.AddDays(7), null), null, null);
        Assert.True(ok);
        Assert.True(test.Store.ResetModule(HelpdeskStore.ResetModules.Onboarding, "Onboarding", keepBackup: false).Ok);
        Assert.Empty(test.Store.Onboardings);
        Assert.DoesNotContain(test.Store.Tickets, x => x.Number == starter);
        Assert.Contains(test.Store.Tickets, x => x.Number == other);
        Assert.Contains(test.Store.Users, x => x.Name == "Sam Price");
    }

    [Fact]
    public void Projects_reset_starts_again_at_PRJ_0001()
    {
        using var test = new TestStore();
        var person = test.Store.Users[0];
        Assert.True(test.Store.RaiseProject(person.Id, "Class set of iPads", Today.AddDays(30), "30 iPads", [], null, 3).Ok);
        Assert.True(test.Store.Projects.Count >= 1);
        Assert.True(test.Store.ResetModule(HelpdeskStore.ResetModules.Projects, "Projects", keepBackup: false).Ok);
        Assert.Empty(test.Store.Projects);
        var (ok, _, number) = test.Store.RaiseProject(person.Id, "Visualisers", Today.AddDays(30), "6 visualisers", [], null, 3);
        Assert.True(ok);
        Assert.Equal(1, number);
        test.Reopen();
        Assert.Equal("PRJ-0001", test.Store.Projects.Single().Reference);
    }

    [Fact]
    public void Assets_reset_unlinks_tickets_and_keeps_kits_and_contracts()
    {
        using var test = new TestStore();
        var asset = test.Store.Assets[0];
        var (contractOk, _, contractId) = test.Store.AddContract(new ContractRecord(Guid.Empty, "Laptop lease", DateTime.UtcNow));
        Assert.True(contractOk);
        Assert.True(test.Store.UpdateAsset(asset with { ContractId = contractId }));
        var number = test.Store.AddTicket(new TicketRecord(0, "Broken screen", "Cracked.", test.Store.Users[0].Id, [asset.Id], null, "Normal", "Open", test.Store.Categories[0], DateTime.UtcNow, null));
        var kits = test.Store.LoanKits.Count;
        Assert.True(kits > 0);

        Assert.True(test.Store.ResetModule(HelpdeskStore.ResetModules.Assets, "Assets", keepBackup: false).Ok);
        Assert.Empty(test.Store.Assets);
        Assert.Empty(test.Store.KitLoans);
        Assert.Equal(kits, test.Store.LoanKits.Count);
        Assert.All(test.Store.LoanKits, x => Assert.Empty(x.AssetIds));
        Assert.Empty(test.Store.Tickets.Single(x => x.Number == number).AssetIds);
        Assert.Contains(test.Store.Contracts, x => x.Id == contractId);
        test.Reopen();
        Assert.Empty(test.Store.Assets);
    }

    [Fact]
    public void Parts_contracts_and_access_reset_on_their_own()
    {
        using var test = new TestStore();
        Assert.NotEmpty(test.Store.Parts);
        var assets = test.Store.Assets.Count;
        var (_, _, contractId) = test.Store.AddContract(new ContractRecord(Guid.Empty, "Broadband", DateTime.UtcNow));
        Assert.True(test.Store.UpdateAsset(test.Store.Assets[0] with { ContractId = contractId }));
        var (_, _, system) = test.Store.SaveAccessResource(new AccessResource(Guid.Empty, "SIMS", DateTime.UtcNow));
        Assert.True(test.Store.AddAccessGrant(new AccessGrant(Guid.Empty, test.Store.Users[0].Id, system!.Value, DateTime.UtcNow)).Ok);

        Assert.True(test.Store.ResetModule(HelpdeskStore.ResetModules.Parts, "Parts", keepBackup: false).Ok);
        Assert.Empty(test.Store.Parts);
        Assert.NotEmpty(test.Store.Contracts);

        Assert.True(test.Store.ResetModule(HelpdeskStore.ResetModules.Contracts, "Contracts", keepBackup: false).Ok);
        Assert.Empty(test.Store.Contracts);
        Assert.Equal(assets, test.Store.Assets.Count);
        Assert.All(test.Store.Assets, x => Assert.Null(x.ContractId));
        Assert.NotEmpty(test.Store.AccessResources);

        Assert.True(test.Store.ResetModule(HelpdeskStore.ResetModules.Access, "Access", keepBackup: false).Ok);
        Assert.Empty(test.Store.AccessResources);
        Assert.Empty(test.Store.AccessGrants);
        test.Reopen();
        Assert.Empty(test.Store.Parts);
        Assert.Empty(test.Store.Contracts);
        Assert.Empty(test.Store.AccessGrants);
    }

    [Fact]
    public void Facilities_reset_clears_round_history_and_restarts_the_count_today()
    {
        using var test = new TestStore(facilities: true);
        var room = test.Store.Locations[0];
        Assert.True(test.Store.SaveCleaningRound(null, "Ground floor", [room], [.. Enum.GetValues<DayOfWeek>()], "", true, Today.AddDays(-6)).Ok);
        var round = test.Store.CleaningRounds.Single();
        Assert.True(test.Store.RecordRound(round.Id, Today.AddDays(-1), [new HelpdeskStore.VisitInput(room, true, null, null, null, null)]).Ok);
        Assert.NotEmpty(test.Store.RoundVisits);
        var other = NewTicket(test);

        Assert.True(test.Store.ResetModule(HelpdeskStore.ResetModules.Facilities, "Facilities", keepBackup: false).Ok);
        Assert.Empty(test.Store.RoundVisits);
        Assert.Equal(Today, test.Store.CleaningRounds.Single().StartedOn);
        Assert.Contains(test.Store.Tickets, x => x.Number == other);
    }
}
