namespace EduHelpdesk.Tests;

// The asset import making a column that matches nothing into a new custom attribute, the kind it guesses from the
// values, and the Date and Number kinds.
public class ImportAttributeTests
{
    private static readonly string[] Headers = ["Asset tag", "Make", "Model", "Type", "Battery health", "RAM (GB)", "Battery replaced"];

    private static string?[] Targets()
    {
        var targets = AssetImportTargets.Suggest(Headers, []);
        for (var i = 4; i < targets.Length; i++) targets[i] = AssetImportTargets.NewAttributeTarget;
        return targets;
    }

    [Theory]
    [InlineData("checkbox", "", "Yes", "No", "yes")]
    [InlineData("date", "", "01/09/2025", "14/02/2026", "")]
    [InlineData("number", "", "8", "16", "8.5")]
    [InlineData("dropdown", "Good, Poor", "Good", "Poor", "Good", "Poor")]
    [InlineData("single-line", "", "Room 4 trolley", "Mr Smith's desk", "Hall")]
    public void The_kind_is_guessed_from_the_values(string kind, string choices, params string[] values)
    {
        Assert.Equal((kind, choices), AssetImportTargets.SuggestKind(values, "dmy"));
    }

    [Fact]
    public void Long_text_suggests_multi_line_and_a_comma_rules_out_a_dropdown()
    {
        Assert.Equal("multi-line", AssetImportTargets.SuggestKind([new string('x', 150)], "dmy").Kind);
        Assert.Equal("single-line", AssetImportTargets.SuggestKind(["A, B", "A, B", "C", "C"], "dmy").Kind);
    }

    [Fact]
    public void New_attributes_are_made_only_when_the_import_runs_and_values_are_kept_in_their_kind()
    {
        using var test = new TestStore();
        var options = new AssetImportOptions("dmy", CreateMissing: true, BlankClears: false, null, null)
        {
            NewAttributes =
            [
                new(4, "Battery health", "dropdown", ["Laptop"], "Good, Poor"),
                new(5, "RAM (GB)", "number", [], ""),
                new(6, "Battery replaced", "date", ["Laptop", "Chromebook"], ""),
            ]
        };
        Assert.Null(test.Store.CheckNewAttributes(options.NewAttributes));
        string[][] rows =
        [
            ["TST-1", "Dell", "Latitude 5440", "Laptop", "Good", "16", "01/09/2025"],
            ["TST-2", "Dell", "Latitude 5440", "Chromebook", "", "8", "14/02/2026"],
        ];

        var preview = test.Store.PlanAssetImport(rows, Targets(), options);
        Assert.Equal(2, preview.Created);
        Assert.Contains(("Custom attribute", "Battery health (Dropdown, for Laptop)"), preview.NewValues.Select(x => (x.List, x.Value)));
        Assert.Contains(("Custom attribute", "RAM (GB) (Number, for every asset type)"), preview.NewValues.Select(x => (x.List, x.Value)));
        Assert.DoesNotContain(test.Store.AssetAttributeDefinitions, x => x.Name == "Battery health");

        test.Store.ApplyAssetImport(rows, Targets(), options, "attributes.csv");
        test.Reopen();
        var health = test.Store.AssetAttributeDefinitions.Single(x => x.Name == "Battery health");
        var ram = test.Store.AssetAttributeDefinitions.Single(x => x.Name == "RAM (GB)");
        var replaced = test.Store.AssetAttributeDefinitions.Single(x => x.Name == "Battery replaced");
        Assert.Equal(("dropdown", "Good,Poor"), (health.FieldType, health.Choices));
        Assert.Equal(["Laptop"], health.AssetTypes);
        Assert.Empty(ram.AssetTypes);
        // Chromebook came in with this file, so the date attribute keeps it.
        Assert.Equal(["Laptop", "Chromebook"], replaced.AssetTypes);

        var tst1 = test.Store.Assets.Single(x => x.AssetTag == "TST-1").Id;
        var tst2 = test.Store.Assets.Single(x => x.AssetTag == "TST-2").Id;
        string? Value(Guid asset, Guid definition) => test.Store.GetAssetAttributeValues(asset).FirstOrDefault(x => x.AttributeDefinitionId == definition)?.Value;
        Assert.Equal(("Good", "16", "2025-09-01"), (Value(tst1, health.Id), Value(tst1, ram.Id), Value(tst1, replaced.Id)));
        Assert.Equal(("8", "2026-02-14"), (Value(tst2, ram.Id), Value(tst2, replaced.Id)));
    }

