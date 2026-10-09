namespace EduHelpdesk.Tests;

// The DfE digital technology asset register fields merged in from EduInventory: building and room, condition, ownership,
// checks and end of support, and the disposal evidence.
public class AssetComplianceTests
{
    private static readonly DateOnly Today = AssetInsights.Today;

    private static AssetRecord Laptop(string tag = "TST-1") =>
        new(Guid.NewGuid(), tag, "Dell", "Latitude 5440", "Laptop", "SN-" + tag, "Room 1", null);

    [Fact]
    public void Every_DfE_field_survives_a_restart()
    {
        using var test = new TestStore();
        Assert.Equal("Building added.", test.Store.AddManagedOption("Buildings", "Main block"));
        var asset = Laptop() with
        {
            Building = "Main block", OperatingSystem = "Windows 11 Pro", Condition = "Refurbished", Ownership = AssetOwnership.Leased,
            LastCheckDate = Today.AddDays(-10), LastCheckBy = "Sam Tech", NextCheckDate = Today.AddMonths(11), EndOfSupport = Today.AddYears(2)
        };
        test.Store.AddAsset(asset);
        // Saved twice so the second goes through the changes-only path, not the full rewrite.
        Assert.True(test.Store.UpdateAsset(test.Store.Assets.Single(x => x.Id == asset.Id) with { OperatingSystem = "Windows 11 Pro 24H2" }));

        var saved = test.Reopen().Assets.Single(x => x.Id == asset.Id);
        Assert.Equal("Main block", saved.Building);
        Assert.Equal("Room 1", saved.Location);
        Assert.Equal("Windows 11 Pro 24H2", saved.OperatingSystem);
        Assert.Equal("Refurbished", saved.Condition);
        Assert.Equal(AssetOwnership.Leased, saved.Ownership);
        Assert.Equal(Today.AddDays(-10), saved.LastCheckDate);
        Assert.Equal("Sam Tech", saved.LastCheckBy);
        Assert.Equal(Today.AddMonths(11), saved.NextCheckDate);
        Assert.Equal(Today.AddYears(2), saved.EndOfSupport);
        // Nothing else moved: the reader is by column position, so a misplaced column would scramble these.
        Assert.Equal(("Dell", "Latitude 5440", "Laptop", "SN-TST-1"), (saved.Make, saved.Model, saved.Type, saved.SerialNumber));
    }

    [Fact]
    public void Conditions_start_with_the_DfE_values_and_buildings_are_a_managed_list()
    {
        using var test = new TestStore();
        Assert.Equal(["New", "Used", "Refurbished", "Donated"], test.Store.AssetConditions);
        Assert.Empty(test.Store.Buildings);

        test.Store.AddManagedOption("Buildings", "Science block");
        var asset = Laptop() with { Building = "Science block" };
        test.Store.AddAsset(asset);
        Assert.Contains("in use", test.Store.DeleteManagedOption("Buildings", "Science block"));
        test.Store.UpdateManagedOption("Buildings", "Science block", "STEM block");
        Assert.Equal("STEM block", test.Store.Assets.Single(x => x.Id == asset.Id).Building);

        // An emptied condition list gets its starting values back on the next start.
        foreach (var condition in test.Store.AssetConditions.ToList()) test.Store.DeleteManagedOption("AssetConditions", condition);
        Assert.Empty(test.Store.AssetConditions);
        Assert.Equal(4, test.Reopen().AssetConditions.Count);
    }

    [Fact]
    public void Checks_and_end_of_support_join_the_review_list()
    {
        var checks = new AssetCheckSettings(30, 180, 12);
        var overdue = Laptop("A") with { NextCheckDate = Today.AddDays(-1) };
        var soon = Laptop("B") with { NextCheckDate = Today.AddDays(20) };
        var later = Laptop("C") with { NextCheckDate = Today.AddDays(60), EndOfSupport = Today.AddYears(1) };
        var unsupported = Laptop("D") with { EndOfSupport = Today.AddDays(-5) };
        var leaseEnding = Laptop("E") with { EndOfSupport = Today.AddDays(90), Ownership = AssetOwnership.Leased };
        var disposed = Laptop("F") with { NextCheckDate = Today.AddDays(-100), Status = HelpdeskStore.DisposedStatus, DisposalDate = Today };

        var items = AssetInsights.ReviewItems([overdue, soon, later, unsupported, leaseEnding, disposed], new Dictionary<string, int>(), 60, Today, null, checks);
        var reasons = items.ToDictionary(x => x.Asset.AssetTag, x => x.Reasons.Single());

        Assert.Equal(["A", "B", "D", "E"], reasons.Keys.Order());
        Assert.Equal(("Check", true), (reasons["A"].Kind, reasons["A"].Overdue));
        Assert.Equal(("Check", false), (reasons["B"].Kind, reasons["B"].Overdue));
        Assert.StartsWith("Unsupported since", reasons["D"].Text);
        Assert.StartsWith("Lease ending", reasons["E"].Text);
    }

