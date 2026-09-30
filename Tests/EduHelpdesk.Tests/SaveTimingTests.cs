using System.Diagnostics;
using Xunit.Abstractions;

namespace EduHelpdesk.Tests;

// How long changes take on a real-sized database, measured the way someone waiting would: the whole call, save
// included. Off unless EDUHELPDESK_TIMING_DB names a database to copy, because it is a measurement, not a check:
//   $env:EDUHELPDESK_TIMING_DB = "C:\path\helpdesk.db"; dotnet test --filter SaveTimingTests --logger "console;verbosity=detailed"
public class SaveTimingTests(ITestOutputHelper output)
{
    [Fact]
    public void Time_changes_on_a_copy_of_a_database()
    {
        if (Environment.GetEnvironmentVariable("EDUHELPDESK_TIMING_DB") is not { Length: > 0 } path) return;
        var opened = Stopwatch.StartNew();
        using var test = new TestStore(copyOf: path);
        var store = test.Store;
        output.WriteLine($"{store.Tickets.Count} tickets, {store.Assets.Count} assets, opened in {opened.Elapsed.TotalSeconds:0.0} s; the startup save rewrote everything in {store.RecentSaveTimings()[^1].Milliseconds:0} ms");

        var number = store.Tickets[store.Tickets.Count / 2].Number;
        var person = store.Users.First(x => x.IsActive).Id;
        void Time(string what, int times, Action change)
        {
            var before = store.RecentSaveTimings().Count;
            var ms = new List<double>();
            for (var i = 0; i < times; i++) { var clock = Stopwatch.StartNew(); change(); ms.Add(clock.Elapsed.TotalMilliseconds); }
            ms.Sort();
            var saves = store.RecentSaveTimings().Skip(before).ToList();
            output.WriteLine($"  {what,-26} median {ms[ms.Count / 2],6:0} ms, slowest {ms[^1],6:0} ms, {saves.Count(x => x.FullRewrite)} full rewrites, {saves.Average(x => x.RowsWritten):0.#} rows written each");
        }
        Time("add a comment", 20, () => store.AddTicketComment(number, "Timing comment."));
        Time("change a ticket's title", 10, () => store.UpdateTicket(store.Tickets.Single(x => x.Number == number) with { Title = $"Timed {Guid.NewGuid():N}" }));
        var added = new List<int>();
        Time("log a new ticket", 10, () => added.Add(test.AddTicket(person, "Timing ticket")));
        Time("delete a ticket", 10, () => { store.DeleteTicket(added[^1]); added.RemoveAt(added.Count - 1); });
        Assert.Null(store.CompareDatabaseWithMemory());
    }
}
