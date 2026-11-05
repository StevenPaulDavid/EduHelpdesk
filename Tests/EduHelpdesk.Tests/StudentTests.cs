namespace EduHelpdesk.Tests;

// Devices given to pupils who aren't in People (HelpdeskStore.Students): the With Student status and a typed name, an
// ownership period like anyone else's, and never a loan.
public class StudentTests
{
    private static AssetRecord Laptop(string tag) =>
        new(Guid.NewGuid(), tag, "Dell", "Latitude 5440", "Laptop", "SN-" + tag, "", null) { Status = "In stock or spare" };

    [Fact]
    public void A_device_given_to_a_student_has_their_name_and_is_never_a_loan()
    {
        using var test = new TestStore();
        Assert.Contains(HelpdeskStore.WithStudentStatus, test.Store.AssetStatuses);
        var asset = Laptop("TST-1");
        test.Store.AddAsset(asset);
        var loansBefore = test.Store.AllLoans().Count;

        Assert.False(test.Store.GiveToStudent(asset.Id, " ").Ok);
        var (ok, message) = test.Store.GiveToStudent(asset.Id, " Amira Khan ");
        Assert.True(ok, message);
        var given = test.Store.Assets.Single(x => x.Id == asset.Id);
        Assert.Equal(("Amira Khan", HelpdeskStore.WithStudentStatus, (Guid?)null, (DateOnly?)null), (given.StudentName, given.Status, given.AssignedUserId, given.LoanDueDate));
        var period = Assert.Single(given.Assignments);
        Assert.Equal(("Amira Khan", (DateOnly?)null, (DateTime?)null), (period.UserName, period.DueBack, period.EndedAt));
        Assert.Contains(given.History, x => x.Action == "Given to student");
        Assert.Equal(loansBefore, test.Store.AllLoans().Count);

        // Held, so it can't be lent or given to someone else until it's back.
        Assert.False(test.Store.LoanAsset(asset.Id, test.Store.Users[0].Id, DateOnly.FromDateTime(DateTime.Today).AddDays(1), test.Store.LoanReasons[0]).Ok);
        Assert.False(test.Store.GiveToStudent(asset.Id, "Someone Else").Ok);

        test.Reopen();
        Assert.Equal("Amira Khan", test.Store.Assets.Single(x => x.Id == asset.Id).StudentName);

        Assert.True(test.Store.BackFromStudent(asset.Id).Ok);
        var back = test.Store.Assets.Single(x => x.Id == asset.Id);
        Assert.Equal(("", "In stock or spare"), (back.StudentName, back.Status));
        Assert.NotNull(back.Assignments.Single().EndedAt);
        Assert.Contains(back.History, x => x.Action == "Back from student");
        Assert.False(test.Store.BackFromStudent(asset.Id).Ok);
    }

    [Fact]
    public void Changing_the_status_or_assigning_someone_in_People_ends_it()
    {
        using var test = new TestStore();
        var asset = Laptop("TST-2");
        test.Store.AddAsset(asset);
        Assert.True(test.Store.GiveToStudent(asset.Id, "Amira Khan").Ok);
        var given = test.Store.Assets.Single(x => x.Id == asset.Id);
        Assert.True(test.Store.UpdateAsset(given with { Status = "In repair" }));
        Assert.Equal("", test.Store.Assets.Single(x => x.Id == asset.Id).StudentName);

        Assert.True(test.Store.GiveToStudent(asset.Id, "Amira Khan").Ok);
        given = test.Store.Assets.Single(x => x.Id == asset.Id);
        Assert.True(test.Store.UpdateAsset(given with { AssignedUserId = test.Store.Users[0].Id }));
        var now = test.Store.Assets.Single(x => x.Id == asset.Id);
        Assert.Equal("", now.StudentName);
        Assert.Equal(test.Store.Users[0].Id, now.Assignments.Single(x => x.EndedAt is null).UserId);
    }

    [Fact]
    public void The_status_cannot_be_renamed()
    {
        using var test = new TestStore();
        Assert.Contains("can't be renamed", test.Store.UpdateManagedOption("Asset status", HelpdeskStore.WithStudentStatus, "1:1 device"));
        Assert.Contains(HelpdeskStore.WithStudentStatus, test.Store.AssetStatuses);
    }

    [Fact]
    public void An_import_with_the_status_takes_a_typed_name_and_other_rows_still_need_People()
    {
        using var test = new TestStore();
        string[] headers = ["Asset tag", "Make", "Model", "Type", "Status", "Assigned to", "Loan due back"];
        var targets = AssetImportTargets.Suggest(headers, []);
        var options = new AssetImportOptions("dmy", CreateMissing: true, BlankClears: false, null, null);
        string[][] rows =
        [
            ["TST-10", "Dell", "Latitude 5440", "Laptop", "with student", "Amira Khan", ""],
            ["TST-11", "Dell", "Latitude 5440", "Laptop", "In use", "Nobody Known", ""],
            ["TST-12", "Dell", "Latitude 5440", "Laptop", "With Student", "Ben Ode", "01/12/2026"],
        ];
        var plan = test.Store.ApplyAssetImport(rows, targets, options, "students.csv");
        Assert.Equal(1, plan.Created);
        Assert.Equal("No user matches 'Nobody Known'.", plan.Results[1].Error);
        Assert.Contains("isn't a loan", plan.Results[2].Error);
        var imported = test.Store.Assets.Single(x => x.AssetTag == "TST-10");
        Assert.Equal(("Amira Khan", HelpdeskStore.WithStudentStatus), (imported.StudentName, imported.Status));
        Assert.Equal("Amira Khan", imported.Assignments.Single().UserName);
        Assert.DoesNotContain(test.Store.Users, x => x.Name == "Amira Khan");

        // Imported again with the asset moved back to stock: the student's period ends.
        string[][] again = [["TST-10", "Dell", "Latitude 5440", "Laptop", "In stock or spare", "", ""]];
        var update = test.Store.PlanAssetImport(again, targets, options);
        Assert.Contains(update.Results[0].Changes, x => x.StartsWith("Student: Amira Khan →"));
        test.Store.ApplyAssetImport(again, targets, options, "back.csv");
        Assert.Equal("", test.Store.Assets.Single(x => x.AssetTag == "TST-10").StudentName);
    }

    [Fact]
    public void The_asset_list_finds_and_filters_students()
    {
        using var test = new TestStore();
        var asset = Laptop("TST-3");
        test.Store.AddAsset(asset);
        test.Store.AddAsset(Laptop("TST-4"));
        Assert.True(test.Store.GiveToStudent(asset.Id, "Amira Khan").Ok);
        List<string> Run(AssetListQuery query) => query.Run(test.Store.Assets, test.Store.Users, new Dictionary<string, int>(), 60, DateOnly.FromDateTime(DateTime.Today))
            .Where(x => x.AssetTag.StartsWith("TST-")).Select(x => x.AssetTag).ToList();
        Assert.Equal(["TST-3"], Run(new AssetListQuery { Search = "amira" }));
        Assert.Equal(["TST-3"], Run(new AssetListQuery { Holder = AssetListQuery.StudentsHolder }));
        Assert.Equal(["TST-4"], Run(new AssetListQuery { Holder = "none" }));
    }
}
