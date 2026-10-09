using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Tests;

// The DfE contracts register merged in from EduInventory: storage, the Settings lists, the date and money rules, the
// roles it brings, and adding a contract from an approved project.
public class ContractTests
{
    private static readonly DateOnly Today = AssetInsights.Today;

    private static ContractRecord Contract(string name = "Microsoft 365") => new(Guid.Empty, name, DateTime.UtcNow);

    [Fact]
    public void A_contract_survives_a_restart_with_every_field()
    {
        using var test = new TestStore();
        var supplier = new SupplierRecord(Guid.NewGuid(), "Acme IT", "", "", "", "", "", "", "", "", "", "", "", DateTime.UtcNow);
        test.Store.AddSupplier(supplier);
        var (ok, message, id) = test.Store.AddContract(Contract() with
        {
            Description = "A3 licences for staff", ContractType = "subscription", SpendCategory = "Administration, software and systems",
            Duration = "Yearly", Status = "Active", SupplierId = supplier.Id, SupplierContact = "Jo Bloggs", Cost = 1250.5m,
            CostPeriod = ContractCostPeriods.PerQuarter, CostNotes = "Ex VAT", RenewalType = ContractRenewalTypes.Automatic,
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 8, 31), NextRenewalDate = new DateOnly(2027, 9, 1),
            NoticeMonths = 3, ContractOwner = "Business manager", ProcurementApproach = "DfE framework", ApprovedApp = true,
            ProcessesPersonalData = true, RelatedParty = true, RelatedPartyReportedOn = new DateOnly(2026, 8, 1), Notes = "Renews each September"
        });
        Assert.True(ok, message);
        Assert.True(test.Store.UpdateContract(test.Store.FindContract(id!.Value)! with { CostNotes = "Excluding VAT" }).Ok);

