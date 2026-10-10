using System.Text;

namespace EduHelpdesk.Tests;

// Stage 4 of the EduInventory merge: the DfE templates, exactly as downloaded, must import; the registers export in the
// DfE layout and import back unchanged; a staff list updates People; and the compliance findings and reminders.
public class ComplianceTests
{
    private static readonly DateOnly Today = AssetInsights.Today;
    private static byte[] TestFile(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", name));

    private static SheetTable Read(string kind, byte[] bytes, string fileName) =>
        SpreadsheetReader.Read(bytes, fileName, HelpdeskStore.ImportHeaders(kind), HelpdeskStore.ImportSheetNames(kind));

    [Fact]
    public void The_DfE_contracts_template_imports_with_costs_understood_and_its_examples_held_back()
    {
        using var test = new TestStore();
        var table = Read(HelpdeskStore.ContractImport, TestFile("Digital_technology_contracts_register_template.xlsx"), "contracts.xlsx");
        var preview = test.Store.PreviewRegisterImport(HelpdeskStore.ContractImport, table);
        Assert.Equal(8, preview.Rows.Count);
        Assert.All(preview.Rows, r => Assert.NotNull(r.HeldBack));
        Assert.All(preview.Rows, r => Assert.Equal(RegisterImportActions.Add, r.Action));

        var (ok, message) = test.Store.ApplyRegisterImport(HelpdeskStore.ContractImport, table, preview.Rows.Select(r => r.Number).ToHashSet());
        Assert.True(ok, message);
        var broadband = test.Store.Contracts.Single(x => x.Name == "Broadband");
        Assert.Equal((10_500m, ContractCostPeriods.PerYear, ContractRenewalTypes.Manual, 3), (broadband.Cost!.Value, broadband.CostPeriod, broadband.RenewalType, broadband.NoticeMonths!.Value));
        Assert.Equal(2_400m, ContractRules.AnnualCost(test.Store.Contracts.Single(x => x.Name == "Printers (MFD)")));
        var zeTriangle = test.Store.Contracts.Single(x => x.Name == "ZeTriangle");
        Assert.Equal(875m, zeTriangle.Cost);
        Assert.Contains("£175", zeTriangle.CostNotes);
    }

    [Fact]
    public void The_contracts_register_exports_in_the_DfE_layout_and_imports_back_unchanged()
    {
        using var test = new TestStore();
        var template = Read(HelpdeskStore.ContractImport, TestFile("Digital_technology_contracts_register_template.xlsx"), "contracts.xlsx");
        test.Store.ApplyRegisterImport(HelpdeskStore.ContractImport, template, template.Rows.Select(r => r.Number).ToHashSet());

        var export = RegisterExports.ContractsRegister(test.Store, Today);
        Assert.Equal("Name of contract or service", export.Headers[0]);
        foreach (var (bytes, name) in new[] { (RegisterExports.ToXlsx(export), "export.xlsx"), (RegisterExports.ToCsv(export), "export.csv") })
        {
            var preview = test.Store.PreviewRegisterImport(HelpdeskStore.ContractImport, Read(HelpdeskStore.ContractImport, bytes, name));
            Assert.All(preview.Rows, r => Assert.True(r.Action == RegisterImportActions.NoChange, $"{name} row {r.Number} {r.Key}: {r.Action} {r.Detail}"));
        }
    }

    [Fact]
    public void The_DfE_asset_template_is_found_and_its_hidden_examples_left_out()
    {
        var table = SpreadsheetReader.Read(TestFile("Digital_technology_asset_register_template.xlsx"), "assets.xlsx", RegisterExports.AssetHeaders, "Digital technology assets");
        Assert.Contains("Asset number", table.Headers.Select(x => x.Trim()));
        Assert.Equal(6, table.Rows.Count);
        var serial = table.Headers.FindIndex(x => SpreadsheetReader.NormaliseHeader(x) == "serial number");
        Assert.All(table.Rows, r => Assert.Contains(r.Cell(serial), RegisterExports.ExampleAssetSerials, StringComparer.OrdinalIgnoreCase));

        // The DfE headings map to the importer's fields without anyone choosing them.
        var targets = AssetImportTargets.Suggest(table.Headers, []);
        string? Target(string heading) => targets[table.Headers.FindIndex(x => SpreadsheetReader.NormaliseHeader(x) == SpreadsheetReader.NormaliseHeader(heading))];
        Assert.Equal(("tag", "make", "model", "condition", "building", "location", "endOfSupport", "ownership"),
            (Target("Asset number"), Target("Make of asset"), Target("Model of asset"), Target("Condition of asset"), Target("Location: Building"),
             Target("Location: Room"), Target("When does the asset expire or become unsupported?"), Target("Is this asset leased or loaned?")));
    }

    [Fact]
    public void The_asset_register_export_is_a_valid_workbook_with_the_DfE_headings_first()
    {
        using var test = new TestStore();
        test.Store.AddAsset(new AssetRecord(Guid.NewGuid(), "=SUM(A1)", "Dell", "Latitude", "Laptop", "SN1", "Room 1", null) { Ownership = AssetOwnership.Leased, PurchaseDate = new DateOnly(2025, 1, 2) });
        var table = RegisterExports.AssetRegister(test.Store, Today);
        Assert.Equal(("Asset type", "Additional comments"), (table.Headers[0], table.Headers[23]));
        using var stream = new MemoryStream(RegisterExports.ToXlsx(table));
        using var document = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(stream, false);
        Assert.Empty(new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(document));
        var csv = Encoding.UTF8.GetString(RegisterExports.ToCsv(table));
        // A cell starting with = must not become a formula when the CSV is opened in Excel; a leased asset's price is "Leased".
        Assert.Contains("'=SUM(A1)", csv);
        Assert.Contains(",Leased,", csv);
        using var access = new MemoryStream(RegisterExports.ToXlsx(RegisterExports.AccessRegister(test.Store, Today)));
        using var accessDocument = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(access, false);
        Assert.Empty(new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(accessDocument));
    }

    [Fact]
    public void A_staff_list_adds_people_then_updates_them_and_marks_leavers()
    {
        using var test = new TestStore();
        var csv = "Name,Email,Type,Department,Start date\r\nAlex Pike,alex.pike@school.example,Staff,Science,01/09/2024\r\n\"Lee, Jordan\",jordan.lee@school.example,Contractor,,\r\n";
        var table = Read(HelpdeskStore.StaffImport, Encoding.UTF8.GetBytes(csv), "staff.csv");
        var preview = test.Store.PreviewRegisterImport(HelpdeskStore.StaffImport, table);
        Assert.Equal(2, preview.Count(RegisterImportActions.Add));
        Assert.True(test.Store.ApplyRegisterImport(HelpdeskStore.StaffImport, table, preview.Rows.Select(x => x.Number).ToHashSet()).Ok);
        var alex = test.Store.Users.Single(x => x.Email == "alex.pike@school.example");
        Assert.Equal((new DateOnly(2024, 9, 1), "Science", true), (alex.StartDate!.Value, alex.Department, alex.IsActive));
        Assert.Equal("Lee, Jordan", test.Store.Users.Single(x => x.Email == "jordan.lee@school.example").Name);

        // Next term's export: Alex has left, Jordan unchanged.
        var again = Read(HelpdeskStore.StaffImport, Encoding.UTF8.GetBytes(csv.Replace("Start date", "Start date,Leave date").Replace("01/09/2024", "01/09/2024,20/07/2026").Replace("Contractor,,", "Contractor,,,")), "staff.csv");
        var second = test.Store.PreviewRegisterImport(HelpdeskStore.StaffImport, again);
        Assert.Equal((RegisterImportActions.Update, RegisterImportActions.NoChange), (second.Rows[0].Action, second.Rows[1].Action));
        Assert.Contains("Left", second.Rows[0].Detail);
        test.Store.ApplyRegisterImport(HelpdeskStore.StaffImport, again, second.Rows.Select(x => x.Number).ToHashSet());
        var left = test.Reopen().Users.Single(x => x.Id == alex.Id);
        Assert.False(left.IsActive);
        Assert.Equal(new DateOnly(2026, 7, 20), AccessRules.LeaveDate(left));
    }

    [Fact]
    public void Findings_cover_all_three_registers_and_the_bell_rings_only_for_what_is_new()
    {
        using var test = new TestStore();
        test.Store.AddAsset(new AssetRecord(Guid.NewGuid(), "TST-C1", "Dell", "Latitude", "Laptop", "SN-C1", "", null) { NextCheckDate = Today.AddDays(-1) });
        test.Store.AddContract(new ContractRecord(Guid.Empty, "Broadband", DateTime.UtcNow) { NextRenewalDate = Today.AddDays(40), NoticeMonths = 1 });
        var person = test.AddRequester("Jo Smith");
        var (_, _, system) = test.Store.SaveAccessResource(new AccessResource(Guid.Empty, "SIMS", DateTime.UtcNow));
        test.Store.AddAccessGrant(new AccessGrant(Guid.Empty, person.Id, system!.Value, DateTime.UtcNow) { GrantedOn = Today, Mfa = MfaStates.Enabled });
        test.Store.UpdateUser(test.Store.Users.Single(x => x.Id == person.Id) with { IsActive = false });

        var findings = ComplianceFindings.For(test.Store, Today);
        Assert.Contains(findings, x => x.Register == ComplianceFindings.Assets && x.Issue == "Check overdue");
        Assert.Contains(findings, x => x.Register == ComplianceFindings.Contracts && x.Issue == "Notice date is in a month");
        Assert.Contains(findings, x => x.Register == ComplianceFindings.Access && x.Issue == "Leaver still has access");
        Assert.Contains(findings, x => x.Issue == "Termly access review due");
        Assert.Equal(ComplianceFinding.Levels.Danger, findings[0].Level);

        // The Administrator can see every register, so is told; a second run with nothing new tells nobody.
        var now = DateTime.Today.AddHours(9);
        Assert.True(test.Store.SendComplianceReminders(now, force: true) >= 1);
        Assert.Equal(0, test.Store.SendComplianceReminders(now, force: true));
        // Not before seven, and once a day.
        Assert.Equal(0, test.Store.SendComplianceReminders(DateTime.Today.AddDays(1).AddHours(6)));
        // Something new is due: the bell rings again, and survives a restart without ringing for the old ones.
        test.Store.AddContract(new ContractRecord(Guid.Empty, "Phones", DateTime.UtcNow) { EndDate = Today.AddDays(5) });
        Assert.True(test.Reopen().SendComplianceReminders(now, force: true) >= 1);
        var admin = test.Store.Technicians.First(x => x.Role == StaffRoles.Administrator);
        Assert.Contains(test.Store.InboxFor(admin.Id, broadcast: false, take: 10).Entries, x => x.Notification.Kind == HelpdeskStore.NotificationKinds.Compliance);
    }
}
