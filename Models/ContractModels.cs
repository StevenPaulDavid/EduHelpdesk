namespace EduHelpdesk.Models;

// One line of the DfE digital technology contracts register (merged from EduInventory): what the school pays for, who
// supplies it, what it costs, and when it renews or has to be given notice. Field names follow the DfE template's
// columns; the date rules are in Services/ContractRules.cs.
public record ContractRecord(Guid Id, string Name, DateTime CreatedAt)
{
    public string Description { get; init; } = "";
    // From Settings lists: Contract types, Spend categories, Contract durations and Contract statuses.
    public string ContractType { get; init; } = "";
    public string SpendCategory { get; init; } = "";
    public string Duration { get; init; } = "";
    public string Status { get; init; } = ContractStatusDefaults.Active;
    public Guid? SupplierId { get; init; }
    public string SupplierContact { get; init; } = "";
    // DfE's template takes cost as free text ("£10,500 pa"). It is held as an amount and a period here so contracts
    // priced monthly, quarterly and yearly can be added up into an annual spend; CostNotes holds anything that doesn't
    // fit ("£875 year 1 only").
    public decimal? Cost { get; init; }
    public string CostPeriod { get; init; } = ContractCostPeriods.PerYear;
    public string CostNotes { get; init; } = "";
    public string RenewalType { get; init; } = "";
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public DateOnly? NextRenewalDate { get; init; }
    // Months of notice needed to renew or cancel, counted back from the renewal date (or the end date).
    public int? NoticeMonths { get; init; }
    // Who looks after it in school - free text, since it is often someone outside IT (the business manager, the SENCo).
    public string ContractOwner { get; init; } = "";
    public string ProcurementApproach { get; init; } = "";
    // Cyber security standard: "maintain a current list of approved applications on your contracts register".
    public bool ApprovedApp { get; init; }
    // Data held in the system belongs on the information asset register, with a DPIA.
    public bool ProcessesPersonalData { get; init; }
    // Academy Trust Handbook: a related party contract is reported to DfE before it starts or renews, and needs DfE's
    // approval above £40,000 in a financial year (ContractRules.RelatedPartyApprovalThreshold).
    public bool RelatedParty { get; init; }
    public DateOnly? RelatedPartyReportedOn { get; init; }
    public string Notes { get; init; } = "";
    // Set when the contract was added from an approved project's chosen quote, so the project shows it is on the register.
    public int? ProjectNumber { get; init; }
    public Guid? ProjectItemId { get; init; }
}

public static class ContractCostPeriods
{
    public const string PerMonth = "Per month";
    public const string PerQuarter = "Per quarter";
    public const string PerYear = "Per year";
    public const string Total = "Total for the contract";
    public const string OneOff = "One-off";
    public static readonly string[] All = [PerMonth, PerQuarter, PerYear, Total, OneOff];
    public static string Find(string? value) => All.FirstOrDefault(x => string.Equals(x, value?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? PerYear;
}

public static class ContractRenewalTypes
{
    public const string Automatic = "Renews automatically";
    public const string Manual = "Renew manually";
    public const string None = "Does not renew";
    public static readonly string[] All = [Automatic, Manual, None];
    public static string Find(string? value) => All.FirstOrDefault(x => string.Equals(x, value?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "";
}

// The DfE template's dropdown lists, as each list's starting values (Settings → Contract types and so on).
public static class ContractStatusDefaults
{
    public const string Active = "Active";
    // The one status the code relies on: an expired contract has ended, so it is left out of renewal and notice
    // warnings and of the annual spend. It is put back if removed, like the asset status Disposed.
    public const string Expired = "Expired";
    public static readonly string[] All = [Active, "Ends this year", "Expired but still using the service", Expired];
    public static readonly string[] Types = ["Licence", "Subscription", "Software", "Maintenance", "IT support", "Certificate", "Lease", "Broadband", "Telephony"];
    public static readonly string[] SpendCategories = ["Connectivity", "On-site servers", "IT learning resources", "Administration, software and systems", "Laptops, desktops and tablets", "Other hardware"];
    public static readonly string[] Durations = ["Monthly", "Quarterly", "Yearly", "2 yearly", "3 yearly", "5 yearly", "Rolling"];
}
