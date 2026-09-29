namespace EduHelpdesk.Tests;

// Technicians' recovery keys (Services/RecoveryKeys.cs, HelpdeskStore.RecoveryKeys.cs).
public class RecoveryKeyTests
{
    [Fact]
    public void Keys_are_long_random_and_read_back_however_they_are_typed()
    {
        var key = RecoveryKeys.NewKey();
        Assert.Matches("^[A-Z2-7]{4}(-[A-Z2-7]{4}){5}$", key);
        Assert.NotEqual(key, RecoveryKeys.NewKey());

        var hash = RecoveryKeys.Hash(key);
        Assert.True(RecoveryKeys.Matches(key.ToLowerInvariant().Replace("-", " "), hash));
        Assert.True(RecoveryKeys.Matches(key.Replace("-", ""), hash));
        Assert.False(RecoveryKeys.Matches(RecoveryKeys.NewKey(), hash));
        Assert.False(RecoveryKeys.Matches("", hash));
        Assert.False(RecoveryKeys.Matches(key, null));
        // A zero typed for the letter O is read as O.
        Assert.Equal(RecoveryKeys.Normalise("OOOO"), RecoveryKeys.Normalise("0o0O"));
    }

    [Fact]
    public void The_key_is_found_in_its_own_recovery_file()
    {
        var key = RecoveryKeys.NewKey();
        var file = RecoveryKeys.RecoveryFile("Test Academy", "sam@test.example", key, "http://helpdesk/", DateTime.UtcNow);
        Assert.Equal(key, RecoveryKeys.FindIn(file));
        Assert.Null(RecoveryKeys.FindIn("nothing to see here"));
    }

    private static TechnicianRecord AddTechnician(TestStore test, string name = "Sam Porter", bool active = true)
    {
        var technician = new TechnicianRecord(Guid.NewGuid(), name, name.ToLowerInvariant().Replace(' ', '.') + "@test.example", test.Store.TechnicianTeams[0], "Technician",
            PasswordHasher.Hash("Original-Walnut-Kettle-31"), IsActive: active);
        test.Store.AddTechnician(technician);
        return technician;
    }

    [Fact]
    public void A_key_resets_the_password_once_and_is_then_used_up()
    {
        using var test = new TestStore();
        var technician = AddTechnician(test);
        var key = test.Store.CreateRecoveryKey(technician.Id)!;

        // Survives a restart and an ordinary edit of the account.
        test.Reopen();
        test.Store.UpdateTechnician(technician with { Team = test.Store.TechnicianTeams[0] });
        Assert.True(test.Store.RecoveryKeyMatches(technician.Id, key));

        Assert.Null(test.Store.ResetPasswordWithRecoveryKey(technician.Email, RecoveryKeys.NewKey(), PasswordHasher.Hash("Wrong-Key-Lantern-44"), _ => null).Technician);
        Assert.Null(test.Store.ResetPasswordWithRecoveryKey("nobody@test.example", key, PasswordHasher.Hash("Wrong-Key-Lantern-44"), _ => null).Technician);

        var (reset, problem) = test.Store.ResetPasswordWithRecoveryKey(technician.Email.ToUpperInvariant(), key, PasswordHasher.Hash("Brand-New-Harbour-58"), _ => null);
        Assert.Null(problem);
        Assert.NotNull(reset);
        var saved = test.Reopen().Technicians.Single(x => x.Id == technician.Id);
        Assert.True(PasswordHasher.Verify(saved.PasswordHash, "Brand-New-Harbour-58"));
        Assert.False(saved.RequirePasswordChange);
        Assert.Null(saved.RecoveryKey);

        Assert.Null(test.Store.ResetPasswordWithRecoveryKey(technician.Email, key, PasswordHasher.Hash("Again-Again-Again-12"), _ => null).Technician);
    }

    [Fact]
    public void A_refused_new_password_leaves_the_key_unused()
    {
        using var test = new TestStore();
        var technician = AddTechnician(test);
        var key = test.Store.CreateRecoveryKey(technician.Id)!;

        var (reset, problem) = test.Store.ResetPasswordWithRecoveryKey(technician.Email, key, PasswordHasher.Hash("Sam-Porter-Password-1"),
            account => PasswordRules.Problem("Sam-Porter-Password-1", account.Name, account.Email));
        Assert.Null(reset);
        Assert.NotNull(problem);
        Assert.True(test.Store.RecoveryKeyMatches(technician.Id, key));
    }

    [Fact]
    public void Inactive_accounts_removed_keys_and_the_school_switch_all_stop_a_reset()
    {
        using var test = new TestStore();
        var inactive = AddTechnician(test, "Ivy Gone", active: false);
        var inactiveKey = test.Store.CreateRecoveryKey(inactive.Id)!;
        Assert.Null(test.Store.ResetPasswordWithRecoveryKey(inactive.Email, inactiveKey, "x", _ => null).Technician);

        var removed = AddTechnician(test, "Rex Removed");
        var removedKey = test.Store.CreateRecoveryKey(removed.Id)!;
        Assert.True(test.Store.RemoveRecoveryKey(removed.Id).Ok);
        Assert.Null(test.Store.ResetPasswordWithRecoveryKey(removed.Email, removedKey, "x", _ => null).Technician);

        var active = AddTechnician(test, "Ada Active");
        var activeKey = test.Store.CreateRecoveryKey(active.Id)!;
        test.Store.SetAllowRecoveryKeys(false);
        Assert.False(test.Reopen().AllowRecoveryKeys);
        Assert.Null(test.Store.ResetPasswordWithRecoveryKey(active.Email, activeKey, "x", _ => null).Technician);
        test.Store.SetAllowRecoveryKeys(true);
        Assert.NotNull(test.Store.ResetPasswordWithRecoveryKey(active.Email, activeKey, PasswordHasher.Hash("Ada-Fresh-Meadow-77"), _ => null).Technician);

        // A new key replaces the old one.
        var first = test.Store.CreateRecoveryKey(active.Id)!;
        var second = test.Store.CreateRecoveryKey(active.Id)!;
        Assert.False(test.Store.RecoveryKeyMatches(active.Id, first));
        Assert.True(test.Store.RecoveryKeyMatches(active.Id, second));
    }
}