    [Fact]
    public void Recording_a_check_sets_the_next_one_and_skips_disposed_kit()
    {
        using var test = new TestStore();
        var live = Laptop("A");
        var gone = Laptop("B");
        test.Store.AddAsset(live);
        test.Store.AddAsset(gone);
        Assert.True(test.Store.DisposeAsset(gone.Id, Today, "Recycled (WEEE)", null, "GreenTech Recycling", "WEEE-0042").Ok);
        Assert.Equal("Check and support windows saved.", test.Store.SetAssetCheckSettings(30, 180, 6));

        Assert.NotNull(test.Store.RecordAssetChecks([live.Id], Today.AddDays(1), "Sam").Error);
        Assert.NotNull(test.Store.RecordAssetChecks([live.Id], Today, " ").Error);
        var (count, unchanged, skipped, error) = test.Store.RecordAssetChecks([live.Id, gone.Id], Today.AddDays(-2), "Sam Tech");
        Assert.Null(error);
        Assert.Equal((1, 0, 1), (count, unchanged, skipped));
        var again = test.Store.RecordAssetChecks([live.Id, gone.Id], Today.AddDays(-2), "Sam Tech");
        Assert.Equal((0, 1, 1), (again.Checked, again.Unchanged, again.Skipped));

        var store = test.Reopen();
        var checkedAsset = store.Assets.Single(x => x.Id == live.Id);
        Assert.Equal(Today.AddDays(-2), checkedAsset.LastCheckDate);
        Assert.Equal("Sam Tech", checkedAsset.LastCheckBy);
        Assert.Equal(Today.AddDays(-2).AddMonths(6), checkedAsset.NextCheckDate);
        Assert.Contains(checkedAsset.History, x => x.Action == "Check recorded");

        var disposed = store.Assets.Single(x => x.Id == gone.Id);
        Assert.Null(disposed.LastCheckDate);
        Assert.Equal(("GreenTech Recycling", "WEEE-0042"), (disposed.DisposedBy, disposed.DisposalCertificate));
        Assert.Contains(disposed.History, x => x.Action == "Disposed" && x.Details.Contains("WEEE-0042"));
    }

    [Fact]
    public void The_list_filters_on_building_checks_and_support()
    {
        var a = Laptop("A") with { Building = "Main", NextCheckDate = Today.AddDays(-3) };
        var b = Laptop("B") with { Building = "Annexe", EndOfSupport = Today.AddDays(30) };
        var c = Laptop("C");
        List<string> Run(AssetListQuery query) => query.Run([a, b, c], [], new Dictionary<string, int>(), 60, Today).Select(x => x.AssetTag).ToList();

        Assert.Equal(["A"], Run(new AssetListQuery { Building = "main" }));
        Assert.Equal(["C"], Run(new AssetListQuery { Building = AssetListQuery.None }));
        Assert.Equal(["A"], Run(new AssetListQuery { Flag = "check" }));
        Assert.Equal(["B"], Run(new AssetListQuery { Flag = "support" }));
        Assert.Equal(["A", "B"], Run(new AssetListQuery { Flag = "review" }));
    }

    [Fact]
    public void A_DfE_template_import_maps_its_columns_and_fills_the_fields()
    {
        using var test = new TestStore();
        test.Store.AddAsset(Laptop("TST-1"));
        string[] headers = ["Asset number", "Building", "Room", "Operating system", "Condition", "Owned by school", "Date of last check", "Checked by", "Date of next check", "End of support date"];
        var targets = AssetImportTargets.Suggest(headers, []);
        Assert.Equal(["tag", "building", "location", "os", "condition", "ownership", "lastCheck", "lastCheckBy", "nextCheck", "endOfSupport"], targets.Select(x => x ?? ""));

        string[] row = ["TST-1", "Main block", "Room 1", "Windows 11", "Used", "No", "01/09/2026", "Sam Tech", "01/09/2027", "14/10/2025"];
        var plan = test.Store.ApplyAssetImport([row], targets, new AssetImportOptions("dmy", CreateMissing: true, BlankClears: false, null, null), "test.csv");
        Assert.Equal(1, plan.Updated);

        var asset = test.Store.Assets.Single(x => x.AssetTag == "TST-1");
        Assert.Equal(("Main block", "Windows 11", "Used", AssetOwnership.Leased), (asset.Building, asset.OperatingSystem, asset.Condition, asset.Ownership));
        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2027, 9, 1), new DateOnly(2025, 10, 14)), (asset.LastCheckDate!.Value, asset.NextCheckDate!.Value, asset.EndOfSupport!.Value));
        Assert.Contains("Main block", test.Store.Buildings);
    }

    [Fact]
    public void The_CSV_export_carries_the_DfE_columns()
    {
        var asset = Laptop() with { Building = "Main", Ownership = AssetOwnership.Loaned, EndOfSupport = new DateOnly(2027, 1, 31), DisposalCertificate = "C-1" };
        var lines = AssetCsv.Build([asset], [], [], [], [], new Dictionary<string, int>()).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var header = lines[0].Split(',');
        var cells = lines[1].Split(',');
        string Cell(string name) => cells[Array.IndexOf(header, name)];
        Assert.Equal("Main", Cell("Building"));
        Assert.Equal("Loaned", Cell("Ownership"));
        Assert.Equal("2027-01-31", Cell("End of support"));
        Assert.Equal("C-1", Cell("Disposal certificate"));
    }
}
