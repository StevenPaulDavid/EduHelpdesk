using EduHelpdesk.Models;
using EduHelpdesk.Services;

namespace EduHelpdesk.Pages.Contracts;

// The contract form as posted (Pages/Contracts/_ContractForm.cshtml), shared by adding on /Contracts/Edit and editing
// in the drawer on /Contract. A mutable class so Razor Pages can bind it; ContractRecord itself stays immutable.
public sealed class ContractInput
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? ContractType { get; set; }
    public string? SpendCategory { get; set; }
    public string? Duration { get; set; }
    public string? Status { get; set; }
    public Guid? SupplierId { get; set; }
    public string? SupplierContact { get; set; }
    // Text, so "1,250.00" and a stray "£" read the same way the asset forms read prices.
    public string? Cost { get; set; }
    public string? CostPeriod { get; set; }
    public string? CostNotes { get; set; }
    public string? RenewalType { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public DateOnly? NextRenewalDate { get; set; }
    public int? NoticeMonths { get; set; }
    public string? ContractOwner { get; set; }
    public string? ProcurementApproach { get; set; }
    public bool ApprovedApp { get; set; }
    public bool ProcessesPersonalData { get; set; }
    public bool RelatedParty { get; set; }
    public DateOnly? RelatedPartyReportedOn { get; set; }
    public string? Notes { get; set; }
    // Carried through the form when it was filled in from an approved project.
    public int? ProjectNumber { get; set; }
    public Guid? ProjectItemId { get; set; }

    public static ContractInput From(ContractRecord x) => new()
    {
        Name = x.Name, Description = x.Description, ContractType = x.ContractType, SpendCategory = x.SpendCategory,
        Duration = x.Duration, Status = x.Status, SupplierId = x.SupplierId, SupplierContact = x.SupplierContact,
        Cost = x.Cost?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), CostPeriod = x.CostPeriod, CostNotes = x.CostNotes,
        RenewalType = x.RenewalType, StartDate = x.StartDate, EndDate = x.EndDate, NextRenewalDate = x.NextRenewalDate,
        NoticeMonths = x.NoticeMonths, ContractOwner = x.ContractOwner, ProcurementApproach = x.ProcurementApproach,
        ApprovedApp = x.ApprovedApp, ProcessesPersonalData = x.ProcessesPersonalData, RelatedParty = x.RelatedParty,
        RelatedPartyReportedOn = x.RelatedPartyReportedOn, Notes = x.Notes, ProjectNumber = x.ProjectNumber, ProjectItemId = x.ProjectItemId
    };

    // The record to hand the store, which checks the rest. Null with a message if the cost can't be read.
    public ContractRecord? ToRecord(Guid id, out string? error)
    {
        error = null;
        if (!AssetForm.TryPrice(Cost?.Replace("£", "").Replace(",", ""), out var cost))
        {
            error = "Enter the cost as a positive amount, such as 1250.00, or leave it blank.";
            return null;
        }
        return new ContractRecord(id, Name ?? "", DateTime.UtcNow)
        {
            Description = Description ?? "", ContractType = ContractType ?? "", SpendCategory = SpendCategory ?? "",
            Duration = Duration ?? "", Status = Status ?? "", SupplierId = SupplierId, SupplierContact = SupplierContact ?? "",
            Cost = cost, CostPeriod = CostPeriod ?? "", CostNotes = CostNotes ?? "", RenewalType = RenewalType ?? "",
            StartDate = StartDate, EndDate = EndDate, NextRenewalDate = NextRenewalDate, NoticeMonths = NoticeMonths,
            ContractOwner = ContractOwner ?? "", ProcurementApproach = ProcurementApproach ?? "",
            ApprovedApp = ApprovedApp, ProcessesPersonalData = ProcessesPersonalData, RelatedParty = RelatedParty,
            RelatedPartyReportedOn = RelatedPartyReportedOn, Notes = Notes ?? "", ProjectNumber = ProjectNumber, ProjectItemId = ProjectItemId
        };
    }
}

// What the form partial needs: the values, and the lists its dropdowns offer.
public sealed record ContractForm(ContractInput Input, IReadOnlyList<string> Types, IReadOnlyList<string> SpendCategories,
    IReadOnlyList<string> Durations, IReadOnlyList<string> Statuses, IReadOnlyList<SupplierRecord> Suppliers)
{
    public static ContractForm For(HelpdeskStore store, ContractInput input) =>
        new(input, store.ContractTypes, store.SpendCategories, store.ContractDurations, store.ContractStatuses,
            store.Suppliers.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList());

    // A saved value that has since left its list is still offered, so saving keeps it.
    public static IReadOnlyList<string> With(IReadOnlyList<string> list, string? current) =>
        string.IsNullOrWhiteSpace(current) || list.Contains(current, StringComparer.OrdinalIgnoreCase) ? list : [.. list, current];
}
