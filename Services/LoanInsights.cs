using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// Turns raw kit loans into the person-centric picture the loan report needs: who is borrowing, how often, and why.
// The existing asset loan history is asset-centric, which is no use for the conversation a line manager has to have.
public static class LoanInsights
{
    public sealed record BorrowerRow(string Name, Guid? UserId, IReadOnlyList<KitLoan> Loans)
    {
        public int Count => Loans.Count;
        public DateTime FirstIssued => Loans.Min(x => x.IssuedAt);
        public DateTime LastIssued => Loans.Max(x => x.IssuedAt);
        public int StillOut => Loans.Count(x => x.ReturnedAt is null);
        // Counted separately so legitimate loans don't inflate the number the manager sees.
        public int Flaggable(IReadOnlyCollection<string> excludedReasons) =>
            Loans.Count(x => !excludedReasons.Contains(x.Reason, StringComparer.OrdinalIgnoreCase));
        public IReadOnlyList<(string Reason, int Count)> ByReason => Loans
            .GroupBy(x => x.Reason, StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.Key, g.Count()))
            .OrderByDescending(x => x.Item2).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // A borrower is grouped by user id where there is one, and by trimmed name otherwise, so typed-in supply staff
    // still group together as long as the name is spelled the same way.
    public static string Key(KitLoan loan) =>
        loan.BorrowerUserId is { } id ? id.ToString() : "name:" + loan.BorrowerName.Trim().ToLowerInvariant();

    public static List<BorrowerRow> Borrowers(IEnumerable<KitLoan> loans) => loans
        .GroupBy(Key)
        .Select(g =>
        {
            var ordered = g.OrderByDescending(x => x.IssuedAt).ToList();
            return new BorrowerRow(ordered[0].BorrowerName, ordered[0].BorrowerUserId, ordered);
        })
        .OrderByDescending(x => x.Count).ThenByDescending(x => x.LastIssued).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    // Loans issued within the window, newest first.
    public static List<KitLoan> InPeriod(IEnumerable<KitLoan> loans, DateTime since) =>
        loans.Where(x => x.IssuedAt >= since).OrderByDescending(x => x.IssuedAt).ToList();

    public static List<KitLoan> CurrentlyOut(IEnumerable<KitLoan> loans) =>
        loans.Where(x => x.ReturnedAt is null).OrderBy(x => x.DueBack).ThenBy(x => x.IssuedAt).ToList();

    public static IReadOnlyList<(string Reason, int Count)> ByReason(IEnumerable<KitLoan> loans) => loans
        .GroupBy(x => x.Reason, StringComparer.OrdinalIgnoreCase)
        .Select(g => (g.Key, g.Count()))
        .OrderByDescending(x => x.Item2).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToList();

    // How long the kit was kept, or has been out so far.
    public static string Duration(KitLoan loan, DateTime now)
    {
        var span = (loan.ReturnedAt ?? now) - loan.IssuedAt;
        if (span.TotalHours < 1) return $"{Math.Max(1, (int)span.TotalMinutes)} min";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours} hr";
        var days = (int)span.TotalDays;
        return $"{days} day{(days == 1 ? "" : "s")}";
    }
}
