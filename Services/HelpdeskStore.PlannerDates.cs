using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// The other dates the planner shows beside tickets (planner stage 4): recurring tickets and premises checks still to
// be logged, cleaning rounds, loans due back, new starters, contract dates and asset checks. Which of these a person
// may see is the page's to decide (Pages/Jobs: permissions, features, desks); this only works out what falls when.
public static class PlannerKinds
{
    public const string Recurring = "recurring";
    public const string Rounds = "rounds";
    public const string Loans = "loans";
    public const string Starters = "starters";
    public const string Contracts = "contracts";
    public const string Checks = "checks";

    public static readonly string[] All = [Recurring, Rounds, Loans, Starters, Contracts, Checks];

    public static string Label(string kind) => kind switch
    {
        Recurring => "Recurring work",
        Rounds => "Cleaning rounds",
        Loans => "Loans due back",
        Starters => "New starters",
        Contracts => "Contract dates",
        Checks => "Asset checks",
        _ => kind
    };
}

// One thing on one day. Text is what the planner shows; Detail its fuller wording. DeskId is the desk it belongs to, or
// null for something every desk shares. TechnicianId and ForPerson put recurring work in its technician's row (or
// Unassigned) on the week; everything else goes in the row of other dates. RecordId and Number say what it links to.
public sealed record PlannerDate(DateOnly Date, string Kind, string Text, string Detail, Guid? DeskId = null,
    bool ForPerson = false, Guid? TechnicianId = null, Guid? RecordId = null, int? Number = null, int Count = 1, bool Warning = false);

