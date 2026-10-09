using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// The contracts register's date and money rules, merged from EduInventory's Compliance class. The wording and the
// thresholds are the DfE contracts register template's own ("needs renewing in a month", "notice date is in a month"),
// so the list, the contract page and the exports can never disagree about what is due.
public static class ContractRules
{
    // Academy Trust Handbook: related party contracts need DfE's prior approval above £40,000 in a financial year.
    public const decimal RelatedPartyApprovalThreshold = 40_000m;

    // Tone is the badge class: red (badge-warning), amber (badge-soon) or plain for information.
    public sealed record Flag(string Kind, string Text, string Tone, DateOnly Date);

    // An expired contract has ended: it drops out of the warnings and the annual spend but stays on the register.
    public static bool IsLive(ContractRecord contract) =>
        !string.Equals(contract.Status, ContractStatusDefaults.Expired, StringComparison.OrdinalIgnoreCase);

    // The date the school next has to act by: the renewal date if there is one, otherwise the end date.
    public static DateOnly? ActionDate(ContractRecord contract) => contract.NextRenewalDate ?? contract.EndDate;

    public static DateOnly? NoticeDate(ContractRecord contract) =>
        contract.NoticeMonths is > 0 && ActionDate(contract) is { } date ? date.AddMonths(-contract.NoticeMonths.Value) : null;

    // DfE: red "needs renewing in a month"; amber "needs renewing in 2 to 3 months". A live contract whose renewal date
    // has passed is shown too, because it means the register wasn't updated when it renewed.
    public static Flag? Renewal(ContractRecord contract, DateOnly today)
    {
        if (!IsLive(contract) || contract.NextRenewalDate is not { } renewal) return null;
        if (renewal < today) return new("Renewal", "Renewal date passed - update the register", "", renewal);
        if (renewal <= today.AddMonths(1)) return new("Renewal", "Needs renewing in a month", "badge-warning", renewal);
        if (renewal <= today.AddMonths(3)) return new("Renewal", "Needs renewing in 2 to 3 months", "badge-soon", renewal);
        return null;
    }

    // DfE: red "notice date for renewal or cancellation is in a month". Once it has passed, the chance to cancel or
    // renegotiate has gone for this cycle - worth knowing for a contract that renews itself.
    public static Flag? Notice(ContractRecord contract, DateOnly today)
    {
        if (!IsLive(contract) || NoticeDate(contract) is not { } notice || ActionDate(contract) is not { } action || action < today) return null;
        if (notice < today) return new("Notice", "Notice date passed", "", notice);
        if (notice <= today.AddMonths(1)) return new("Notice", "Notice date is in a month", "badge-warning", notice);
        return null;
    }

    // DfE: the end date turns red when the contract is due to end within the next month.
    public static Flag? End(ContractRecord contract, DateOnly today)
    {
        if (!IsLive(contract) || contract.EndDate is not { } end) return null;
        if (end < today) return new("End", "Past its end date", "badge-warning", end);
        if (end <= today.AddMonths(1)) return new("End", "Ends within a month", "badge-warning", end);
        return null;
    }

    // Academy Trust Handbook: reported to DfE before it starts or renews, and approved above the threshold.
    public static Flag? RelatedPartyCheck(ContractRecord contract)
    {
        if (!contract.RelatedParty || !IsLive(contract)) return null;
        if (contract.RelatedPartyReportedOn is null) return new("Related party", "Related party - not yet reported to DfE", "badge-warning", contract.StartDate ?? DateOnly.MinValue);
        if (AnnualCost(contract) >= RelatedPartyApprovalThreshold) return new("Related party", "Related party over £40,000 - needs DfE approval", "badge-soon", contract.StartDate ?? DateOnly.MinValue);
        return null;
    }

    // Everything on one contract worth acting on, soonest first.
    public static List<Flag> Flags(ContractRecord contract, DateOnly today) =>
        new[] { Notice(contract, today), Renewal(contract, today), End(contract, today), RelatedPartyCheck(contract) }
            .OfType<Flag>().OrderBy(x => x.Date).ToList();