    [Fact]
    public void Nothing_is_made_when_no_row_imports_and_bad_values_are_skipped_with_a_reason()
    {
        using var test = new TestStore();
        var options = new AssetImportOptions("dmy", CreateMissing: false, BlankClears: false, null, null)
        {
            NewAttributes = [new(5, "RAM (GB)", "number", [], "")]
        };
        var targets = Targets();
        targets[4] = null;
        targets[6] = null;
        string[][] rows = [["TST-1", "Nobody", "Latitude 5440", "Laptop", "", "lots", ""]];
        var plan = test.Store.ApplyAssetImport(rows, targets, options, "bad.csv");
        Assert.Equal(1, plan.Errors);
        Assert.DoesNotContain(test.Store.AssetAttributeDefinitions, x => x.Name == "RAM (GB)");

        rows = [["TST-1", "Dell", "Latitude 5440", "Laptop", "", "lots", ""]];
        Assert.Equal("'lots' is not a number for 'RAM (GB)'.", test.Store.PlanAssetImport(rows, targets, options).Results[0].Error);
    }

    [Fact]
    public void A_name_already_used_for_those_types_is_refused()
    {
        using var test = new TestStore();
        test.Store.AddAssetAttributeDefinition("Battery health", ["Laptop"], "single-line", null);
        Assert.Contains("already a custom attribute called 'battery health'", test.Store.CheckNewAttributes([new(4, "battery health", "single-line", [], "")]));
        Assert.Null(test.Store.CheckNewAttributes([new(4, "Battery health", "single-line", ["Tablet"], "")]));
        Assert.Contains("its choices", test.Store.CheckNewAttributes([new(4, "Grade", "dropdown", [], " , ")]));
    }

    [Fact]
    public void Settings_takes_date_and_number_attributes_and_the_asset_form_keeps_them_tidy()
    {
        using var test = new TestStore();
        Assert.Equal("Custom attribute added.", test.Store.AddAssetAttributeDefinition("Screen size", [], "number", null));
        Assert.Equal("Custom attribute added.", test.Store.AddAssetAttributeDefinition("Battery replaced", [], "date", null));
        var size = test.Store.AssetAttributeDefinitions.Single(x => x.Name == "Screen size");
        var date = test.Store.AssetAttributeDefinitions.Single(x => x.Name == "Battery replaced");
        var asset = test.Store.Assets[0];
        Assert.True(test.Store.UpdateAssetAttributeValues(asset.Id, asset.Type, new Dictionary<Guid, string> { [size.Id] = "15.6", [date.Id] = "2026-03-01" }));
        var values = test.Store.GetAssetAttributeValues(asset.Id).ToDictionary(x => x.AttributeDefinitionId, x => x.Value);
        Assert.Equal(("15.6", "2026-03-01"), (values[size.Id], values[date.Id]));
        Assert.True(test.Store.UpdateAssetAttributeValues(asset.Id, asset.Type, new Dictionary<Guid, string> { [size.Id] = "big", [date.Id] = "soon" }));
        Assert.DoesNotContain(test.Store.GetAssetAttributeValues(asset.Id), x => x.AttributeDefinitionId == size.Id || x.AttributeDefinitionId == date.Id);
    }
}
