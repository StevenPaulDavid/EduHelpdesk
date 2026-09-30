using Microsoft.Extensions.Configuration;

namespace EduHelpdesk.Tests;

// Saving only what changed (HelpdeskStore.SaveChanges). The database after each change is checked against what a full
// rewrite of the same data would hold - every row, and each ticket's, asset's or project's rows in the same order - so
// a change the comparison missed shows up as a difference, not as data that quietly disappears after a restart.
public class SaveChangesTests
{
    [Fact]
    public void A_change_in_every_module_saves_only_the_difference_and_leaves_the_database_matching_memory()
    {
        using var test = new TestStore();
        var store = test.Store;
        var today = DateOnly.FromDateTime(DateTime.Today);

        void Step(string what, Action change, bool mayRewriteEverything = false)
        {
            var before = test.Store.RecentSaveTimings().Count;
            change();
            var saves = test.Store.RecentSaveTimings().Skip(before).ToList();
            if (!mayRewriteEverything) Assert.True(saves.All(x => !x.FullRewrite), $"{what}: fell back to rewriting everything");
            Assert.Null(test.Store.CompareDatabaseWithMemory() is { } problem ? $"{what}: {problem}" : null);
        }

        var priya = test.AddRequester("Priya Shah");
        var sam = test.AddRequester("Sam Taylor");
        store.UpdateUser(sam with { CanRaiseProjects = true });
        var first = test.AddTicket(priya.Id, "Projector has no signal");
        var middle = test.AddTicket(priya.Id, "Printer jammed");
        var last = test.AddTicket(sam.Id, "Laptop won't charge");

        Step("comment on a ticket in the middle", () => store.AddTicketComment(middle, "Cleared the tray."));
        Step("internal note", () => store.AddTicketComment(middle, "Needs a new fuser.", isInternal: true));
        Step("second comment elsewhere", () => store.AddTicketComment(first, "Swapped the cable."));
        Step("remove the first of two comments", () =>
        {
            var comment = store.Tickets.Single(x => x.Number == middle).Comments[0];
            Assert.True(store.RemoveTicketComment(middle, HelpdeskStore.CommentKey(comment.CreatedAt)).Ok);
        });
        Step("edit a ticket", () => store.UpdateTicket(store.Tickets.Single(x => x.Number == first) with { Title = "Projector in B12 has no signal", Priority = "High" }));
        Step("close a ticket", () => store.UpdateTicket(store.Tickets.Single(x => x.Number == last) with { Status = "Closed", ClosedAt = DateTime.UtcNow }));

        var laptop = new AssetRecord(Guid.NewGuid(), "TST-1", "Dell", "Latitude 5440", "Laptop", "SN-1", "Room 1", null);
        Step("add an asset", () => store.AddAsset(laptop));
        Step("asset comment", () => store.AddAssetComment(laptop.Id, "Screen replaced."));
        Step("link asset to ticket", () => Assert.True(store.LinkAssetToTicket(laptop.Id, middle)));
        Step("loan it", () => Assert.True(store.LoanAsset(laptop.Id, priya.Id, today.AddDays(7), store.LoanReasons[0]).Ok));
        Step("return it", () => store.ReturnAsset(laptop.Id, null));
        Step("unlink", () => Assert.True(store.UnlinkAssetFromTicket(laptop.Id, middle)));

        Step("merge two tickets", () => Assert.Null(store.MergeTicket(first, middle)));
        Step("delete a ticket", () => Assert.Null(store.DeleteTicket(last)));

        var project = 0;
        Step("raise a project", () => project = store.RaiseProject(sam.Id, "Class set of iPads", today.AddDays(30), "30 iPads", [], null, 3).Number);
        Step("project note", () => Assert.True(store.AddProjectNote(project, "Quote requested.", isInternal: false).Ok));
        Step("project item", () => Assert.True(store.AddProjectItem(project, "iPad", 30).Ok));
        Step("remove the note", () =>
        {
            var note = store.FindProject(project)!.Notes.Single();
            Assert.True(store.RemoveProjectNote(project, HelpdeskStore.CommentKey(note.CreatedAt)).Ok);
        });

        var onboarding = 0;
        Step("start an onboarding", () =>
        {
            var template = store.OnboardingTemplates.Single(x => x.Name == "Teacher");
            var started = store.StartOnboarding(new HelpdeskStore.OnboardingDetails("Jane Smith", null, "Teacher of Science", null, null, today.AddDays(14), null), template.Id, null);
            Assert.True(started.Ok, started.Message);
            onboarding = started.Number;
        });
        Step("tick a task", () =>
        {
            var task = store.FindOnboarding(onboarding)!.Tasks.First(x => x.Action == OnboardingActions.None);
            Assert.True(store.SetOnboardingTaskDone(onboarding, task.Id, true).Ok);
        });
        Step("add a task", () => Assert.True(store.AddOnboardingTask(onboarding, "Order a lanyard", -2, OnboardingOwners.Officer).Ok));
        Step("remove a task from the middle", () =>
        {
            var task = store.FindOnboarding(onboarding)!.Tasks[3];
            Assert.True(store.RemoveOnboardingTask(onboarding, task.Id).Ok);
        });

        Step("add a category", () => store.AddTicketOption("Category", "Printers"));
        Step("rename it", () => store.UpdateTicketOption("Category", "Printers", "Printing"));
        Step("delete it", () => store.DeleteTicketOption("Category", "Printing"));
        // Technicians point at teams, and a rename changes a team's key in place - the one kind of change that still
        // rewrites everything. It must still come out right.
        Step("add a team", () => store.AddTechnicianTeam("Network"));
        Step("rename a team", () => store.UpdateTechnicianTeam("Network", "Networks"), mayRewriteEverything: true);
        Step("branding", () => store.UpdateBranding(new BrandingSettings { BrandName = "Test School", PrimaryColor = store.Branding.PrimaryColor }));
        Step("go live: remove the demo data", () => Assert.True(store.RemoveDemoData().Ok));

        // And the app sees it all after a restart.
        var reopened = test.Reopen();
        Assert.Null(reopened.CompareDatabaseWithMemory());
        Assert.Equal([$"(Merged from #{first}) Swapped the cable."], reopened.Tickets.Single(x => x.Number == middle).Comments.Select(x => x.Text).Where(x => x.Contains("Swapped")));
    }

