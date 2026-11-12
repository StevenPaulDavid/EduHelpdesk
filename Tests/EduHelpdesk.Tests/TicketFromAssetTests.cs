using System.Security.Claims;
using EduHelpdesk.Pages;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Tests;

// New ticket started from an asset: the asset page's button and the asset list's "Raise one ticket for them" open the
// New ticket form (Pages/NewTicket) with the assets linked and the technician raising it as the requester.
public class TicketFromAssetTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    private static AssetRecord Laptop(string tag) =>
        new(Guid.NewGuid(), tag, "Dell", "Latitude 5440", "Laptop", "SN-" + tag, "Room 1", null);

    private static ClaimsPrincipal Principal(TechnicianRecord account) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()), new Claim(ClaimTypes.Name, account.Name), new Claim(ClaimTypes.Role, account.Role)], "Test"));

    private static T Signed<T>(T page, ClaimsPrincipal user) where T : PageModel
    {
        page.PageContext = new PageContext { HttpContext = new DefaultHttpContext { User = user } };
        return page;
    }

    [Fact]
    public void The_form_links_the_assets_leaves_out_disposed_ones_and_makes_the_technician_the_requester()
    {
        using var test = new TestStore();
        var admin = test.Store.Technicians.First(x => x.Role == StaffRoles.Administrator);
        var (one, two, gone) = (Laptop("TST-1"), Laptop("TST-2"), Laptop("TST-3"));
        foreach (var asset in new[] { one, two, gone }) test.Store.AddAsset(asset);
        Assert.True(test.Store.DisposeAsset(gone.Id, Today, "Recycled (WEEE)", null).Ok);

        var page = Signed(new NewTicketModel(test.Store), Principal(admin));
        page.OnGet(null, null, [one.Id, two.Id, gone.Id, Guid.NewGuid()]);

        Assert.Equal(new[] { one.Id, two.Id }.Order(), page.Ticket.AssetIds.Order());
        Assert.Equal(1, page.DisposedLeftOut);
        // The technician's staff record, matched by email (made for them when they have none, as the staff portal does).
        var requester = test.Store.Users.Single(x => x.Id == page.Ticket.RequesterId);
        Assert.Equal(admin.Email, requester.Email, ignoreCase: true);
    }

    [Fact]
    public void An_existing_staff_record_is_used_rather_than_a_second_one()
    {
        using var test = new TestStore();
        var admin = test.Store.Technicians.First(x => x.Role == StaffRoles.Administrator);
        var mine = new UserRecord(Guid.NewGuid(), admin.Name, admin.Email.ToUpperInvariant(), "IT", "Office");
        test.Store.AddUser(mine);
        var asset = Laptop("TST-1");
        test.Store.AddAsset(asset);

        var page = Signed(new NewTicketModel(test.Store), Principal(admin));
        page.OnGet(null, null, [asset.Id]);

        Assert.Equal(mine.Id, page.Ticket.RequesterId);
        Assert.Single(test.Store.Users, x => string.Equals(x.Email, admin.Email, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Without_assets_the_form_is_unchanged()
    {
        using var test = new TestStore();
        var admin = test.Store.Technicians.First(x => x.Role == StaffRoles.Administrator);
        var usersBefore = test.Store.Users.Count;

        var page = Signed(new NewTicketModel(test.Store), Principal(admin));
        page.OnGet(null, null, null);

        Assert.Empty(page.Ticket.AssetIds);
        Assert.Equal(Guid.Empty, page.Ticket.RequesterId);
        Assert.Equal(usersBefore, test.Store.Users.Count);
    }

    [Fact]
    public void A_disposed_asset_posted_to_the_form_is_not_linked()
    {
        using var test = new TestStore();
        var admin = test.Store.Technicians.First(x => x.Role == StaffRoles.Administrator);
        var requester = test.AddRequester("Ada Lovelace");
        var (live, gone) = (Laptop("TST-1"), Laptop("TST-2"));
        test.Store.AddAsset(live);
        test.Store.AddAsset(gone);
        Assert.True(test.Store.DisposeAsset(gone.Id, Today, "Written off", null).Ok);

        var page = Signed(new NewTicketModel(test.Store), Principal(admin));
        page.OnGet(null, null, null);
        page.Ticket.Title = "Collect for recycling";
        page.Ticket.RequesterId = requester.Id;
        page.Ticket.AssetIds = [live.Id, gone.Id];
        var result = Assert.IsType<RedirectToPageResult>(page.OnPostCreateTicket());

        var number = Assert.IsType<int>(result.RouteValues!["number"]);
        Assert.Equal([live.Id], test.Store.Tickets.Single(x => x.Number == number).AssetIds);
    }

    [Fact]
    public void The_list_action_opens_the_form_with_the_ticked_assets()
    {
        using var test = new TestStore();
        var admin = test.Store.Technicians.First(x => x.Role == StaffRoles.Administrator);
        var (one, two) = (Laptop("TST-1"), Laptop("TST-2"));
        test.Store.AddAsset(one);
        test.Store.AddAsset(two);

        var list = Signed(new AssetsModel(test.Store), Principal(admin));
        var result = Assert.IsType<RedirectToPageResult>(list.OnPostBulk("ticket", [one.Id, two.Id], false, null, null, null, null, null));

        Assert.Equal("/NewTicket", result.PageName);
        Assert.Equal([one.Id, two.Id], Assert.IsAssignableFrom<IEnumerable<Guid>>(result.RouteValues!["asset"]));
    }

    [Fact]
    public void The_list_action_refuses_too_many_and_needs_tickets_new()
    {
        using var test = new TestStore();
        var admin = test.Store.Technicians.First(x => x.Role == StaffRoles.Administrator);
        var ids = Enumerable.Range(1, NewTicketModel.MaxAssetsFromList + 1).Select(_ => Guid.NewGuid()).ToArray();

        var list = Signed(new AssetsModel(test.Store), Principal(admin));
        var result = Assert.IsType<RedirectToPageResult>(list.OnPostBulk("ticket", ids, false, null, null, null, null, null));
        Assert.NotEqual("/NewTicket", result.PageName);
        Assert.Contains($"up to {NewTicketModel.MaxAssetsFromList} assets", list.Message);

        // A role that can view assets but not log tickets gets neither the option nor the action.
        Assert.True(test.Store.AddRole(new RoleRecord("Asset viewer") { Grants = { [Modules.Assets] = ModulePermission.Access | ModulePermission.View } }).Ok);
        var viewer = new TechnicianRecord(Guid.NewGuid(), "Viewer", "viewer@test.example", test.Store.TechnicianTeams[0], "Asset viewer");
        test.Store.AddTechnician(viewer);
        var viewerList = Signed(new AssetsModel(test.Store), Principal(viewer));
        Assert.False(viewerList.CanRaiseTicket);
        Assert.IsType<ForbidResult>(viewerList.OnPostBulk("ticket", ids.Take(1).ToArray(), false, null, null, null, null, null));
    }
}
