using Microsoft.Extensions.Logging.Abstractions;

namespace EduHelpdesk.Tests;

// Temporary passwords for new accounts, and the helpdesk address printed on their quick start guides.
public class AccountTests
{
    [Fact]
    public void Made_up_passwords_pass_the_password_rules_and_differ()
    {
        var made = Enumerable.Range(0, 200).Select(_ => TemporaryPasswords.Generate("Robin Maple", "robin.maple@school.example")).ToList();
        Assert.All(made, x => Assert.Null(PasswordRules.Problem(x, "Robin Maple", "robin.maple@school.example")));
        Assert.All(made, x => Assert.Matches("^[A-Z][a-z]+-[A-Z][a-z]+-[A-Z][a-z]+-[1-9][0-9]$", x));
        // Nothing that contains the person's own name gets through.
        Assert.DoesNotContain(made, x => x.Contains("Robin", StringComparison.OrdinalIgnoreCase) || x.Contains("Maple", StringComparison.OrdinalIgnoreCase));
        Assert.True(made.Distinct().Count() > 190);
    }

    [Fact]
    public void A_remembered_password_is_only_given_back_while_it_is_still_the_accounts()
    {
        var passwords = new TemporaryPasswords();
        var id = Guid.NewGuid();
        var hash = PasswordHasher.Hash("Otter-Lantern-Maple-38");
        passwords.Remember(id, "Otter-Lantern-Maple-38", hash);

        Assert.Equal("Otter-Lantern-Maple-38", passwords.Recall(id, hash));
        Assert.Null(passwords.Recall(id, PasswordHasher.Hash("Something-Else-Entirely-12")));
        Assert.Null(passwords.Recall(id, null));
        Assert.Null(passwords.Recall(Guid.NewGuid(), hash));
    }

    [Fact]
    public void Staff_without_a_password_get_temporary_ones_in_one_batch_only_their_issuer_can_open()
    {
        using var test = new TestStore();
        var ann = test.AddRequester("Ann Lowe");
        var ben = test.AddRequester("Ben Carr");
        test.AddRequester("Cy Left", active: false);
        test.Store.UpdateUser(test.AddRequester("Dee Set") with { PasswordHash = PasswordHasher.Hash("Kept-Own-Password-19") });
        // A technician's own portal record is reached through their helpdesk sign-in, so it's left alone.
        test.Store.AddTechnician(new TechnicianRecord(Guid.NewGuid(), "Tess Tech", "tess.tech@test.example", test.Store.TechnicianTeams[0], "Technician"));
        test.AddRequester("Tess Tech");

        // The fresh store's demo requester has no password either, so it's in the list too.
        var waiting = test.Store.RequestersWithoutPassword();
        Assert.Contains(waiting, x => x.Id == ann.Id);
        Assert.Contains(waiting, x => x.Id == ben.Id);
        Assert.DoesNotContain(waiting, x => x.Name is "Cy Left" or "Dee Set" or "Tess Tech");

        var passwords = new TemporaryPasswords();
        var batchId = passwords.IssueToRequesters(test.Store, waiting, "issuer");
        Assert.Null(passwords.RecallBatch(batchId, "someone else"));
        var batch = passwords.RecallBatch(batchId, "issuer")!;
        Assert.False(batch.Technicians);
        Assert.Equal(waiting.Count, batch.AccountIds.Count);

        foreach (var id in new[] { ann.Id, ben.Id })
        {
            var person = test.Reopen().Users.Single(x => x.Id == id);
            Assert.True(person.RequirePasswordChange);
            Assert.True(PasswordHasher.Verify(person.PasswordHash, passwords.Recall(id, person.PasswordHash)!));
        }
        Assert.Empty(test.Store.RequestersWithoutPassword());
    }

    [Theory]
    [InlineData("https://helpdesk.school.org.uk", "https://helpdesk.school.org.uk/")]
    [InlineData(" http://HELPDESK-PC:5277/ ", "http://helpdesk-pc:5277/")]
    [InlineData("https://school.org.uk/helpdesk/", "https://school.org.uk/helpdesk/")]
    [InlineData("helpdesk.school.org.uk", null)]
    [InlineData("ftp://helpdesk", null)]
    [InlineData("https://helpdesk/?x=1", null)]
    public void Helpdesk_addresses_are_tidied_or_refused(string typed, string? expected) =>
        Assert.Equal(expected, HelpdeskStore.NormaliseSiteAddress(typed));

    [Fact]
    public void The_address_is_kept_and_the_installer_does_not_replace_one_set_in_settings()
    {
        using var test = new TestStore();
        Assert.Equal("", test.Store.SiteAddress);
        Assert.False(test.Store.SetSiteAddress("not an address").Ok);

        var data = Path.Combine(test.Root, "App_Data");
        File.WriteAllText(Path.Combine(data, InstallSettings.FileName), """{ "SiteAddress": "http://helpdesk-pc/" }""");
        var brandName = test.Store.Branding.BrandName;
        InstallSettings.Apply(test.Store, data, NullLogger.Instance);
        Assert.Equal("http://helpdesk-pc/", test.Store.SiteAddress);
        Assert.Equal(brandName, test.Store.Branding.BrandName);

        Assert.True(test.Store.SetSiteAddress("https://helpdesk.school.org.uk").Ok);
        File.WriteAllText(Path.Combine(data, InstallSettings.FileName), """{ "SiteAddress": "http://helpdesk-pc/" }""");
        InstallSettings.Apply(test.Store, data, NullLogger.Instance);
        Assert.Equal("https://helpdesk.school.org.uk/", test.Reopen().SiteAddress);
    }
}
