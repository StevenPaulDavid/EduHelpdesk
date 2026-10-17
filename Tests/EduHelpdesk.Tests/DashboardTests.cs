using System.Security.Claims;
using EduHelpdesk.Pages;
using EduHelpdesk.Pages.Reports;

namespace EduHelpdesk.Tests;

// The dashboard (Pages/Reports/Dashboard): who sees it and which parts, and the ticket list's "logged between" filter
// its bars open.
public class DashboardTests
{
    private static ClaimsPrincipal Principal(TechnicianRecord account) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()), new Claim(ClaimTypes.Name, account.Name), new Claim(ClaimTypes.Role, account.Role)], "Test"));

    private static ClaimsPrincipal Account(TestStore test, string role)
    {
        var account = new TechnicianRecord(Guid.NewGuid(), role + " account", $"{Guid.NewGuid():N}@test.example", test.Store.TechnicianTeams[0], role);
        test.Store.AddTechnician(account);
        return Principal(account);
    }

    [Fact]
    public void Each_part_needs_its_report_and_the_dashboard_comes_first_in_the_menu()
    {
        using var test = new TestStore();
        var admin = Principal(test.Store.Technicians.First(x => x.Role == StaffRoles.Administrator));
        Assert.Equal(["tickets", "assets", "spend", "loans", "parts", "projects"], DashboardModel.Sections(test.Store, admin));
        Assert.Equal(ReportMenu.DashboardPage, ReportMenu.For(test.Store, admin)[0].Page);

        // The seeded Junior Technician has the asset, parts and loans reports, and no others the dashboard charts.
        var junior = Account(test, "Junior Technician");
        Assert.Equal(["assets", "loans", "parts"], DashboardModel.Sections(test.Store, junior));
        Assert.Contains(ReportMenu.For(test.Store, junior), x => x.Page == ReportMenu.DashboardPage);
    }

    [Fact]
    public void Without_any_report_it_charts_there_is_no_dashboard()
    {
        using var test = new TestStore();
        Assert.True(test.Store.AddRole(new RoleRecord("Onboarding only")
        {
            Grants = { [Modules.Reports] = ModulePermission.Access },
            Flags = { Modules.Flags.ReportOnboarding }
        }).Ok);
        var person = Account(test, "Onboarding only");
        Assert.Empty(DashboardModel.Sections(test.Store, person));
        Assert.DoesNotContain(ReportMenu.For(test.Store, person), x => x.Page == ReportMenu.DashboardPage);
        Assert.Contains(ReportMenu.For(test.Store, person), x => x.Page == "/Reports/Onboarding");
    }

    [Fact]
    public void The_ticket_list_filters_by_the_day_a_ticket_was_logged()
    {
        using var test = new TestStore();
        var requester = test.AddRequester("Priya Shah").Id;
        var now = DateTime.UtcNow;
        int[] mine = [test.AddTicket(requester, "Old", createdAt: now.AddDays(-10)), test.AddTicket(requester, "Recent", createdAt: now.AddDays(-2)),
            test.AddTicket(requester, "Today", createdAt: now)];
        var store = test.Store;
        // Only these three: the new install's demo ticket was logged recently too.
        var tickets = store.Tickets.Where(t => mine.Contains(t.Number)).ToList();
        var context = new TicketContext(store.Users, store.Technicians, store.Assets, store.Priorities, store.Statuses, now, 24, null);
        var day = DateOnly.FromDateTime(now.AddDays(-2).ToLocalTime());

        Assert.Equal(["Recent"], new TicketListQuery { View = "all", LoggedFrom = day, LoggedTo = day }.Run(tickets, context).Select(x => x.Title));
        Assert.Equal(["Today", "Recent"], new TicketListQuery { View = "all", LoggedFrom = day }.Run(tickets, context).Select(x => x.Title));
        Assert.Equal(["Old"], new TicketListQuery { View = "all", LoggedTo = day.AddDays(-1) }.Run(tickets, context).Select(x => x.Title));
    }
}
