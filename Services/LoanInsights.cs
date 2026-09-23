using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// Turns loans into the person-centric picture the loan report needs: who is borrowing, how often, and why.
// Both kinds of loan feed in through LoanEntry - a kit handed over, and a single asset lent from its own page - because
// a teacher who has been given a spare laptop three times has been given one three times either way, and a report that
// counted only kits would under-report exactly the behaviour it exists to catch.
public static class LoanInsights
{
    public const string KitKind = "Kit";
    public const string AssetKind = "Asset";
    public const string ReasonNotRecorded = "Not recorded";

    // One loan of either kind, flattened. IssuedAt is null for holders recorded before assignments were tracked.
    // CountsTowardFlag is false for anything with no reason or no start date: those rows are shown honestly but must
    // never put somebody on a list a line manager acts on.
    public sealed record LoanEntry(
        string Kind,
        string What,
        Guid? BorrowerUserId,
        string BorrowerName,
        string? Reason,
        DateTime? IssuedAt,
        DateOnly? DueBack,
        DateTime? ReturnedAt)
    {
        public bool CountsTowardFlag => Reason is not null && IssuedAt is not null;
        public bool IsOut => ReturnedAt is null;
        public string ReasonLabel => Reason ?? ReasonNotRecorded;
        public bool IsOverdue(DateOnly today) => ReturnedAt is null && DueBack is { } due && due < today;
        // Sorts unknown start dates as oldest, so they only surface under "All time" rather than in a recent window.
        public DateTime SortDate => IssuedAt ?? DateTime.MinValue;
    }

    public static LoanEntry From(KitLoan loan, string kitName) =>
        new(KitKind, kitName, loan.BorrowerUserId, loan.BorrowerName, loan.Reason, loan.IssuedAt, loan.DueBack, loan.ReturnedAt);

    public static LoanEntry From(AssetAssignment assignment, string assetTag) =>
        new(AssetKind, assetTag, assignment.UserId, assignment.UserName, assignment.Reason, assignment.StartedAt, assignment.DueBack, assignment.EndedAt);

    public sealed record BorrowerRow(string Name, Guid? UserId, IReadOnlyList<LoanEntry> Loans)
    {
        public int Count => Loans.Count;
        // Only loans that count toward flagging, which is what the repeat-borrower threshold is measured against.
        public int CountedLoans => Loans.Count(x => x.CountsTowardFlag);
        public DateTime FirstIssued => Loans.Min(x => x.SortDate);
        public DateTime LastIssued => Loans.Max(x => x.SortDate);
        public int StillOut => Loans.Count(x => x.IsOut);
        public IReadOnlyList<(string Reason, int Count)> ByReason => Loans
            .GroupBy(x => x.ReasonLabel, StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.Key, g.Count()))
            .OrderByDescending(x => x.Item2).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // A borrower is grouped by user id where there is one, and by trimmed name otherwise, so typed-in supply staff
    // still group together as long as the name is spelled the same way.
    public static string Key(LoanEntry loan) =>
        loan.BorrowerUserId is { } id ? id.ToString() : "name:" + loan.BorrowerName.Trim().ToLowerInvariant();

    public static List<BorrowerRow> Borrowers(IEnumerable<LoanEntry> loans) => loans
        .GroupBy(Key)
        .Select(g =>
        {
            var ordered = g.OrderByDescending(x => x.SortDate).ToList();
            return new BorrowerRow(ordered[0].BorrowerName, ordered[0].BorrowerUserId, ordered);
        })
        .OrderByDescending(x => x.Count).ThenByDescending(x => x.LastIssued).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    // Loans issued within the window, newest first. Entries with no recorded start date only appear when since is
    // DateTime.MinValue, i.e. all time.
    public static List<LoanEntry> InPeriod(IEnumerable<LoanEntry> loans, DateTime since) =>
        loans.Where(x => x.SortDate >= since).OrderByDescending(x => x.SortDate).ToList();

    public static List<LoanEntry> CurrentlyOut(IEnumerable<LoanEntry> loans) =>
        loans.Where(x => x.IsOut).OrderBy(x => x.DueBack ?? DateOnly.MaxValue).ThenByDescending(x => x.SortDate).ToList();

    public static IReadOnlyList<(string Reason, int Count)> ByReason(IEnumerable<LoanEntry> loans) => loans
        .GroupBy(x => x.ReasonLabel, StringComparer.OrdinalIgnoreCase)
        .Select(g => (g.Key, g.Count()))
        .OrderByDescending(x => x.Item2).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToList();

    // How long it was kept, or has been out so far.
    public static string Duration(LoanEntry loan, DateTime now)
    {
        if (loan.IssuedAt is not { } issued) return "unknown";
        var span = (loan.ReturnedAt ?? now) - issued;
        if (span.TotalHours < 1) return $"{Math.Max(1, (int)span.TotalMinutes)} min";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours} hr";
        var days = (int)span.TotalDays;
        return $"{days} day{(days == 1 ? "" : "s")}";
    }

    // The kit desk screen still works in KitLoan terms, so it keeps a direct overload.
    public static string Duration(KitLoan loan, DateTime now) =>
        Duration(new LoanEntry(KitKind, "", loan.BorrowerUserId, loan.BorrowerName, loan.Reason, loan.IssuedAt, loan.DueBack, loan.ReturnedAt), now);
}
