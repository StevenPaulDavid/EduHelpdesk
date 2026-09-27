namespace EduHelpdesk.Tests;

// Server-side sessions (HelpdeskStore.Sessions): a signed-out session stays signed out, whatever a copied cookie says.
public class SessionTests
{
    [Fact]
    public void A_session_is_live_until_it_is_ended_and_survives_a_restart()
    {
        using var test = new TestStore();
        var account = Guid.NewGuid();
        var kept = test.Store.StartSession(HelpdeskStore.HelpdeskSession, account, DateTime.UtcNow.AddHours(1));
        var ended = test.Store.StartSession(HelpdeskStore.HelpdeskSession, account, DateTime.UtcNow.AddHours(1));
        Assert.True(test.Store.SessionActive(ended, HelpdeskStore.HelpdeskSession, account));

        test.Store.EndSession(ended);
        Assert.False(test.Store.SessionActive(ended, HelpdeskStore.HelpdeskSession, account));

        var store = test.Reopen();
        Assert.True(store.SessionActive(kept, HelpdeskStore.HelpdeskSession, account));
        Assert.False(store.SessionActive(ended, HelpdeskStore.HelpdeskSession, account));
    }

    [Fact]
    public void A_session_only_counts_for_its_own_kind_account_and_lifetime()
    {
        using var test = new TestStore();
        var account = Guid.NewGuid();
        var id = test.Store.StartSession(HelpdeskStore.PortalSession, account, DateTime.UtcNow.AddHours(1));
        Assert.False(test.Store.SessionActive(id, HelpdeskStore.HelpdeskSession, account));
        Assert.False(test.Store.SessionActive(id, HelpdeskStore.PortalSession, Guid.NewGuid()));
        Assert.False(test.Store.SessionActive(null, HelpdeskStore.PortalSession, account));
        Assert.False(test.Store.SessionActive("made-up", HelpdeskStore.PortalSession, account));

        var expired = test.Store.StartSession(HelpdeskStore.PortalSession, account, DateTime.UtcNow.AddSeconds(-1));
        Assert.False(test.Store.SessionActive(expired, HelpdeskStore.PortalSession, account));
    }

    [Fact]
    public void Everywhere_an_account_is_signed_in_can_be_ended_at_once()
    {
        using var test = new TestStore();
        var account = Guid.NewGuid();
        var other = Guid.NewGuid();
        var first = test.Store.StartSession(HelpdeskStore.HelpdeskSession, account, DateTime.UtcNow.AddHours(1));
        var second = test.Store.StartSession(HelpdeskStore.PortalSession, account, DateTime.UtcNow.AddHours(1));
        var someoneElse = test.Store.StartSession(HelpdeskStore.HelpdeskSession, other, DateTime.UtcNow.AddHours(1));

        Assert.Equal(2, test.Store.EndSessionsFor(account));
        Assert.False(test.Store.SessionActive(first, HelpdeskStore.HelpdeskSession, account));
        Assert.False(test.Store.SessionActive(second, HelpdeskStore.PortalSession, account));
        Assert.True(test.Reopen().SessionActive(someoneElse, HelpdeskStore.HelpdeskSession, other));
    }
}
