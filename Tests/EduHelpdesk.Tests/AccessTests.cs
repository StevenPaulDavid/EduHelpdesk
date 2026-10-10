using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Tests;

// The DfE access control register merged in from EduInventory: systems and areas, each person's access, the problems
// with it, the termly review, and the leaver, onboarding and retention hooks into People.
public class AccessTests
{
    private static readonly DateOnly Today = AssetInsights.Today;

    private static AccessResource System(TestStore test, string name = "Microsoft 365", bool mfa = true, string kind = AccessKinds.System)
    {
        var (ok, message, id) = test.Store.SaveAccessResource(new AccessResource(Guid.Empty, name, DateTime.UtcNow) { Kind = kind, MfaRequired = mfa, Category = "Cloud service" });
        Assert.True(ok, message);
        return test.Store.FindAccessResource(id!.Value)!;
    }

    private static AccessGrant Grant(TestStore test, Guid person, Guid system, Action<AccessGrant>? check = null, string level = "", bool privileged = false, string? approvedBy = null, string mfa = MfaStates.Enabled)
    {
        var (ok, message, id) = test.Store.AddAccessGrant(new AccessGrant(Guid.Empty, person, system, DateTime.UtcNow)
        {
            AccessLevel = level, Privileged = privileged, ApprovedBy = approvedBy ?? "", Mfa = mfa, GrantedOn = Today.AddDays(-10), Identifier = "jsmith"
        });
        Assert.True(ok, message);
        return test.Store.FindAccessGrant(id!.Value)!;
    }

    [Fact]
    public void The_register_and_a_persons_type_and_dates_survive_a_restart()
    {
        using var test = new TestStore();
        var person = test.AddRequester("Jo Smith");
        Assert.True(test.Store.UpdateUser(person with { PersonType = "Contractor", StartDate = new DateOnly(2026, 9, 1) }));
        var system = System(test);
        var grant = Grant(test, person.Id, system.Id, level: "Global admin", privileged: true, approvedBy: "Head teacher");
        Assert.True(test.Store.CompleteAccessReview(Today, "Business manager", "All", "Termly", [grant.Id], [], null).Ok);

        var store = test.Reopen();
        var saved = store.Users.Single(x => x.Id == person.Id);
        Assert.Equal(("Contractor", new DateOnly(2026, 9, 1)), (saved.PersonType, saved.StartDate!.Value));
        var savedGrant = store.FindAccessGrant(grant.Id)!;
        Assert.Equal(("Global admin", "jsmith", true, "Head teacher", MfaStates.Enabled, Today), (savedGrant.AccessLevel, savedGrant.Identifier, savedGrant.Privileged, savedGrant.ApprovedBy, savedGrant.Mfa, savedGrant.LastReviewedOn!.Value));
        Assert.Equal(("Microsoft 365", true, "Cloud service"), (store.FindAccessResource(system.Id)!.Name, store.FindAccessResource(system.Id)!.MfaRequired, store.FindAccessResource(system.Id)!.Category));
        Assert.Equal(("Business manager", 1, 0), (store.AccessReviews.Single().ReviewedWith, store.AccessReviews.Single().Confirmed, store.AccessReviews.Single().Revoked));
    }