    [Fact]
    public void Hundreds_of_random_ticket_changes_keep_the_database_matching_memory()
    {
        using var test = new TestStore();
        var random = new Random(20260930);
        var people = Enumerable.Range(1, 3).Select(i => test.AddRequester($"Person {i}")).ToList();
        var tickets = Enumerable.Range(1, 8).Select(i => test.AddTicket(people[i % 3].Id, $"Ticket {i}")).ToList();
        var asset = new AssetRecord(Guid.NewGuid(), "TST-9", "Dell", "Latitude 5440", "Laptop", "SN-9", "Room 1", null);
        test.Store.AddAsset(asset);

        for (var step = 1; step <= 400; step++)
        {
            var store = test.Store;
            var live = store.Tickets.Select(x => x.Number).Where(tickets.Contains).ToList();
            var number = live[random.Next(live.Count)];
            var ticket = store.Tickets.Single(x => x.Number == number);
            switch (random.Next(8))
            {
                case 0 or 1: store.AddTicketComment(number, $"Update {step}", isInternal: random.Next(3) == 0); break;
                case 2 when ticket.Comments.Count > 0:
                    store.RemoveTicketComment(number, HelpdeskStore.CommentKey(ticket.Comments[random.Next(ticket.Comments.Count)].CreatedAt)); break;
                case 3: store.UpdateTicket(ticket with { Title = $"Ticket {number} (step {step})" }); break;
                case 4: store.UpdateTicket(ticket with { Status = store.Statuses[random.Next(store.Statuses.Count)] }); break;
                case 5 when live.Count > 3: store.DeleteTicket(number); break;
                case 6: tickets.Add(test.AddTicket(people[random.Next(people.Count)].Id, $"New at step {step}")); break;
                case 7:
                    if (ticket.AssetIds.Contains(asset.Id)) store.UnlinkAssetFromTicket(asset.Id, number); else store.LinkAssetToTicket(asset.Id, number);
                    break;
            }
            if (step % 20 == 0) Assert.Null(test.Store.CompareDatabaseWithMemory() is { } problem ? $"after step {step}: {problem}" : null);
            // A restart halfway: the rest runs on a store that has to learn what the database holds all over again.
            if (step == 200) test.Reopen();
        }
        // Only the reopened store's startup save rewrote everything.
        Assert.Equal(1, test.Store.RecentSaveTimings().Count(x => x.FullRewrite));
    }

