namespace EduHelpdesk.Tests;

// Authenticator codes (Services/Totp.cs) and two-step sign-in in the store (HelpdeskStore.TwoFactor.cs).
public class TotpTests
{
    // RFC 6238 appendix B: the ASCII secret "12345678901234567890", SHA-1, eight digits - the last six are ours.
    private const string RfcSecret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    [Theory]
    [InlineData(59, "287082")]
    [InlineData(1111111109, "081804")]
    [InlineData(1111111111, "050471")]
    [InlineData(1234567890, "005924")]
    [InlineData(2000000000, "279037")]
    public void Codes_match_the_RFC_test_vectors(long unixSeconds, string expected) =>
        Assert.Equal(expected, Totp.Code(Base32.Decode(RfcSecret)!, unixSeconds / 30));

    [Fact]
    public void A_code_is_accepted_once_with_a_step_of_leeway_either_side()
    {
        var secret = Totp.NewSecret();
        var now = DateTimeOffset.UtcNow;
        var key = Base32.Decode(secret)!;
        var step = Totp.StepAt(now);
        Assert.Equal(step, Totp.Verify(secret, Totp.Code(key, step), now, long.MinValue));
        Assert.Equal(step - 1, Totp.Verify(secret, Totp.Code(key, step - 1), now, long.MinValue));
        Assert.Null(Totp.Verify(secret, Totp.Code(key, step - 3), now, long.MinValue));
        // Used already: not again.
        Assert.Null(Totp.Verify(secret, Totp.Code(key, step), now, step));
        Assert.Equal(step, Totp.Verify(secret, Totp.Code(key, step)[..3] + " " + Totp.Code(key, step)[3..], now, long.MinValue));
        Assert.Null(Totp.Verify(secret, "12345", now, long.MinValue));
    }

    [Fact]
    public void Base32_round_trips_and_recovery_codes_are_distinct()
    {
        var bytes = Enumerable.Range(0, 37).Select(x => (byte)(x * 7)).ToArray();
        Assert.Equal(bytes, Base32.Decode(Base32.Encode(bytes)));
        Assert.Null(Base32.Decode("not base32!"));
        var codes = Totp.NewRecoveryCodes();
        Assert.Equal(10, codes.Distinct().Count());
        Assert.All(codes, code => Assert.Matches("^[a-z2-7]{4}-[a-z2-7]{4}$", code));
        Assert.Equal(Totp.HashRecoveryCode(codes[0]), Totp.HashRecoveryCode(codes[0].ToUpperInvariant().Replace("-", " ")));
    }
}

public class TwoFactorStoreTests
{
    private static string CodeNow(string secret, int offset = 0) => Totp.Code(Base32.Decode(secret)!, Totp.StepAt(DateTimeOffset.UtcNow) + offset);

    [Fact]
    public void Turning_it_on_needs_a_right_code_and_survives_a_restart_and_an_edit()
    {
        using var test = new TestStore();
        var admin = test.Store.Technicians.First();
        var secret = Totp.NewSecret();
        Assert.False(test.Store.EnableTwoFactor(admin.Id, secret, "000000").Ok);
        var (ok, _, codes) = test.Store.EnableTwoFactor(admin.Id, secret, CodeNow(secret));
        Assert.True(ok);
        Assert.Equal(10, codes.Count);

        // The staff form builds a record without the two-step settings; saving it mustn't wipe them.
        test.Store.UpdateTechnician(new TechnicianRecord(admin.Id, admin.Name, admin.Email, admin.Team, admin.Role, admin.PasswordHash, false, true));
        var reloaded = test.Reopen().Technicians.Single(x => x.Id == admin.Id);
        Assert.NotNull(reloaded.TwoFactor);
        Assert.Equal(secret, reloaded.TwoFactor!.Secret);
        Assert.Equal(10, reloaded.TwoFactor.RecoveryCodeHashes.Count);
    }

    [Fact]
    public void Codes_are_single_use_and_recovery_codes_are_struck_off()
    {
        using var test = new TestStore();
        var admin = test.Store.Technicians.First();
        var secret = Totp.NewSecret();
        var codes = test.Store.EnableTwoFactor(admin.Id, secret, CodeNow(secret, -1)).RecoveryCodes;

        Assert.True(test.Store.CheckTwoFactor(admin.Id, CodeNow(secret)).Ok);
        Assert.False(test.Store.CheckTwoFactor(admin.Id, CodeNow(secret)).Ok);

        var first = test.Store.CheckTwoFactor(admin.Id, codes[0]);
        Assert.True(first.Ok);
        Assert.True(first.UsedRecoveryCode);
        Assert.Equal(9, first.RecoveryCodesLeft);
        Assert.False(test.Store.CheckTwoFactor(admin.Id, codes[0]).Ok);
        Assert.Equal(9, test.Reopen().Technicians.Single(x => x.Id == admin.Id).TwoFactor!.RecoveryCodeHashes.Count);
    }

    [Fact]
    public void It_can_be_required_reset_and_only_turned_off_with_a_code_when_optional()
    {
        using var test = new TestStore();
        var admin = test.Store.Technicians.First();
        var secret = Totp.NewSecret();
        test.Store.EnableTwoFactor(admin.Id, secret, CodeNow(secret, -1));

        test.Store.SetRequireTwoFactor(true);
        Assert.True(test.Reopen().RequireTwoFactor);
        Assert.False(test.Store.DisableTwoFactor(admin.Id, CodeNow(secret)).Ok);

        test.Store.SetRequireTwoFactor(false);
        Assert.False(test.Store.DisableTwoFactor(admin.Id, "000000").Ok);
        Assert.True(test.Store.DisableTwoFactor(admin.Id, CodeNow(secret)).Ok);
        Assert.Null(test.Store.Technicians.Single(x => x.Id == admin.Id).TwoFactor);

        test.Store.EnableTwoFactor(admin.Id, secret, CodeNow(secret, 1));
        Assert.True(test.Store.ResetTwoFactor(admin.Id).Ok);
        Assert.Null(test.Reopen().Technicians.Single(x => x.Id == admin.Id).TwoFactor);
        Assert.Contains(test.Store.GetAuditEntries(), x => x.Area == "Sign-in" && x.Action == "Two-step sign-in reset");
    }
}
