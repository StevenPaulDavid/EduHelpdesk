namespace EduHelpdesk.Tests;

// The asset import's built-in numbers for rows with no tag (HelpdeskStore.AssetNumbers), matching those rows by serial
// number, and the Contract column with the contracts it adds.
public class AssetImportExtrasTests
{
    private static AssetImportOptions Options(bool number = true, bool createMissing = true) =>
        new("dmy", createMissing, BlankClears: false, DefaultType: "Laptop", DefaultStatus: null, NumberBlankTags: number);

    private static readonly string[] Headers = ["Asset tag", "Make", "Model", "Serial number", "Contract", "Supplier"];
    private static string?[] Targets => AssetImportTargets.Suggest(Headers, []);

    [Fact]
    public void The_contract_heading_is_recognised()
    {
        Assert.Equal(["tag", "make", "model", "serial", "contract", "supplier"], Targets.Select(x => x ?? ""));
    }

    [Fact]
    public void Rows_with_no_tag_get_the_next_number_and_are_matched_by_serial_next_time()
    {
        using var test = new TestStore();
        Assert.Equal("Asset numbers saved. The next one will be SCH-001.", test.Store.SetAssetNumbering("SCH-", 3));
        string[][] rows =
        [
            ["", "Dell", "Latitude 5440", "SN-A", "", ""],
            ["", "Dell", "Latitude 5440", "SN-B", "", ""],
            ["SCH-007", "Dell", "Latitude 5440", "SN-C", "", ""],
        ];
        var preview = test.Store.PlanAssetImport(rows, Targets, Options());
        // The typed SCH-007 further down is counted, so generated numbers start after it.
        Assert.Equal(["SCH-008", "SCH-009", "SCH-007"], preview.Results.Select(x => x.Tag));

        var applied = test.Store.ApplyAssetImport(rows, Targets, Options(), "first.csv");
        Assert.Equal(3, applied.Created);
        Assert.Equal("SCH-008", test.Store.Assets.Single(x => x.SerialNumber == "SN-A").AssetTag);
        Assert.Equal("SCH-010", test.Store.NextAssetTag);

        // The same file again: matched by serial, nothing new.
        var again = test.Store.PlanAssetImport(rows, Targets, Options());
        Assert.Equal(0, again.Created);
        Assert.Equal(3, again.Unchanged);

        // A deleted number is never handed out again, even after a restart.
        Assert.Null(test.Store.DeleteAsset(test.Store.Assets.Single(x => x.AssetTag == "SCH-009").Id));
        Assert.Null(test.Store.DeleteAsset(test.Store.Assets.Single(x => x.AssetTag == "SCH-008").Id));
        Assert.Null(test.Store.DeleteAsset(test.Store.Assets.Single(x => x.AssetTag == "SCH-007").Id));
        test.Reopen();
        Assert.Equal("SCH-010", test.Store.NextAssetTag);
    }

    [Fact]
    public void Without_the_tick_a_blank_tag_is_still_skipped()
    {
        using var test = new TestStore();
        string[][] rows = [["", "Dell", "Latitude 5440", "SN-A", "", ""]];
        var plan = test.Store.PlanAssetImport(rows, Targets, Options(number: false));
        Assert.Equal(1, plan.Errors);
        Assert.Equal("The asset tag is blank.", plan.Results[0].Error);
    }

    [Fact]
    public void A_file_with_no_tag_column_can_be_numbered_and_warns_when_there_is_no_serial_either()
    {
        using var test = new TestStore();
        string[] headers = ["Make", "Model", "Serial number"];
        var targets = AssetImportTargets.Suggest(headers, []);
        string[][] rows = [["Dell", "Latitude 5440", ""], ["Dell", "Latitude 5440", "SN-X"], ["Dell", "Latitude 5440", "SN-X"]];
        var plan = test.Store.PlanAssetImport(rows, targets, Options());
        Assert.Equal(2, plan.Created);
        Assert.Contains(plan.Results[0].Warnings, x => x.Contains("importing this file again will add it again"));
        Assert.Empty(plan.Results[1].Warnings);
        Assert.Contains("also on row 3", plan.Results[2].Error);
        Assert.Equal(ImportAction.Error, plan.Results[2].Action);
    }

    [Fact]
    public void A_contract_not_on_the_register_is_added_once_and_needs_its_details()
    {
        using var test = new TestStore();
        var (_, _, existing) = test.Store.AddContract(new ContractRecord(Guid.Empty, "Broadband", DateTime.UtcNow));
        string[][] rows =
        [
            ["TST-1", "Dell", "Latitude 5440", "SN-1", "Laptop lease 2026", "Lease Co"],
            ["TST-2", "Dell", "Latitude 5440", "SN-2", "laptop lease 2026 ", ""],
            ["TST-3", "Dell", "Latitude 5440", "SN-3", "BROADBAND", ""],
        ];
        var preview = test.Store.PlanAssetImport(rows, Targets, Options());
        Assert.Contains((ListName: "Contract", Value: "Laptop lease 2026"), preview.NewValues.Select(x => (x.List, x.Value)));
        Assert.Contains(preview.Results[0].Changes, x => x == "Contract: Laptop lease 2026 (new)");
        Assert.DoesNotContain(test.Store.Contracts, x => x.Name == "Laptop lease 2026");

        var applied = test.Store.ApplyAssetImport(rows, Targets, Options(), "contracts.csv");
        Assert.Equal(3, applied.Created);
        var lease = test.Store.Contracts.Single(x => x.Name == "Laptop lease 2026");
        Assert.True(lease.NeedsDetails);
        Assert.Equal(test.Store.Suppliers.Single(x => x.Name == "Lease Co").Id, lease.SupplierId);
        Assert.Equal(lease.Id, test.Store.Assets.Single(x => x.AssetTag == "TST-2").ContractId);
        Assert.Equal(existing, test.Store.Assets.Single(x => x.AssetTag == "TST-3").ContractId);
        Assert.Contains(ComplianceFindings.For(test.Store, DateOnly.FromDateTime(DateTime.Today)), x => x.Issue == "Needs its details" && x.Subject == lease.Name);

        test.Reopen();
        lease = test.Store.Contracts.Single(x => x.Name == "Laptop lease 2026");
        Assert.True(lease.NeedsDetails);
        // Saved from its page, even unchanged, it no longer needs its details.
        Assert.True(test.Store.UpdateContract(lease with { NeedsDetails = true }).Ok);
        Assert.False(test.Store.Contracts.Single(x => x.Id == lease.Id).NeedsDetails);
        Assert.DoesNotContain(ComplianceFindings.For(test.Store, DateOnly.FromDateTime(DateTime.Today)), x => x.Issue == "Needs its details");
    }

    [Fact]
    public void Without_adding_missing_values_an_unknown_contract_is_skipped()
    {
        using var test = new TestStore();
        string[][] rows = [["TST-1", "Dell", "Latitude 5440", "SN-1", "Nowhere", ""]];
        var plan = test.Store.PlanAssetImport(rows, Targets, Options(createMissing: false));
        Assert.Equal("Contract 'Nowhere' is not on the contracts register.", plan.Results[0].Error);
    }

    [Fact]
    public void The_prefix_is_checked()
    {
        using var test = new TestStore();
        Assert.StartsWith("Use only", test.Store.SetAssetNumbering("A B", 5));
        Assert.StartsWith("Choose between", test.Store.SetAssetNumbering("A-", 0));
        Assert.Equal("AST-00001", test.Store.NextAssetTag);
    }
}
