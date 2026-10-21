namespace EduHelpdesk.Tests;

// Who may show the helpdesk inside their own page: nobody, unless Settings → Sign-in security names the school's
// EduDashboard.
public class DashboardFramingTests
{
    [Fact]
    public void With_no_dashboard_nothing_may_frame_the_pages()
    {
        Assert.Equal(SecurityHeaders.Policy, SecurityHeaders.PolicyFor(null));
        Assert.Equal(SecurityHeaders.Policy, SecurityHeaders.PolicyFor(""));
        Assert.Equal("frame-ancestors 'none'", SecurityHeaders.FrameAncestors(""));
    }

    [Fact]
    public void With_a_dashboard_only_it_may_frame_the_pages()
    {
        var policy = SecurityHeaders.PolicyFor("https://dashboard.school.org.uk");
        Assert.Contains("frame-ancestors https://dashboard.school.org.uk", policy);
        Assert.DoesNotContain("frame-ancestors 'none'", policy);
        Assert.Contains("script-src 'self'", policy);
        Assert.Contains("object-src 'none'", policy);
        Assert.Equal("frame-ancestors https://dashboard.school.org.uk", SecurityHeaders.FrameAncestors("https://dashboard.school.org.uk"));
    }

    [Theory]
    [InlineData("https://dashboard.school.org.uk", "https://dashboard.school.org.uk")]
    [InlineData("  https://dashboard.school.org.uk/  ", "https://dashboard.school.org.uk")]
    [InlineData("http://server:5297/Tiles", "http://server:5297")]
    [InlineData("HTTPS://Dashboard.School.org.uk", "https://dashboard.school.org.uk")]
    [InlineData("dashboard.school.org.uk", null)]
    [InlineData("ftp://dashboard.school.org.uk", null)]
    [InlineData("https://dashboard.school.org.uk/?x=1", null)]
    [InlineData("https://someone@dashboard.school.org.uk", null)]
    [InlineData("*", null)]
    public void The_address_is_kept_as_scheme_server_and_port(string typed, string? expected) =>
        Assert.Equal(expected, SecurityHeaders.NormaliseOrigin(typed));

    [Fact]
    public void The_dashboard_address_starts_empty_is_kept_and_can_be_cleared()
    {
        using var test = new TestStore();
        Assert.Equal("", test.Store.DashboardAddress);
        Assert.False(test.Store.SetDashboardAddress("dashboard.school.org.uk").Ok);
        Assert.Equal("", test.Store.DashboardAddress);

        Assert.True(test.Store.SetDashboardAddress("https://dashboard.school.org.uk/").Ok);
        Assert.Equal("https://dashboard.school.org.uk", test.Reopen().DashboardAddress);

        Assert.True(test.Reopen().SetDashboardAddress("").Ok);
        Assert.Equal("", test.Reopen().DashboardAddress);
    }
}