        var saved = test.Reopen().FindContract(id.Value)!;
        Assert.Equal("Subscription", saved.ContractType); // matched to the list's spelling
        Assert.Equal((supplier.Id, 1250.50m, ContractCostPeriods.PerQuarter, "Excluding VAT"), (saved.SupplierId!.Value, saved.Cost!.Value, saved.CostPeriod, saved.CostNotes));
        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31), new DateOnly(2027, 9, 1), 3), (saved.StartDate!.Value, saved.EndDate!.Value, saved.NextRenewalDate!.Value, saved.NoticeMonths!.Value));
        Assert.True(saved.ApprovedApp && saved.ProcessesPersonalData && saved.RelatedParty);
        Assert.Equal(new DateOnly(2026, 8, 1), saved.RelatedPartyReportedOn);
        Assert.Equal(("Business manager", "DfE framework", "Renews each September", ContractRenewalTypes.Automatic), (saved.ContractOwner, saved.ProcurementApproach, saved.Notes, saved.RenewalType));
        Assert.Contains(test.Store.GetAuditEntries(), x => x.EntityType == "Contract" && x.EntityKey == id.ToString());
    }

    [Fact]
    public void Bad_contracts_are_refused()
    {
        using var test = new TestStore();
        Assert.False(test.Store.AddContract(Contract(" ")).Ok);
        Assert.False(test.Store.AddContract(Contract() with { ContractType = "Not a type" }).Ok);
        Assert.False(test.Store.AddContract(Contract() with { StartDate = Today, EndDate = Today.AddDays(-1) }).Ok);
        Assert.False(test.Store.AddContract(Contract() with { SupplierId = Guid.NewGuid() }).Ok);
        Assert.False(test.Store.AddContract(Contract() with { NoticeMonths = 61 }).Ok);
    }

    [Fact]
    public void A_new_install_has_the_DfE_lists_and_the_two_roles_and_an_upgrade_gets_them_once()
    {
        using var test = new TestStore();
        Assert.Contains("Licence", test.Store.ContractTypes);
        Assert.Contains("Connectivity", test.Store.SpendCategories);
        Assert.Contains(ContractStatusDefaults.Expired, test.Store.ContractStatuses);
        Assert.True(test.Store.RoleAllows(HelpdeskStore.BusinessManagerRole, Modules.Contracts, ModulePermission.Delete));
        Assert.True(test.Store.RoleAllows(HelpdeskStore.GovernorRole, Modules.Contracts, ModulePermission.View));
        Assert.False(test.Store.RoleAllows(HelpdeskStore.GovernorRole, Modules.Contracts, ModulePermission.Edit));
        Assert.False(test.Store.RoleAllows(HelpdeskStore.GovernorRole, Modules.Assets, ModulePermission.Edit));

        // A database from before the register: no lists, no roles, no version.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using (var connection = new SqliteConnection($"Data Source={test.DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM ContractTypes; DELETE FROM ContractStatuses;
                DELETE FROM RolePermissions WHERE RoleName IN ('Business manager', 'Governor or auditor');
                DELETE FROM Roles WHERE Name IN ('Business manager', 'Governor or auditor');
                DELETE FROM Metadata WHERE Key = 'ComplianceVersion';
                """;
            command.ExecuteNonQuery();
        }
        var store = test.Reopen();
        Assert.Contains("Licence", store.ContractTypes);
        Assert.Equal(ContractStatusDefaults.All, store.ContractStatuses);
        Assert.Contains(store.Roles, x => x.Name == HelpdeskStore.BusinessManagerRole);

        // Once in place, a school's own choices stick: a deleted starting value doesn't come back.
        Assert.Equal("Contract type deleted.", store.DeleteManagedOption("ContractTypes", "Telephony"));
        Assert.DoesNotContain("Telephony", test.Reopen().ContractTypes);
    }

    [Fact]
    public void List_values_cascade_and_Expired_is_protected()
    {
        using var test = new TestStore();
        var (_, _, id) = test.Store.AddContract(Contract() with { ContractType = "Licence" });
        Assert.Contains("in use", test.Store.DeleteManagedOption("ContractTypes", "Licence"));
        test.Store.UpdateManagedOption("ContractTypes", "Licence", "Licences");
        Assert.Equal("Licences", test.Store.FindContract(id!.Value)!.ContractType);
        Assert.Contains("can't be renamed", test.Store.UpdateManagedOption("ContractStatuses", "Expired", "Finished"));
        Assert.Contains("can't be renamed", test.Store.DeleteManagedOption("ContractStatuses", "Expired"));
    }

    [Fact]
    public void Renewal_notice_and_end_dates_follow_the_DfE_template()
    {
        var renewsIn20Days = Contract() with { NextRenewalDate = Today.AddDays(20) };
        var renewsIn2Months = Contract() with { NextRenewalDate = Today.AddMonths(2) };
        var noticeSoon = Contract() with { NextRenewalDate = Today.AddMonths(3).AddDays(10), NoticeMonths = 3 };
        var ended = Contract() with { EndDate = Today.AddDays(-1) };
        var expired = ended with { Status = ContractStatusDefaults.Expired, NextRenewalDate = Today };

        Assert.Equal(("Needs renewing in a month", "badge-warning"), (ContractRules.Renewal(renewsIn20Days, Today)!.Text, ContractRules.Renewal(renewsIn20Days, Today)!.Tone));
        Assert.Equal("Needs renewing in 2 to 3 months", ContractRules.Renewal(renewsIn2Months, Today)!.Text);
        Assert.Equal(Today.AddDays(10), ContractRules.NoticeDate(noticeSoon));
        Assert.Equal("Notice date is in a month", ContractRules.Notice(noticeSoon, Today)!.Text);
        Assert.Equal("Past its end date", ContractRules.End(ended, Today)!.Text);
        // Expired contracts have finished: nothing to warn about.
        Assert.Empty(ContractRules.Flags(expired, Today));
    }

    [Fact]
    public void Costs_add_up_to_a_year()
    {
        Assert.Equal(1200m, ContractRules.AnnualCost(Contract() with { Cost = 100m, CostPeriod = ContractCostPeriods.PerMonth }));
        Assert.Equal(400m, ContractRules.AnnualCost(Contract() with { Cost = 100m, CostPeriod = ContractCostPeriods.PerQuarter }));
        Assert.Null(ContractRules.AnnualCost(Contract() with { Cost = 100m, CostPeriod = ContractCostPeriods.OneOff }));
        var threeYears = Contract() with { Cost = 3000m, CostPeriod = ContractCostPeriods.Total, StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2028, 12, 31) };
        Assert.InRange(ContractRules.AnnualCost(threeYears)!.Value, 999m, 1001m);
    }

    [Fact]
    public void An_approved_projects_chosen_quote_fills_in_a_contract()
    {
        using var test = new TestStore();
        var supplier = new SupplierRecord(Guid.NewGuid(), "Lease Co", "", "", "", "", "", "", "", "", "", "", "", DateTime.UtcNow);
        var monthly = new ItemSupplier(supplier.Id) { Reference = "Q-77", PaymentLines = [new PaymentLine(Guid.NewGuid(), "Lease", 250m, PaymentFrequencies.Monthly, 3, VatTreatments.Standard)] };
        var item = new ProjectItem(Guid.NewGuid(), "Photocopier lease", 1) { Suppliers = [monthly], ChosenSupplierId = supplier.Id };
        var project = new ProjectRecord(12, "New copier", Guid.NewGuid(), Today, "A copier", DateTime.UtcNow) { Items = [item] };

        var contract = ContractRules.FromQuote(project, item, monthly, new DateOnly(2026, 10, 1), ContractStatusDefaults.Durations);
        Assert.Equal((250m, ContractCostPeriods.PerMonth, "3 yearly"), (contract.Cost!.Value, contract.CostPeriod, contract.Duration));
        Assert.Equal(new DateOnly(2029, 9, 30), contract.EndDate);
        Assert.Equal((12, item.Id, supplier.Id), (contract.ProjectNumber!.Value, contract.ProjectItemId!.Value, contract.SupplierId!.Value));
        Assert.Contains("Q-77", contract.Notes);

        // One-off and recurring together: the whole contract's total, broken down in the notes.
        var mixed = monthly with { PaymentLines = [.. monthly.PaymentLines, new PaymentLine(Guid.NewGuid(), "Install", 300m, PaymentFrequencies.OneOff, 1, VatTreatments.Standard)] };
        var total = ContractRules.FromQuote(project, item, mixed, new DateOnly(2026, 10, 1), ContractStatusDefaults.Durations);
        Assert.Equal((250m * 36 + 300m, ContractCostPeriods.Total), (total.Cost!.Value, total.CostPeriod));
        Assert.Contains("Install", total.CostNotes);

        // Only from a project that was approved.
        Assert.NotNull(test.Store.ContractFromProject(999, item.Id).Error);
    }

    [Fact]
    public void Removing_a_contract_unlinks_its_assets_and_a_supplier_on_the_register_stays()
    {
        using var test = new TestStore();
        var supplier = new SupplierRecord(Guid.NewGuid(), "Lease Co", "", "", "", "", "", "", "", "", "", "", "", DateTime.UtcNow);
        test.Store.AddSupplier(supplier);
        var (_, _, id) = test.Store.AddContract(Contract("Copier lease") with { SupplierId = supplier.Id });
        var asset = new AssetRecord(Guid.NewGuid(), "TST-L1", "Ricoh", "IM C3000", "Printer", "SN-L1", "", null) { Ownership = AssetOwnership.Leased, ContractId = id };
        test.Store.AddAsset(asset);
        Assert.Equal(id, test.Reopen().Assets.Single(x => x.Id == asset.Id).ContractId);

        Assert.NotNull(test.Store.DeleteSupplier(supplier.Id));
        Assert.Null(test.Store.DeleteContract(id!.Value));
        var unlinked = test.Reopen().Assets.Single(x => x.Id == asset.Id);
        Assert.Null(unlinked.ContractId);
        Assert.Contains(unlinked.History, x => x.Action == "Contract changed");
    }
}
