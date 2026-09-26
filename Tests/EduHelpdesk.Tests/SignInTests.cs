using System.Net;
using Microsoft.Extensions.Logging.Abstractions;

namespace EduHelpdesk.Tests;

public class PasswordRulesTests
{
    [Theory]
    [InlineData("short1")]
    [InlineData("Password123!")]
    [InlineData("!!welcome2025")]
    [InlineData("Teachers1")]
    [InlineData("12345678901")]
    [InlineData("aaaaaaaaaaaa")]
    public void Weak_passwords_are_refused(string password) => Assert.NotNull(PasswordRules.Problem(password));

    [Fact]
    public void The_persons_own_name_is_refused() =>
        Assert.NotNull(PasswordRules.Problem("bloggs-in-the-lab", "Jo Bloggs", "jo.bloggs@school.example"));

    [Theory]
    [InlineData("correct horse battery")]
    [InlineData("Kettle-Planet-Fox")]
    public void Long_unusual_passwords_are_accepted(string password) =>
        Assert.Null(PasswordRules.Problem(password, "Jo Bloggs", "jo.bloggs@school.example"));

    [Fact]
    public void Hashes_verify_only_the_right_password()
    {
        var hash = PasswordHasher.Hash("Kettle-Planet-Fox");
        Assert.True(PasswordHasher.Verify(hash, "Kettle-Planet-Fox"));
        Assert.False(PasswordHasher.Verify(hash, "kettle-planet-fox"));
        Assert.False(PasswordHasher.Verify(null, "Kettle-Planet-Fox"));
    }
}

public class SignInThrottleTests
{
    private static readonly IPAddress Address = IPAddress.Parse("10.0.0.5");

    [Fact]
    public void An_account_locks_after_five_wrong_passwords_and_says_so_in_the_audit_log()
    {
        using var test = new TestStore();
        var throttle = new SignInThrottle(test.Store, NullLogger<SignInThrottle>.Instance);
        for (var i = 0; i < SignInThrottle.AccountLimit - 1; i++) throttle.Failed("helpdesk", "jo@school.example", Address);
        Assert.Null(throttle.Refusal("helpdesk", "jo@school.example", Address));

        throttle.Failed("helpdesk", "JO@school.example ", Address);
        Assert.Contains("this account", throttle.Refusal("helpdesk", "jo@school.example", Address));
        // Another account from the same computer, and the same email on the portal's form, are unaffected.
        Assert.Null(throttle.Refusal("helpdesk", "sam@school.example", Address));
        Assert.Null(throttle.Refusal("portal", "jo@school.example", Address));
        Assert.Contains(test.Store.GetAuditEntries(), x => x.Area == "Sign-in" && x.Action == "Locked out" && x.Entity == "JO@school.example");

        // Someone setting a new password lifts the lock straight away.
        throttle.Clear("helpdesk", "jo@school.example");
        Assert.Null(throttle.Refusal("helpdesk", "jo@school.example", Address));
    }

    [Fact]
    public void A_computer_is_blocked_after_thirty_wrong_passwords_across_accounts()
    {
        using var test = new TestStore();
        var throttle = new SignInThrottle(test.Store, NullLogger<SignInThrottle>.Instance);
        for (var i = 0; i < SignInThrottle.AddressLimit; i++) throttle.Failed("portal", $"person{i}@school.example", Address);
        Assert.Contains("this computer", throttle.Refusal("portal", "someone.new@school.example", Address));
        Assert.Null(throttle.Refusal("portal", "someone.new@school.example", IPAddress.Parse("10.0.0.6")));
    }

    [Fact]
    public void The_security_policy_refuses_inline_script()
    {
        var scriptSrc = SecurityHeaders.Policy.Split(';').Select(x => x.Trim()).Single(x => x.StartsWith("script-src "));
        Assert.Equal("script-src 'self'", scriptSrc);
        Assert.Contains("frame-ancestors 'none'", SecurityHeaders.Policy);
        Assert.Contains("object-src 'none'", SecurityHeaders.Policy);
    }
}