    [Fact]
    public void A_new_install_and_an_upgrade_both_get_the_lists_and_the_business_manager_can_review()
    {
        using var test = new TestStore();
        Assert.Contains("Service account", test.Store.PersonTypes);
        Assert.Contains("Leaver", test.Store.RevokeReasons);
        Assert.Contains("MIS", test.Store.AccessCategories);
        Assert.True(test.Store.RoleAllows(HelpdeskStore.BusinessManagerRole, Modules.Access, ModulePermission.Edit));
        Assert.False(test.Store.RoleAllows(HelpdeskStore.GovernorRole, Modules.Access, ModulePermission.Access));

        // A database at stage 2 of the merge: the contracts register but nothing of the access register.
        SqliteConnection.ClearAllPools();
        using (var connection = new SqliteConnection($"Data Source={test.DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM PersonTypes; DELETE FROM AccessCategories; DELETE FROM RevokeReasons;
                DELETE FROM RolePermissions WHERE RoleName = 'Business manager' AND Permission LIKE 'Access:%';
                UPDATE Metadata SET Value = '1' WHERE Key = 'ComplianceVersion';
                """;
            command.ExecuteNonQuery();
        }
        var store = test.Reopen();
        Assert.Equal(AccessDefaults.PersonTypes, store.PersonTypes);
        Assert.Contains("Removed at review", store.RevokeReasons);
        Assert.True(store.RoleAllows(HelpdeskStore.BusinessManagerRole, Modules.Access, ModulePermission.Edit));
    }

    [Fact]
    public void Problems_with_access_follow_the_cyber_security_standard()
    {
        var cloud = new AccessResource(Guid.NewGuid(), "Microsoft 365", DateTime.UtcNow) { MfaRequired = true };
        var room = new AccessResource(Guid.NewGuid(), "Server room", DateTime.UtcNow) { Kind = AccessKinds.Physical };
        var staff = new UserRecord(Guid.NewGuid(), "Jo Smith", "jo@test.example", "", "");
        var leaver = staff with { IsActive = false, LeftAt = DateTime.UtcNow.AddDays(-3) };
        var fresh = new AccessGrant(Guid.NewGuid(), staff.Id, cloud.Id, DateTime.UtcNow) { GrantedOn = Today, Mfa = MfaStates.Enabled };
        List<string> Kinds(AccessGrant g, UserRecord p, AccessResource r) => AccessRules.Issues(g, p, r, Today, 120).Select(x => x.Kind).ToList();

        Assert.Empty(Kinds(fresh, staff, cloud));
        Assert.Equal(["Leaver"], Kinds(fresh, leaver, cloud));
        Assert.Equal(["Mfa"], Kinds(fresh with { Mfa = MfaStates.NotEnabled }, staff, cloud));
        Assert.Equal(["Approval"], Kinds(fresh with { Privileged = true }, staff, cloud));
        Assert.Equal(["Retired"], Kinds(fresh, staff, cloud with { IsRetired = true }));
        Assert.Equal(["Review"], Kinds(fresh with { GrantedOn = Today.AddDays(-200) }, staff, cloud));
        // A key needs no MFA, even for someone with privileged access to it.
        Assert.Equal(["Approval"], Kinds(fresh with { ResourceId = room.Id, Privileged = true, Mfa = MfaStates.NotApplicable }, staff, room));
        // Removed access is history, not a problem.
        Assert.Empty(Kinds(fresh with { RevokedOn = Today.AddDays(-1) }, leaver, cloud));
    }

    [Fact]
    public void Access_is_refused_twice_or_to_a_retired_system_and_stays_on_the_register_once_removed()
    {
        using var test = new TestStore();
        var person = test.AddRequester("Jo Smith");
        var system = System(test);
        var grant = Grant(test, person.Id, system.Id);
        Assert.False(test.Store.AddAccessGrant(new AccessGrant(Guid.Empty, person.Id, system.Id, DateTime.UtcNow) { GrantedOn = Today }).Ok);
        Assert.NotNull(test.Store.DeleteAccessResource(system.Id));
        Assert.True(test.Store.SaveAccessResource(system with { IsRetired = true }).Ok);
        var other = test.AddRequester("Sam Jones");
        Assert.False(test.Store.AddAccessGrant(new AccessGrant(Guid.Empty, other.Id, system.Id, DateTime.UtcNow) { GrantedOn = Today }).Ok);

        // Removing it before it was granted would say they never had it, so it is the grant date instead.
        Assert.True(test.Store.RevokeAccessGrant(grant.Id, Today.AddYears(-1), "No longer required").Ok);
        var revoked = test.Store.FindAccessGrant(grant.Id)!;
        Assert.Equal((grant.GrantedOn, "No longer required"), (revoked.RevokedOn, revoked.RevokeReason));
        Assert.NotNull(test.Store.DeleteUser(person.Id));
    }

    [Fact]
    public void The_leaver_check_removes_everything_as_of_the_day_they_left()
    {
        using var test = new TestStore();
        var person = test.AddRequester("Jo Smith");
        var a = Grant(test, person.Id, System(test, "SIMS").Id);
        var b = Grant(test, person.Id, System(test, "Server room", mfa: false, kind: AccessKinds.Physical).Id);
        var left = new DateTime(Today.Year, Today.Month, Today.Day, 12, 0, 0, DateTimeKind.Local).AddDays(-2).ToUniversalTime();
        Assert.True(test.Store.UpdateUser(test.Store.Users.Single(x => x.Id == person.Id) with { IsActive = false, LeftAt = left }));

        Assert.Equal(2, test.Store.GetHoldings(person.Id)!.Access.Count);
        Assert.Equal(2, test.Store.LeaversStillHolding()[person.Id]);
        Assert.True(test.Store.RevokeAllAccess(person.Id, null, null).Ok);

        foreach (var id in new[] { a.Id, b.Id })
            Assert.Equal((Today.AddDays(-2), "Leaver"), (test.Store.FindAccessGrant(id)!.RevokedOn!.Value, test.Store.FindAccessGrant(id)!.RevokeReason));
        Assert.Empty(test.Store.GetHoldings(person.Id)!.Access);
        Assert.False(test.Store.LeaversStillHolding().ContainsKey(person.Id));
    }

    [Fact]
    public void A_review_confirms_what_is_kept_removes_the_rest_and_is_kept_as_evidence()
    {
        using var test = new TestStore();
        var person = test.AddRequester("Jo Smith");
        var keep = Grant(test, person.Id, System(test, "SIMS").Id);
        var drop = Grant(test, person.Id, System(test, "Finance").Id);
        Assert.False(test.Store.CompleteAccessReview(Today, " ", null, null, [keep.Id, drop.Id], [], null).Ok);
        var (ok, message) = test.Store.CompleteAccessReview(Today, "Business manager", "All", null, [keep.Id, drop.Id], [drop.Id], null);
        Assert.True(ok, message);

        Assert.Equal(Today, test.Store.FindAccessGrant(keep.Id)!.LastReviewedOn);
        Assert.Null(test.Store.FindAccessGrant(keep.Id)!.RevokedOn);
        Assert.Equal((Today, AccessDefaults.RemovedAtReview), (test.Store.FindAccessGrant(drop.Id)!.RevokedOn!.Value, test.Store.FindAccessGrant(drop.Id)!.RevokeReason));
        Assert.Equal((1, 1), (test.Store.AccessReviews.Single().Confirmed, test.Store.AccessReviews.Single().Revoked));
        Assert.Equal(Today.AddDays(test.Store.AccessReviewDays), AccessRules.NextReviewDue(test.Store.AccessReviews, test.Store.AccessReviewDays));
    }

    [Fact]
    public void An_onboarding_task_records_access_when_ticked_and_takes_it_back_when_unticked()
    {
        using var test = new TestStore();
        var system = System(test, "SIMS");
        var template = test.Store.OnboardingTemplates.Single(x => x.Name == "Teacher");
        var details = new HelpdeskStore.OnboardingDetails("Jane Smith", null, "Teacher", null, null, Today.AddDays(7), null);
        var (_, _, number) = test.Store.StartOnboarding(details, template.Id, null);
        var add = test.Store.AddOnboardingTask(number, "Set up SIMS", 0, OnboardingOwners.IT, OnboardingActions.Encode(OnboardingActions.GrantAccess, system.Id.ToString()));
        Assert.True(add.Ok, add.Message);
        var record = test.Store.FindOnboarding(number)!;
        var task = record.Tasks.Single(x => x.Action == OnboardingActions.GrantAccess);
        Assert.Equal(system.Id, task.AccessResourceId);

        Assert.True(test.Store.SetOnboardingTaskDone(number, task.Id, true).Ok);
        var grant = test.Store.AccessGrants.Single(x => x.PersonId == record.StarterId);
        Assert.Equal((system.Id, number, Today), (grant.ResourceId, grant.OnboardingTicket!.Value, grant.GrantedOn!.Value));

        Assert.True(test.Store.SetOnboardingTaskDone(number, task.Id, false).Ok);
        Assert.DoesNotContain(test.Store.AccessGrants, x => x.PersonId == record.StarterId);
        // And the task survives a restart as an access task.
        Assert.Equal(system.Id, test.Reopen().FindOnboarding(number)!.Tasks.Single(x => x.Id == task.Id).AccessResourceId);
    }

    [Fact]
    public void Retention_waits_for_a_leavers_access_to_be_removed_and_subject_access_lists_it()
    {
        using var test = new TestStore();
        var person = test.AddRequester("Jo Smith");
        Grant(test, person.Id, System(test).Id);
        Assert.True(test.Store.UpdateUser(test.Store.Users.Single(x => x.Id == person.Id) with { IsActive = false, LeftAt = DateTime.UtcNow.AddYears(-3) }));
        test.Store.SetRetention(0, 12, 0);
        var preview = test.Store.PreviewRetention();
        Assert.Equal((0, 1), (preview.People, preview.LeaversWaiting));

        Assert.Single(test.Store.GatherSubjectAccess(person.Id)!.Access);
        test.Store.RevokeAllAccess(person.Id, null, null);
        Assert.Equal((1, 0), (test.Store.PreviewRetention().People, test.Store.PreviewRetention().LeaversWaiting));
    }
}