    [Fact]
    public void A_backup_holds_the_latest_change_and_a_factory_reset_still_saves_correctly()
    {
        using var test = new TestStore();
        var person = test.AddRequester("Priya Shah");
        var number = test.AddTicket(person.Id, "Backed up");
        // With write-ahead logging the newest changes sit in helpdesk.db-wal until SQLite folds them in; the backup
        // must include them anyway.
        test.Store.AddTicketComment(number, "Written just before the backup.");
        var (ok, message) = test.Store.CreateBackup(manual: true);
        Assert.True(ok, message);

        var extracted = Path.Combine(test.Root, "extracted");
        System.IO.Compression.ZipFile.ExtractToDirectory(test.Store.ListBackups()[0].FullPath, extracted);
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(extracted, "helpdesk.db")};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM TicketComments WHERE Text = 'Written just before the backup.';";
            Assert.Equal(1L, command.ExecuteScalar());
        }

        Assert.True(test.Store.ResetFactory("DELETE").Ok);
        Assert.Null(test.Store.CompareDatabaseWithMemory());
        test.Store.AddTicketComment(test.Store.Tickets[0].Number, "After the reset.");
        Assert.Null(test.Store.CompareDatabaseWithMemory());
        Assert.Null(test.Reopen().CompareDatabaseWithMemory());
    }

    [Fact]
    public void Moving_the_data_folder_keeps_changes_still_in_the_log_and_leaves_no_log_behind()
    {
        using var test = new TestStore();
        test.AddRequester("Moved Person");
        // A copy taken while the app has the database open - as a crash would leave it: the newest rows are only in
        // helpdesk.db-wal.
        var crashed = Path.Combine(test.Root, "crashed");
        Directory.CreateDirectory(Path.Combine(crashed, "App_Data"));
        foreach (var file in Directory.GetFiles(Path.Combine(test.Root, "App_Data"), "helpdesk.db*"))
            File.Copy(file, Path.Combine(crashed, "App_Data", Path.GetFileName(file)));
        Assert.True(File.Exists(Path.Combine(crashed, "App_Data", "helpdesk.db-wal")));

        var target = Path.Combine(test.Root, "moved");
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [DataLocation.ConfigKey] = target }).Build();
        var location = DataLocation.Resolve(configuration, new Folder(crashed));

        Assert.Equal(target, location.Folder);
        Assert.False(File.Exists(Path.Combine(crashed, "App_Data", "helpdesk.db-wal")));
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(target, "helpdesk.db")};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Users WHERE Name = 'Moved Person';";
        Assert.Equal(1L, command.ExecuteScalar());
    }

    private sealed class Folder(string root) : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "EduHelpdesk.Tests";
        public string ContentRootPath { get; set; } = root;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    [Fact]
    public void The_database_uses_write_ahead_logging()
    {
        using var test = new TestStore();
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={test.DatabasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        Assert.Equal("wal", command.ExecuteScalar() as string);
    }
}
