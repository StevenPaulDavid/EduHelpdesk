using System.IO.Compression;

namespace EduHelpdesk.Tests;

// Leavers, subject access and retention (Services/HelpdeskStore.Lifecycle.cs).
public class LifecycleTests
{
    private static AssetRecord Laptop(string tag, Guid? holder) =>
        new(Guid.NewGuid(), tag, "Dell", "Latitude 5440", "Laptop", "SN-" + tag, "Room 1", holder);

    [Fact]
    public void Marking_someone_inactive_records_when_they_left_and_reactivating_clears_it()
    {
        using var test = new TestStore();
        var person = test.AddRequester("Ava Stone");
        Assert.Null(person.LeftAt);

        test.Store.UpdateUser(person with { IsActive = false });
        var left = test.Store.Users.Single(x => x.Id == person.Id).LeftAt;
        Assert.NotNull(left);

        // Saving the form again later keeps the original date rather than restarting the clock.
        test.Store.UpdateUser(test.Store.Users.Single(x => x.Id == person.Id) with { Department = "Maths" });
        Assert.Equal(left, test.Reopen().Users.Single(x => x.Id == person.Id).LeftAt);

        test.Store.UpdateUser(test.Store.Users.Single(x => x.Id == person.Id) with { IsActive = true });
        Assert.Null(test.Store.Users.Single(x => x.Id == person.Id).LeftAt);
    }

    [Fact]
    public void A_leaver_check_finds_their_equipment_and_books_it_all_back_in()
    {
        using var test = new TestStore();
        var person = test.AddRequester("Noah Price");
        var laptop = Laptop("TST-1", person.Id);
        test.Store.AddAsset(laptop);
        test.AddTicket(person.Id);

        var holdings = test.Store.GetHoldings(person.Id)!;
        Assert.Single(holdings.Assets);
        Assert.Single(holdings.OpenTickets);
        Assert.True(holdings.HoldsAnything);

        Assert.True(test.Store.BookEverythingBackIn(person.Id).Ok);
        var asset = test.Reopen().Assets.Single(x => x.Id == laptop.Id);
        Assert.Null(asset.AssignedUserId);
        Assert.Equal("In stock or spare", asset.Status);
        Assert.Empty(test.Store.GetHoldings(person.Id)!.Assets);
    }

    [Fact]
    public void Retention_limits_are_checked()
    {
        using var test = new TestStore();
        Assert.StartsWith("Keep the audit log for at least", test.Store.SetRetention(0, 0, 6));
        Assert.StartsWith("Enter a number of months", test.Store.SetRetention(HelpdeskStore.MaxRetentionMonths + 1, 0, 0));
        Assert.StartsWith("Retention rules saved", test.Store.SetRetention(24, 12, 12));
        Assert.Equal(24, test.Reopen().Retention.TicketMonths);
        test.Store.SetRetention(0, 0, 0);
        Assert.False(test.Store.ApplyRetention().Ok);
    }

    [Fact]
    public void Retention_anonymises_long_gone_leavers_who_hold_nothing_and_leaves_the_rest()
    {
        using var test = new TestStore();
        var longAgo = DateTime.UtcNow.AddYears(-3);
        var gone = test.AddRequester("Ethan Baker", active: false, leftAt: longAgo);
        var stillHolding = test.AddRequester("Evie Clarke", active: false, leftAt: longAgo);
        var recent = test.AddRequester("Mia Ward", active: false, leftAt: DateTime.UtcNow.AddMonths(-1));
        var current = test.AddRequester("Leo Hart");
        test.Store.AddAsset(Laptop("TST-2", stillHolding.Id));
        var ticket = test.AddTicket(gone.Id, "Ethan Baker's laptop won't charge", status: "Closed", closedAt: DateTime.UtcNow.AddMonths(-2));

        test.Store.SetRetention(0, 12, 0);
        var preview = test.Store.PreviewRetention();
        Assert.Equal(1, preview.People);
        Assert.Equal(1, preview.LeaversWaiting);

        var (ok, summary) = test.Store.ApplyRetention();
        Assert.True(ok);
        Assert.DoesNotContain("Ethan", summary);

        var users = test.Reopen().Users;
        var anonymised = users.Single(x => x.Id == gone.Id);
        Assert.Equal(HelpdeskStore.AnonymisedName, anonymised.Name);
        Assert.DoesNotContain("ethan", anonymised.Email, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(anonymised.AnonymisedAt);
        Assert.Equal("Evie Clarke", users.Single(x => x.Id == stillHolding.Id).Name);
        Assert.Equal("Mia Ward", users.Single(x => x.Id == recent.Id).Name);
        Assert.Equal("Leo Hart", users.Single(x => x.Id == current.Id).Name);
        // Their ticket stays, so counts still add up, but their name is gone from it and from the audit log.
        Assert.DoesNotContain("Ethan", test.Store.Tickets.Single(x => x.Number == ticket).Title);
        Assert.DoesNotContain(test.Store.GetAuditEntries(), x => x.Entity.Contains("Ethan Baker") || x.Details.Contains("Ethan Baker"));
    }

    [Fact]
    public void Retention_deletes_old_closed_tickets_only()
    {
        using var test = new TestStore();
        var person = test.AddRequester("Ruby Lane");
        var old = test.AddTicket(person.Id, "Old", status: "Closed", createdAt: DateTime.UtcNow.AddYears(-3), closedAt: DateTime.UtcNow.AddYears(-3));
        var recentlyClosed = test.AddTicket(person.Id, "Recent", status: "Closed", closedAt: DateTime.UtcNow.AddDays(-3));
        var stillOpen = test.AddTicket(person.Id, "Open for ages", createdAt: DateTime.UtcNow.AddYears(-3));

        test.Store.SetRetention(24, 0, 0);
        Assert.True(test.Store.ApplyRetention().Ok);

        var numbers = test.Reopen().Tickets.Select(x => x.Number).ToList();
        Assert.DoesNotContain(old, numbers);
        Assert.Contains(recentlyClosed, numbers);
        Assert.Contains(stillOpen, numbers);
        // The number isn't handed out again.
        Assert.True(test.AddTicket(person.Id) > stillOpen);
    }

    [Fact]
    public void A_subject_access_export_holds_their_data_and_no_password()
    {
        using var test = new TestStore();
        var person = test.AddRequester("Oliver Kaur");
        test.Store.UpdateUser(person with { PasswordHash = PasswordHasher.Hash("Kettle-Planet-Fox") });
        var number = test.AddTicket(person.Id, "Can't log in");
        var other = test.AddRequester("Grace Lee");
        test.AddTicket(other.Id, "Oliver Kaur asked me to report his projector");

        var data = test.Store.GatherSubjectAccess(person.Id)!;
        Assert.Single(data.Tickets);
        Assert.Single(data.Mentions);

        using var zip = new ZipArchive(new MemoryStream(SubjectAccessExport.Build(data, "Test School")));
        var names = zip.Entries.Select(x => x.FullName).ToList();
        Assert.Contains("README.txt", names);
        Assert.Contains("report.html", names);
        Assert.Contains("data.json", names);
        var json = new StreamReader(zip.GetEntry("data.json")!.Open()).ReadToEnd();
        Assert.Contains("oliver.kaur@test.example", json);
        Assert.Contains($"{number}", json);
        Assert.DoesNotContain("PasswordHash", json);
        Assert.DoesNotContain(test.Store.Users.Single(x => x.Id == person.Id).PasswordHash!, json);
    }
}
