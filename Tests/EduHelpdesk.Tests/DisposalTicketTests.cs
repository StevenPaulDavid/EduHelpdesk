using System.Security.Claims;
using EduHelpdesk.Pages;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Tests;

// Disposing of assets through a ticket (HelpdeskStore.Disposal): the tick marks the linked assets Awaiting disposal,
// other ways of closing are refused while they wait, the close pop-up disposes of them or puts them back, and a
// reopened ticket can undo what it disposed of.
public class DisposalTicketTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    private static AssetRecord Laptop(string tag, string status = "In use") =>
        new(Guid.NewGuid(), tag, "Dell", "Latitude 5440", "Laptop", "SN-" + tag, "Room 1", null) { Status = status };

    private static ClaimsPrincipal Principal(TechnicianRecord account) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()), new Claim(ClaimTypes.Name, account.Name), new Claim(ClaimTypes.Role, account.Role)], "Test"));

    private static T Signed<T>(T page, ClaimsPrincipal user) where T : PageModel
    {
        page.PageContext = new PageContext { HttpContext = new DefaultHttpContext { User = user } };
        return page;
    }

    // A ticket linking the given assets, ticked to dispose of them on close.
    private static int DisposalTicket(TestStore test, params AssetRecord[] assets)
    {
        foreach (var asset in assets) test.Store.AddAsset(asset);
        var requester = test.Store.Users.FirstOrDefault() ?? test.AddRequester("Ada Lovelace");
        return test.Store.AddTicket(new TicketRecord(0, "Dispose of old laptops", "", requester.Id, assets.Select(x => x.Id).ToList(), null,
            test.Store.Priorities[0], "Open", test.Store.Categories[0], DateTime.UtcNow, null) { DisposeOnClose = true });
    }

    private static AssetRecord Get(TestStore test, AssetRecord asset) => test.Store.Assets.Single(x => x.Id == asset.Id);

    [Fact]
    public void Ticked_tickets_hold_their_assets_and_letting_go_puts_each_status_back()
    {
        using var test = new TestStore();
        var (one, two) = (Laptop("TST-1"), Laptop("TST-2", "In stock or spare"));
        var number = DisposalTicket(test, one, two);

        Assert.All(new[] { one, two }, a =>
        {
            var now = Get(test, a);
            Assert.Equal(HelpdeskStore.AwaitingDisposalStatus, now.Status);
            Assert.Equal(number, now.AwaitingDisposalTicket);
        });
        Assert.Equal("In stock or spare", Get(test, two).StatusBeforeDisposal);

        // Unlinking one lets it go; unticking the ticket lets the other go.
        Assert.True(test.Store.UnlinkAssetFromTicket(two.Id, number));
        Assert.Equal(("In stock or spare", (int?)null), (Get(test, two).Status, Get(test, two).AwaitingDisposalTicket));
        Assert.True(test.Store.SetDisposeOnClose(number, false).Ok);
        Assert.Equal(("In use", (int?)null), (Get(test, one).Status, Get(test, one).AwaitingDisposalTicket));

        // Linking to a ticked ticket marks it; deleting the ticket lets it go.
        Assert.True(test.Store.SetDisposeOnClose(number, true).Ok);
        Assert.True(test.Store.LinkAssetToTicket(two.Id, number));
        Assert.Equal(number, Get(test, two).AwaitingDisposalTicket);
        Assert.Null(test.Store.DeleteTicket(number));
        Assert.All(new[] { one, two }, a => Assert.Null(Get(test, a).AwaitingDisposalTicket));
        Assert.Equal(["In use", "In stock or spare"], new[] { Get(test, one).Status, Get(test, two).Status });
        Assert.Null(test.Store.CompareDatabaseWithMemory());
    }

    [Fact]
    public void A_status_changed_by_hand_while_waiting_is_left_alone()
    {
        using var test = new TestStore();
        var asset = Laptop("TST-1");
        var number = DisposalTicket(test, asset);
        Assert.True(test.Store.UpdateAsset(Get(test, asset) with { Status = "In repair" }));
        Assert.True(test.Store.SetDisposeOnClose(number, false).Ok);
        Assert.Equal("In repair", Get(test, asset).Status);
    }

    [Fact]
    public void Every_other_way_of_closing_is_refused_while_assets_wait()
    {
        using var test = new TestStore();
        var number = DisposalTicket(test, Laptop("TST-1"));
        var other = test.AddTicket(test.Store.Users[0].Id);

        Assert.False(test.Store.CloseTicket(number, "Done").Ok);
        Assert.False(test.Store.SetTicketStatus(number, "Closed").Ok);
        Assert.False(test.Store.AddTicketReply(number, "All gone", false, false, "Closed").Ok);
        var bulk = test.Store.BulkUpdateTickets([number], new("close", Text: "Done"));
        Assert.Equal((0, 1), (bulk.Updated, bulk.Skipped));
        Assert.Equal(1, test.Store.BulkUpdateTickets([number], new("status", "Closed")).Skipped);
        Assert.NotNull(test.Store.MergeTicket(number, other));
        Assert.Equal("Open", test.Store.Tickets.Single(x => x.Number == number).Status);

        // With nothing waiting it closes like any other ticket.
        Assert.True(test.Store.SetDisposeOnClose(number, false).Ok);
        Assert.True(test.Store.CloseTicket(number, "Kept after all").Ok);
    }

    [Fact]
    public void Closing_with_disposal_writes_them_off_and_skips_one_still_on_loan()
    {
        using var test = new TestStore();
        var (one, two, lent) = (Laptop("TST-1"), Laptop("TST-2"), Laptop("TST-3"));
        test.Store.AddAsset(lent);
        Assert.True(test.Store.LoanAsset(lent.Id, test.AddRequester("Grace Hopper").Id, Today.AddDays(7), test.Store.LoanReasons[0]).Ok);
        var number = DisposalTicket(test, one, two);
        Assert.True(test.Store.LinkAssetToTicket(lent.Id, number));
        Assert.Equal(number, Get(test, lent).AwaitingDisposalTicket);
        Assert.True(test.Store.SaveDisposalDetails(number, "Recycled (WEEE)", "GreenTech", "", null).Ok);

        var result = test.Store.CloseDisposalTicket(number, "Collected by GreenTech", true, Today, "Recycled (WEEE)", "GreenTech", "WEEE-42", 12.5m);
        Assert.True(result.Ok, result.Message);
        Assert.Contains("TST-3", result.Message);

        foreach (var asset in new[] { one, two })
        {
            var now = Get(test, asset);
            Assert.True(HelpdeskStore.IsDisposed(now));
            Assert.Equal((Today, "Recycled (WEEE)", "GreenTech", "WEEE-42", (decimal?)12.5m, (int?)number, (int?)null),
                (now.DisposalDate!.Value, now.DisposalMethod, now.DisposedBy, now.DisposalCertificate, now.DisposalProceeds, now.DisposalTicket, now.AwaitingDisposalTicket));
        }
        // The one still out goes back to being on loan, and the ticket says so in an internal note.
        Assert.Equal(("On loan", (int?)null), (Get(test, lent).Status, Get(test, lent).AwaitingDisposalTicket));
        var ticket = test.Store.Tickets.Single(x => x.Number == number);
        Assert.Equal("Closed", ticket.Status);
        Assert.Contains(ticket.Comments, c => c.IsInternal && c.Text.Contains("TST-1, TST-2") && c.Text.Contains("TST-3"));
        Assert.Contains(ticket.Comments, c => !c.IsInternal && c.Text == "Collected by GreenTech");
        Assert.Equal("WEEE-42", ticket.DisposalCertificate);
        Assert.Null(test.Store.CompareDatabaseWithMemory());
    }

    [Fact]
    public void Closing_without_disposing_puts_them_back()
    {
        using var test = new TestStore();
        var asset = Laptop("TST-1", "In stock or spare");
        var number = DisposalTicket(test, asset);
        var result = test.Store.CloseDisposalTicket(number, null, false, null, null, null, null, null);
        Assert.True(result.Ok, result.Message);
        Assert.Equal(("In stock or spare", false), (Get(test, asset).Status, HelpdeskStore.IsDisposed(Get(test, asset))));
        Assert.Equal("Closed", test.Store.Tickets.Single(x => x.Number == number).Status);
    }

    [Fact]
    public void Disposing_needs_a_method_and_no_future_date()
    {
        using var test = new TestStore();
        var number = DisposalTicket(test, Laptop("TST-1"));
        Assert.False(test.Store.CloseDisposalTicket(number, null, true, Today, "", null, null, null).Ok);
        Assert.False(test.Store.CloseDisposalTicket(number, null, true, Today.AddDays(1), "Written off", null, null, null).Ok);
        Assert.Equal("Open", test.Store.Tickets.Single(x => x.Number == number).Status);
    }

    [Fact]
    public void A_reopened_ticket_can_undo_its_disposal_and_dispose_again()
    {
        using var test = new TestStore();
        var asset = Laptop("TST-1", "In stock or spare");
        var number = DisposalTicket(test, asset);
        Assert.True(test.Store.CloseDisposalTicket(number, null, true, Today, "Written off", null, null, null).Ok);
        Assert.False(test.Store.RestoreDisposedThrough(number).Ok); // still closed

        Assert.True(test.Store.SetTicketStatus(number, "Open").Ok);
        Assert.Single(test.Store.DisposedThrough(number));
        Assert.True(test.Store.RestoreDisposedThrough(number).Ok);
        var back = Get(test, asset);
        Assert.Equal((HelpdeskStore.AwaitingDisposalStatus, (int?)number, (DateOnly?)null, "", (int?)null, "In stock or spare"),
            (back.Status, back.AwaitingDisposalTicket, back.DisposalDate, back.DisposalMethod, back.DisposalTicket, back.StatusBeforeDisposal));

        // Closing again without disposing puts it back to what it was before any of this.
        Assert.True(test.Store.CloseDisposalTicket(number, null, false, null, null, null, null, null).Ok);
        Assert.Equal("In stock or spare", Get(test, asset).Status);
        Assert.Null(test.Store.CompareDatabaseWithMemory());
    }

    [Fact]
    public void Waiting_assets_cannot_be_lent_put_in_a_kit_or_given_to_a_student()
    {
        using var test = new TestStore();
        var asset = Laptop("TST-1");
        DisposalTicket(test, asset);
        Assert.Contains("waiting to be disposed of", test.Store.LoanAsset(asset.Id, test.Store.Users[0].Id, Today.AddDays(3), test.Store.LoanReasons[0]).Message);
        Assert.False(test.Store.AddLoanKit("Spare kit", null, [asset.Id]).Ok);
        Assert.Contains("waiting to be disposed of", test.Store.GiveToStudent(asset.Id, "Amira Khan").Message);
    }

    [Fact]
    public void Everything_survives_a_restart()
    {
        using var test = new TestStore();
        var (asset, gone) = (Laptop("TST-1", "In repair"), Laptop("TST-2"));
        var number = DisposalTicket(test, asset);
        var other = DisposalTicket(test, gone);
        Assert.True(test.Store.SaveDisposalDetails(number, "Donated", "Local charity", "CERT-1", 5m).Ok);
        Assert.True(test.Store.CloseDisposalTicket(other, null, true, Today, "Sold", "eBay", null, 80m).Ok);

        var store = test.Reopen();
        var ticket = store.Tickets.Single(x => x.Number == number);
        Assert.Equal((true, "Donated", "Local charity", "CERT-1", (decimal?)5m), (ticket.DisposeOnClose, ticket.DisposalMethod, ticket.DisposalBy, ticket.DisposalCertificate, ticket.DisposalProceeds));
        var waiting = store.Assets.Single(x => x.Id == asset.Id);
        Assert.Equal(((int?)number, "In repair"), (waiting.AwaitingDisposalTicket, waiting.StatusBeforeDisposal));
        Assert.Equal((int?)other, store.Assets.Single(x => x.Id == gone.Id).DisposalTicket);
        Assert.Null(store.CompareDatabaseWithMemory());
    }

    [Fact]
    public void The_status_cannot_be_renamed_and_new_roles_get_the_tick_where_they_dispose_today()
    {
        using var test = new TestStore();
        Assert.Contains("can't be renamed", test.Store.UpdateManagedOption("Asset status", HelpdeskStore.AwaitingDisposalStatus, "Going"));
        var roles = test.Store.Roles.ToDictionary(x => x.Name);
        Assert.True(roles["Senior Technician"].Has(Modules.Flags.DisposeAssets));
        Assert.True(roles["Technician"].Has(Modules.Flags.DisposeAssets));
        Assert.False(roles["Junior Technician"].Has(Modules.Flags.DisposeAssets));
    }

    [Fact]
    public void An_upgrade_gives_the_tick_to_roles_that_could_delete_assets()
    {
        using var test = new TestStore();
        // As a database from before the tick: no role holds it and the version key is 0.
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={test.DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM RolePermissions WHERE Permission = 'Assets.Dispose'; UPDATE Metadata SET Value = '0' WHERE Key = 'DisposeFlagVersion';";
            command.ExecuteNonQuery();
        }
        var store = test.Reopen();
        var roles = store.Roles.ToDictionary(x => x.Name);
        Assert.True(roles["Technician"].Has(Modules.Flags.DisposeAssets));
        Assert.False(roles["Junior Technician"].Has(Modules.Flags.DisposeAssets));

        // Once done it isn't redone: a tick taken off by hand stays off.
        var technician = roles["Technician"];
        var without = technician with { Flags = technician.Flags.Where(x => x != Modules.Flags.DisposeAssets).ToHashSet(StringComparer.OrdinalIgnoreCase), Grants = new(technician.Grants, StringComparer.OrdinalIgnoreCase) };
        Assert.True(store.UpdateRole("Technician", without).Ok);
        Assert.False(test.Reopen().Roles.Single(x => x.Name == "Technician").Has(Modules.Flags.DisposeAssets));
    }

    [Fact]
    public void Dispose_via_ticket_opens_the_form_ticked_and_titled()
    {
        using var test = new TestStore();
        var admin = test.Store.Technicians.First(x => x.Role == StaffRoles.Administrator);
        var (one, two) = (Laptop("TST-1"), Laptop("TST-2"));
        test.Store.AddAsset(one);
        test.Store.AddAsset(two);

        var page = Signed(new NewTicketModel(test.Store), Principal(admin));
        page.OnGet(null, null, [one.Id], dispose: true);
        Assert.Equal((true, "Dispose of TST-1", TicketTypes.Disposal), (page.Ticket.DisposeOnClose, page.Ticket.Title, page.Ticket.Type));

        var list = Signed(new AssetsModel(test.Store), Principal(admin));
        var result = Assert.IsType<RedirectToPageResult>(list.OnPostBulk("dispose", [one.Id, two.Id], false, null, null, null, null, null));
        Assert.Equal(true, result.RouteValues!["dispose"]);

        // A role without the tick can't start one, from the form or the list.
        var junior = new TechnicianRecord(Guid.NewGuid(), "Junior", "junior@test.example", test.Store.TechnicianTeams[0], "Junior Technician");
        test.Store.AddTechnician(junior);
        var juniorPage = Signed(new NewTicketModel(test.Store), Principal(junior));
        juniorPage.OnGet(null, null, [one.Id], dispose: true);
        Assert.False(juniorPage.Ticket.DisposeOnClose);
        Assert.IsType<ForbidResult>(Signed(new AssetsModel(test.Store), Principal(junior)).OnPostBulk("dispose", [one.Id], false, null, null, null, null, null));
    }

    [Fact]
    public void The_Disposal_type_is_the_tick_always_on()
    {
        using var test = new TestStore();
        var asset = Laptop("TST-1", "In stock or spare");
        test.Store.AddAsset(asset);
        var requester = test.AddRequester("Ada Lovelace");
        var number = test.Store.AddTicket(new TicketRecord(0, "Old laptop", "", requester.Id, [asset.Id], null,
            test.Store.Priorities[0], "Open", test.Store.Categories[0], DateTime.UtcNow, null) { Type = TicketTypes.Disposal });
        Assert.True(test.Store.Tickets.Single(x => x.Number == number).DisposeOnClose);
        Assert.Equal(number, Get(test, asset).AwaitingDisposalTicket);

        // It can't be unticked while it's a Disposal ticket; changing the type unticks it and frees the asset.
        Assert.False(test.Store.SetDisposeOnClose(number, false).Ok);
        var ticket = test.Store.Tickets.Single(x => x.Number == number);
        Assert.True(test.Store.UpdateTicket(ticket with { Type = TicketTypes.Request }));
        Assert.False(test.Store.Tickets.Single(x => x.Number == number).DisposeOnClose);
        Assert.Equal(("In stock or spare", (int?)null), (Get(test, asset).Status, Get(test, asset).AwaitingDisposalTicket));

        // In bulk too, and the type survives a restart.
        Assert.Equal(1, test.Store.BulkUpdateTickets([number], new("type", TicketTypes.Disposal)).Updated);
        Assert.Equal(number, Get(test, asset).AwaitingDisposalTicket);
        var reopened = test.Reopen().Tickets.Single(x => x.Number == number);
        Assert.Equal((TicketTypes.Disposal, true), (reopened.Type, reopened.DisposeOnClose));
        Assert.Contains(reopened.History, h => h.Action == "Disposal on close");
    }

    [Fact]
    public void Without_the_tick_the_Disposal_type_is_neither_offered_nor_taken()
    {
        using var test = new TestStore();
        var junior = new TechnicianRecord(Guid.NewGuid(), "Junior", "junior@test.example", test.Store.TechnicianTeams[0], "Junior Technician");
        test.Store.AddTechnician(junior);
        var number = test.AddTicket(test.AddRequester("Ada Lovelace").Id);

        var form = Signed(new NewTicketModel(test.Store), Principal(junior));
        Assert.DoesNotContain(TicketTypes.Disposal, form.TypeChoices);

        var page = Signed(new JobModel(test.Store, new EduNotifyClient(test.Store, new NoClients(), Microsoft.Extensions.Logging.Abstractions.NullLogger<EduNotifyClient>.Instance), Microsoft.Extensions.Logging.Abstractions.NullLogger<JobModel>.Instance), Principal(junior));
        Assert.IsType<RedirectToPageResult>(page.OnPostUpdate(number, "type", TicketTypes.Disposal));
        Assert.Equal(TicketTypes.Incident, test.Store.Tickets.Single(x => x.Number == number).Type);
    }

    // The ticket page needs an EduNotify client, which none of these tests reach.
    private sealed class NoClients : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