    // What the contract costs over a year, so contracts priced monthly, quarterly and yearly add up. A total for the
    // whole contract is spread over its length; a one-off purchase is not a yearly cost and returns null.
    public static decimal? AnnualCost(ContractRecord contract)
    {
        if (contract.Cost is not { } cost) return null;
        return contract.CostPeriod switch
        {
            ContractCostPeriods.PerMonth => cost * 12,
            ContractCostPeriods.PerQuarter => cost * 4,
            ContractCostPeriods.PerYear => cost,
            ContractCostPeriods.Total when contract.StartDate is { } s && contract.EndDate is { } e && e > s =>
                Math.Round(cost / Math.Max(1m, (e.DayNumber - s.DayNumber + 1) / 365.25m), 2),
            ContractCostPeriods.Total => cost,
            _ => null
        };
    }

    public static string DescribeCost(ContractRecord contract) => contract.Cost is { } cost
        ? $"{HelpdeskStore.FormatMoney(cost)} {contract.CostPeriod.ToLowerInvariant()}"
        : "Not recorded";

    // A new contract filled in from the quote chosen for one item of an approved project. Quote prices exclude VAT, as
    // the project page shows them, and the contract keeps them that way (said in the cost notes).
    public static ContractRecord FromQuote(ProjectRecord project, ProjectItem item, ItemSupplier quote, DateOnly start, IReadOnlyList<string> durations)
    {
        var lines = quote.PaymentLines;
        var recurring = lines.Where(x => PaymentFrequencies.PerYear(x.Frequency) > 0).ToList();
        var oneOff = lines.Where(x => PaymentFrequencies.PerYear(x.Frequency) == 0).ToList();
        var years = recurring.Select(x => x.TermYears).DefaultIfEmpty(0).Max();
        decimal? cost;
        string period;
        var notes = new List<string>();
        if (recurring.Count > 0 && oneOff.Count == 0 && recurring.Select(x => x.Frequency).Distinct().Count() == 1)
        {
            cost = recurring.Sum(x => x.Amount);
            period = recurring[0].Frequency switch
            {
                PaymentFrequencies.Monthly => ContractCostPeriods.PerMonth,
                PaymentFrequencies.Quarterly => ContractCostPeriods.PerQuarter,
                _ => ContractCostPeriods.PerYear
            };
        }
        else if (recurring.Count == 0)
        {
            cost = lines.Count == 0 ? null : oneOff.Sum(x => x.Amount);
            period = ContractCostPeriods.OneOff;
        }
        else
        {
            // A mix of one-off and recurring payments: the whole contract's total, with the breakdown kept in the notes.
            cost = lines.Sum(x => x.TermExVat);
            period = ContractCostPeriods.Total;
            notes.Add(string.Join("; ", lines.Select(x => $"{x.Description} {HelpdeskStore.FormatMoney(x.Amount)} {x.Frequency.ToLowerInvariant()}{(x.PaymentCount > 1 ? $" for {x.TermYears} year{(x.TermYears == 1 ? "" : "s")}" : "")}")));
        }
        notes.Add("Excluding VAT.");
        var duration = years switch { 1 => "Yearly", > 1 => $"{years} yearly", _ => "" };
        var quotes = item.Suppliers.Count(x => x.CountsAsQuote);
        return new ContractRecord(Guid.NewGuid(), item.Name, DateTime.UtcNow)
        {
            Description = $"{item.Quantity} × {item.Name}, from {project.Reference} {project.Title}",
            SupplierId = quote.SupplierId,
            Cost = cost,
            CostPeriod = period,
            CostNotes = string.Join(" ", notes),
            Duration = durations.FirstOrDefault(x => string.Equals(x, duration, StringComparison.OrdinalIgnoreCase)) ?? "",
            StartDate = start,
            EndDate = years > 0 ? start.AddYears(years).AddDays(-1) : null,
            ProcurementApproach = $"{quotes} quote{(quotes == 1 ? "" : "s")} compared ({project.Reference})",
            Notes = string.IsNullOrWhiteSpace(quote.Reference) ? "" : $"Supplier's quote reference {quote.Reference}.",
            ProjectNumber = project.Number,
            ProjectItemId = item.Id
        };
    }
}
