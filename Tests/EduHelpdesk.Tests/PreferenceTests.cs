namespace EduHelpdesk.Tests;

// Display choices kept against a staff account (HelpdeskStore.Preferences) - which columns a list shows. They have to
// survive a restart, stay with the account that made them, and stay out of the main save.
public class PreferenceTests
{
    [Fact]
    public void A_choice_survives_a_restart_and_belongs_to_its_own_account()
    {
        using var test = new TestStore();
        var mine = Guid.NewGuid();
        var someoneElse = Guid.NewGuid();
        test.Store.SetPreference(mine, "columns:assets", "type,model,holder");

        var store = test.Reopen();
        Assert.Equal("type,model,holder", store.Preference(mine, "columns:assets"));
        Assert.Null(store.Preference(someoneElse, "columns:assets"));
        Assert.Null(store.Preference(mine, "columns:parts"));
    }

    [Fact]
    public void Clearing_a_choice_puts_the_default_back_after_a_restart_too()
    {
        using var test = new TestStore();
        var account = Guid.NewGuid();
        test.Store.SetPreference(account, "columns:assets", "type");
        test.Store.SetPreference(account, "columns:assets", null);
        Assert.Null(test.Store.Preference(account, "columns:assets"));
        Assert.Null(test.Reopen().Preference(account, "columns:assets"));
    }

    [Fact]
    public void Choices_stay_out_of_the_main_save_and_go_with_a_factory_reset()
    {
        using var test = new TestStore();
        var account = Guid.NewGuid();
        test.Store.SetPreference(account, "columns:assets", "type");
        // Written directly, like the bell's read marks: the saved data still matches memory exactly.
        Assert.Null(test.Store.CompareDatabaseWithMemory());

        Assert.True(test.Store.ResetFactory("DELETE").Ok);
        Assert.Null(test.Store.Preference(account, "columns:assets"));
        Assert.Null(test.Reopen().Preference(account, "columns:assets"));
    }
}