public sealed partial class HelpdeskStore
{
    public IReadOnlyList<PlannerDate> PlannerDates(DateOnly from, DateOnly to, DateTime now)
    {
        if (to < from) return [];
        var culture = System.Globalization.CultureInfo.GetCultureInfo("en-GB");
        var today = DateOnly.FromDateTime(now);
        lock (_sync)
        {
            var dates = new List<PlannerDate>();

            // Recurring tickets and premises checks, on the days they'll be logged: only what is still to come, and once
            // a day however many dates move onto it. A paused schedule logs nothing.
            var schoolDays = _data.SchoolDays;
            foreach (var item in _data.RecurringTickets.Where(x => x.IsActive))
            {
                var deskId = DeskForCategoryCore(item.Category).Id;
                var seen = new HashSet<DateOnly>();
                for (var day = from.AddDays(-7); day <= to; day = day.AddDays(1))
                {
                    if (!Recurrence.Occurs(item.Rule, day)) continue;
                    var on = item.Rule.MoveToSchoolDay ? Recurrence.OnSchoolDay(day, schoolDays) : day;
                    var at = on.ToDateTime(item.Rule.At);
                    if (on < from || on > to || at <= now || at <= item.CountedFrom || !seen.Add(on)) continue;
                    var check = item.Checklist.Count > 0;
                    dates.Add(new(on, PlannerKinds.Recurring, (check ? "Check: " : "To log: ") + item.Title,
                        $"{(check ? "Premises check" : "Recurring ticket")} \"{item.Name}\", logged at {item.Rule.At.ToString("HH:mm", culture)}. It's skipped if the last one is still open.",
                        deskId, ForPerson: true, TechnicianId: item.TechnicianId, RecordId: item.Id));
                }
            }

            // Cleaning rounds, on each of their days since they started.
            foreach (var round in _data.CleaningRounds.Where(x => x.IsActive))
                for (var day = from; day <= to; day = day.AddDays(1))
                    if (round.Days.Contains(day.DayOfWeek) && day >= round.StartedOn)
                        dates.Add(new(day, PlannerKinds.Rounds, $"Round: {round.Name}",
                            $"{round.Name}: {round.Rooms.Count} room{(round.Rooms.Count == 1 ? "" : "s")}{(string.IsNullOrWhiteSpace(round.Cleaner) ? "" : $", usually {round.Cleaner}")}.",
                            RecordId: round.Id));

            // Loans still out, on the day they're due back. Assets lent from their own page count too, as on the Loans
            // page; those that came with a kit are the kit's.
            foreach (var loan in _data.KitLoans.Where(x => x.IsOut && x.DueBack >= from && x.DueBack <= to))
            {
                var kit = _data.LoanKits.FirstOrDefault(x => x.Id == loan.KitId)?.Name ?? "Kit";
                dates.Add(new(loan.DueBack, PlannerKinds.Loans, $"Back: {kit}", $"{kit}, lent to {loan.BorrowerName}, is due back.", Warning: loan.IsOverdue(today), RecordId: loan.Id));
            }
            foreach (var asset in _data.Assets)
                foreach (var lent in asset.Assignments.Where(x => x.EndedAt is null && x.KitLoanId is null && x.DueBack is { } due && due >= from && due <= to))
                    dates.Add(new(lent.DueBack!.Value, PlannerKinds.Loans, $"Back: {asset.AssetTag}", $"{asset.AssetTag} ({asset.Model}), lent to {lent.UserName}, is due back.",
                        DeskForAssetTypeCore(asset.Type), Warning: lent.DueBack < today, RecordId: asset.Id));

            // New starters' first days.
            foreach (var starter in _data.Onboardings.Where(x => !x.IsCancelled && x.StartDate >= from && x.StartDate <= to))
            {
                var name = _data.Users.FirstOrDefault(x => x.Id == starter.StarterId)?.Name ?? "New starter";
                var ticket = _data.Tickets.FirstOrDefault(x => x.Number == starter.TicketNumber);
                var left = starter.Tasks.Count(x => !x.IsDone);
                dates.Add(new(starter.StartDate, PlannerKinds.Starters, $"Starts: {name}",
                    $"{name}{(string.IsNullOrWhiteSpace(starter.JobTitle) ? "" : $", {starter.JobTitle},")} starts. {(left == 0 ? "Every onboarding task is done." : $"{left} onboarding task{(left == 1 ? "" : "s")} still to do.")}",
                    ticket is null ? null : DeskForCategoryCore(ticket.Category).Id, Number: starter.TicketNumber, Warning: left > 0 && starter.StartDate <= today.AddDays(7)));
            }

            // Contracts still running: the notice date, the renewal, and the end when it isn't the same day as the renewal.
            foreach (var contract in _data.Contracts.Where(ContractRules.IsLive))
            {
                void Add(DateOnly? day, string what, string detail)
                {
                    if (day is { } on && on >= from && on <= to)
                        dates.Add(new(on, PlannerKinds.Contracts, $"{what}: {contract.Name}", detail, RecordId: contract.Id, Warning: what == "Notice by"));
                }
                Add(ContractRules.NoticeDate(contract), "Notice by", $"Last day to give notice to renew or cancel {contract.Name}.");
                Add(contract.NextRenewalDate, "Renews", $"{contract.Name} renews.");
                if (contract.EndDate != contract.NextRenewalDate) Add(contract.EndDate, "Ends", $"{contract.Name} ends.");
            }

            // Asset checks and the end of support, one line a day for each desk: a walk-round can make dozens due at once.
            var live = _data.Assets.Where(x => !IsDisposed(x)).ToList();
            void Group(Func<AssetRecord, DateOnly?> dateOf, string one, string many, string detail)
            {
                foreach (var group in live.Where(x => dateOf(x) is { } on && on >= from && on <= to)
                             .GroupBy(x => (Day: dateOf(x)!.Value, Desk: DeskForAssetTypeCore(x.Type))))
                {
                    var assets = group.OrderBy(x => x.AssetTag, NaturalComparer.Instance).ToList();
                    var single = assets.Count == 1 ? assets[0] : null;
                    dates.Add(new(group.Key.Day, PlannerKinds.Checks, single is null ? $"{assets.Count} {many}" : $"{one}: {single.AssetTag}",
                        string.Format(culture, detail, string.Join(", ", assets.Take(12).Select(x => x.AssetTag)) + (assets.Count > 12 ? $" and {assets.Count - 12} more" : "")),
                        group.Key.Desk, RecordId: single?.Id, Count: assets.Count, Warning: group.Key.Day < today));
                }
            }
            Group(x => x.NextCheckDate, "Check due", "asset checks due", "Next check due: {0}.");
            Group(x => x.EndOfSupport, "Support ends", "assets lose support", "Support or lease ends: {0}.");

            var order = PlannerKinds.All.ToList();
            return dates.OrderBy(x => x.Date).ThenBy(x => order.IndexOf(x.Kind)).ThenBy(x => x.Text, NaturalComparer.Instance).ToList();
        }
    }

    private Guid DeskForAssetTypeCore(string? type) =>
        type is not null && _data.AssetTypeDesks.TryGetValue(type.Trim(), out var id) && _data.Desks.Any(x => x.Id == id) ? id : _data.Desks[0].Id;
}
