using System.Globalization;
using EduHelpdesk.Models;
using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Services;

// Disposing of assets through a ticket. A ticket ticked "Dispose of the linked assets when closed" (DisposeOnClose)
// holds its linked assets while it is open: they show as "Awaiting disposal", can't be lent, put in a kit or given out,
// and the ticket carries the disposal details as they arrive (who collects, the certificate). Closing it from the
// ticket page asks whether to dispose of them; closing it any other way is refused while assets are waiting, so the
// question is never skipped. Taking an asset off the ticket, unticking it, deleting it or closing without disposing
// puts each asset back to the status it had.
//
// Which assets wait on which ticket is worked out from the tickets on every save (ReconcileDisposalMarksCore), the
// way desk numbers are, rather than at each of the many places a ticket's assets, tick or status can change.
public sealed partial class HelpdeskStore
{
    public const string AwaitingDisposalStatus = "Awaiting disposal";

    public static bool IsAwaitingDisposal(AssetRecord asset) => asset.AwaitingDisposalTicket is not null;

    // Why an asset waiting on a disposal ticket can't be lent, put in a kit or handed out, or null when it isn't waiting.
    private string? AwaitingDisposalBlock(AssetRecord asset, string action) =>
        asset.AwaitingDisposalTicket is { } number
            ? $"{asset.AssetTag} is waiting to be disposed of on {RefCore(number)}, so it can't be {action}. Take it off that ticket first if it's staying."
            : null;

    // ---- Keeping the marks in step with the tickets ----

    // Each asset not yet disposed of waits on the open ticket that disposes of it on close - the lowest numbered, if
    // two do - and on nothing otherwise. Marking keeps the status it had, to go back to; unmarking restores it, unless
    // someone has since changed the status by hand, which is then left alone. A disposed asset waits on nothing.
    // Writes the asset directly rather than through ApplyAssetUpdate, whose rules (a student's name lasts only while
    // the status is With Student) would undo what the mark is holding on to.
    private void ReconcileDisposalMarksCore()
    {
        Dictionary<Guid, TicketRecord>? wanted = null;
        foreach (var ticket in _data.Tickets)
        {
            if (!ticket.DisposeOnClose || TicketInsights.IsClosed(ticket)) continue;
            foreach (var assetId in ticket.AssetIds)
            {
                wanted ??= new();
                if (!wanted.TryGetValue(assetId, out var holder) || ticket.Number < holder.Number) wanted[assetId] = ticket;
            }
        }
        if (wanted is null && !_data.Assets.Any(x => x.AwaitingDisposalTicket is not null)) return;

        var now = DateTime.UtcNow;
        for (var i = 0; i < _data.Assets.Count; i++)
        {
            var asset = _data.Assets[i];
            var want = wanted?.GetValueOrDefault(asset.Id);
            if (IsDisposed(asset))
            {
                if (asset.AwaitingDisposalTicket is not null) _data.Assets[i] = asset with { AwaitingDisposalTicket = null };
                continue;
            }
            if (asset.AwaitingDisposalTicket == want?.Number) continue;

            var history = asset.History.ToList();
            AssetRecord next;
            if (want is null)
            {
                var back = !string.Equals(asset.Status, AwaitingDisposalStatus, StringComparison.OrdinalIgnoreCase) ? asset.Status
                    : ResolveAssetStatus(asset.StatusBeforeDisposal) ?? ResolveAssetStatus(InStockStatus) ?? _data.AssetStatuses.FirstOrDefault(x => !IsSystemAssetStatus(x)) ?? asset.Status;
                next = asset with { Status = back, AwaitingDisposalTicket = null, StatusBeforeDisposal = "" };
                var was = _data.Tickets.FirstOrDefault(x => x.Number == asset.AwaitingDisposalTicket);
                history.Add(new("No longer awaiting disposal", (was is null ? "Its disposal ticket is gone" : was.DisposeOnClose && TicketInsights.IsClosed(was)
                    ? $"{RefCore(was)} was closed without disposing of it" : $"Taken off the disposal on {RefCore(was)}")
                    + (back == asset.Status ? "." : $", so it's back to {back}."), now) { By = CurrentActor() });
            }
            else if (asset.AwaitingDisposalTicket is null)
            {
                var status = ResolveAssetStatus(AwaitingDisposalStatus) ?? asset.Status;
                next = asset with { Status = status, AwaitingDisposalTicket = want.Number, StatusBeforeDisposal = asset.Status };
                history.Add(new("Awaiting disposal", $"To be disposed of when {RefCore(want)} - {want.Title} is closed. It was {asset.Status}.", now) { By = CurrentActor() });
            }
            else
            {
                next = asset with { AwaitingDisposalTicket = want.Number };
                history.Add(new("Awaiting disposal", $"Now waiting on {RefCore(want)} - {want.Title}.", now) { By = CurrentActor() });
            }
            _data.Assets[i] = next with { History = history };
        }
    }

    // The statuses the helpdesk sets by itself, never a sensible one to fall back to.
    private static bool IsSystemAssetStatus(string status) =>
        new[] { OnLoanStatus, DisposedStatus, WithStudentStatus, AwaitingDisposalStatus }.Contains(status, StringComparer.OrdinalIgnoreCase);

    // ---- The ticket's side ----

    // The Disposal type is the tick, always on: becoming one ticks it, and stopping being one unticks it. The tick on
    // its own works on any type.
    private static TicketRecord WithDisposalTypeRule(TicketRecord? previous, TicketRecord item)
    {
        var isDisposal = string.Equals(item.Type, TicketTypes.Disposal, StringComparison.OrdinalIgnoreCase);
        var wasDisposal = previous is not null && string.Equals(previous.Type, TicketTypes.Disposal, StringComparison.OrdinalIgnoreCase);
        if (isDisposal && !item.DisposeOnClose) return item with { DisposeOnClose = true };
        if (wasDisposal && !isDisposal && item.DisposeOnClose) return item with { DisposeOnClose = false };
        return item;
    }

    // Turning the tick on or off. Who may is the page's question (the Dispose of assets tick).
    public WorkResult SetDisposeOnClose(int number, bool on)
    {
        lock (_sync)
        {
            var index = _data.Tickets.FindIndex(x => x.Number == number);
            if (index < 0) return new WorkResult(false, TicketNotFoundMessage);
            var ticket = _data.Tickets[index];
            if (ticket.DisposeOnClose == on) return new WorkResult(true, on ? "This ticket already disposes of its assets when closed." : "Nothing changed.");
            if (!on && string.Equals(ticket.Type, TicketTypes.Disposal, StringComparison.OrdinalIgnoreCase))
                return new WorkResult(false, "A Disposal ticket always disposes of its assets when closed. Change its type to stop it.");
            if (on && TicketInsights.IsClosed(ticket)) return new WorkResult(false, "Reopen the ticket before marking its assets for disposal.");
            var history = ticket.History.ToList();
            history.Add(new(on ? "Disposal on close" : "No disposal on close",
                on ? "Closing this ticket will dispose of its linked assets." : "Closing this ticket will no longer dispose of its assets.", DateTime.UtcNow) { By = CurrentActor() });
            _data.Tickets[index] = ticket with { DisposeOnClose = on, History = history };
            Save();
            var waiting = _data.Assets.Count(x => x.AwaitingDisposalTicket == number);
            return new WorkResult(true, on
                ? waiting == 0 ? "Its linked assets will be disposed of when it's closed. Link the assets to dispose of on the Assets tab." : $"{Count(waiting)} now awaiting disposal on this ticket."
                : "The assets are back to their own statuses.");
        }
    }

    // What is known so far about the disposal: how, who by, the certificate, any money. All optional until it is closed.
    public WorkResult SaveDisposalDetails(int number, string? method, string? by, string? certificate, decimal? proceeds)
    {
        lock (_sync)
        {
            var index = _data.Tickets.FindIndex(x => x.Number == number);
            if (index < 0) return new WorkResult(false, TicketNotFoundMessage);
            var clean = CleanDisposalDetails(method, by, certificate, proceeds);
            if (clean.Error is { } error) return new WorkResult(false, error);
            var (chosen, who, cert) = clean.Values;
            var ticket = _data.Tickets[index];
            if (ticket.DisposalMethod == chosen && ticket.DisposalBy == who && ticket.DisposalCertificate == cert && ticket.DisposalProceeds == proceeds)
                return new WorkResult(true, "Nothing changed.");
            var history = ticket.History.ToList();
            history.Add(new("Disposal details changed", DescribeDetails(chosen, who, cert, proceeds), DateTime.UtcNow) { By = CurrentActor() });
            _data.Tickets[index] = ticket with { DisposalMethod = chosen, DisposalBy = who, DisposalCertificate = cert, DisposalProceeds = proceeds, History = history };
            Save();
            return new WorkResult(true, "Disposal details saved.");
        }
    }

    private const int MaxDisposalTextLength = 200;

    private static (string? Error, (string Method, string By, string Certificate) Values) CleanDisposalDetails(string? method, string? by, string? certificate, decimal? proceeds)
    {
        var chosen = string.IsNullOrWhiteSpace(method) ? "" : DisposalMethods.FirstOrDefault(x => string.Equals(x, method.Trim(), StringComparison.OrdinalIgnoreCase));
        if (chosen is null) return ("Choose how they're being disposed of from the list.", default);
        var who = (by ?? "").Trim();
        var cert = (certificate ?? "").Trim();
        if (who.Length > MaxDisposalTextLength || cert.Length > MaxDisposalTextLength) return ($"Keep each box under {MaxDisposalTextLength} characters.", default);
        if (proceeds is < 0) return ("Proceeds can't be less than nothing.", default);
        return (null, (chosen, who, cert));
    }

    private static string DescribeDetails(string method, string by, string certificate, decimal? proceeds)
    {
        var parts = new List<string>();
        if (method.Length > 0) parts.Add(method);
        if (by.Length > 0) parts.Add($"by {by}");
        if (certificate.Length > 0) parts.Add($"certificate {certificate}");
        if (proceeds is { } money) parts.Add($"proceeds {money.ToString("0.00", CultureInfo.InvariantCulture)}");
        return parts.Count == 0 ? "Cleared." : string.Join(", ", parts) + ".";
    }

    private static string Count(int n) => $"{n} asset{(n == 1 ? "" : "s")}";

    // Every other way of closing a ticket asks this first: a ticket with assets waiting on it closes only from its own
    // page, where the person chooses whether to dispose of them. Null when it may close.
    public string? CloseBlockedByDisposal(int number)
    {
        lock (_sync) return CloseBlockedByDisposalCore(number);
    }
    private string? CloseBlockedByDisposalCore(int number) =>
        _data.Tickets.FirstOrDefault(x => x.Number == number) is { DisposeOnClose: true } ticket && !TicketInsights.IsClosed(ticket)
            && _data.Assets.Any(x => x.AwaitingDisposalTicket == number)
            ? $"{RefCore(ticket)} disposes of its assets when it's closed. Close it with Mark as closed on its own page, where you choose whether to dispose of them."
            : null;

    // The close pop-up. dispose: write off every asset waiting on the ticket (skipping, and naming, any still out on loan
    // or in a kit); otherwise they go back to their own statuses. Either way the ticket closes, with the closing message
    // as a reply like the Close button, and an internal note of what became of each asset. The date defaults to today.
    public WorkResult CloseDisposalTicket(int number, string? closingMessage, bool dispose, DateOnly? date, string? method, string? by, string? certificate, decimal? proceeds)
    {
        lock (_sync)
        {
            if (_data.Tickets.FirstOrDefault(x => x.Number == number) is not { } ticket) return new WorkResult(false, TicketNotFoundMessage);
            if (TicketInsights.IsClosed(ticket)) return new WorkResult(false, "Ticket is already closed.");
            if (CloseBlockedByChecklist(number) is { } blocked) return new WorkResult(false, blocked);
            if (RequiresCloseMessage(ticket) && string.IsNullOrWhiteSpace(closingMessage))
                return new WorkResult(false, $"A closing message is required for {ticket.Priority} priority / {ticket.Category} tickets before they can be closed.");
            var waiting = _data.Assets.Where(x => x.AwaitingDisposalTicket == number).ToList();
            var disposedOn = date ?? AssetInsights.Today;
            var note = "";
            var message = "Ticket closed.";
            if (dispose)
            {
                if (waiting.Count == 0) return new WorkResult(false, "No assets are waiting to be disposed of on this ticket. Use Close without disposing.");
                if (disposedOn > AssetInsights.Today) return new WorkResult(false, "The disposal date can't be in the future.");
                var clean = CleanDisposalDetails(method, by, certificate, proceeds);
                if (clean.Error is { } error) return new WorkResult(false, error);
                var (chosen, who, cert) = clean.Values;
                if (chosen.Length == 0) return new WorkResult(false, "Choose how they were disposed of.");

                var done = new List<string>();
                var skipped = new List<string>();
                foreach (var asset in waiting)
                {
                    var result = DisposeAssetCore(asset.Id, disposedOn, chosen, proceeds, who, cert, number);
                    if (result.Ok) done.Add(asset.AssetTag); else skipped.Add(result.Message);
                }
                var index = _data.Tickets.FindIndex(x => x.Number == number);
                _data.Tickets[index] = _data.Tickets[index] with { DisposalMethod = chosen, DisposalBy = who, DisposalCertificate = cert, DisposalProceeds = proceeds };
                note = (done.Count > 0 ? $"Disposed of {string.Join(", ", done)} on {AssetInsights.Format(disposedOn)}: {DescribeDetails(chosen, who, cert, proceeds)}" : "Nothing was disposed of.")
                    + (skipped.Count > 0 ? $"\nNot disposed of, and back to their own statuses: {string.Join(" ", skipped)}" : "");
                message = $"Ticket closed and {Count(done.Count)} disposed of."
                    + (skipped.Count > 0 ? $" {Count(skipped.Count)} couldn't be: {string.Join(" ", skipped)}" : "");
            }
            else if (waiting.Count > 0)
            {
                note = $"Closed without disposing of {string.Join(", ", waiting.Select(x => x.AssetTag))}; they're back to their own statuses.";
                message = $"Ticket closed. {Count(waiting.Count)} went back to their own statuses, not disposed of.";
            }

            // One save at the end: the reply, the note, the disposals and the close together. UpdateTicket saves, and the
            // save puts anything not disposed of back to its own status.
            var current = _data.Tickets.First(x => x.Number == number);
            var comments = current.Comments.ToList();
            if (!string.IsNullOrWhiteSpace(closingMessage)) comments.Add(new TicketComment(closingMessage.Trim(), DateTime.UtcNow) { By = CurrentActor() });
            if (note.Length > 0) comments.Add(new TicketComment(note, DateTime.UtcNow, true) { By = CurrentActor() });
            UpdateTicket(current with { Comments = comments, Status = TicketInsights.ClosedStatus, ClosedAt = current.ClosedAt ?? DateTime.UtcNow });
            return new WorkResult(true, message);
        }
    }

    // The assets this ticket disposed of that still are, for the reopen question and the Assets tab.
    public IReadOnlyList<AssetRecord> DisposedThrough(int number)
    {
        lock (_sync) return _data.Assets.Where(x => x.DisposalTicket == number && IsDisposed(x)).ToList();
    }

    // Undoing what the ticket disposed of, once it has been reopened: each asset's disposal record is cleared, and it waits
    // on the ticket again (or, if the ticket no longer disposes of it, goes back to the status it had before).
    public WorkResult RestoreDisposedThrough(int number)
    {
        lock (_sync)
        {
            if (_data.Tickets.FirstOrDefault(x => x.Number == number) is not { } ticket) return new WorkResult(false, TicketNotFoundMessage);
            if (TicketInsights.IsClosed(ticket)) return new WorkResult(false, "Reopen the ticket before undoing its disposal.");
            var now = DateTime.UtcNow;
            var restored = new List<string>();
            for (var i = 0; i < _data.Assets.Count; i++)
            {
                var asset = _data.Assets[i];
                if (asset.DisposalTicket != number || !IsDisposed(asset)) continue;
                var waitsAgain = ticket.DisposeOnClose && ticket.AssetIds.Contains(asset.Id);
                var status = waitsAgain ? ResolveAssetStatus(AwaitingDisposalStatus) ?? asset.StatusBeforeDisposal
                    : ResolveAssetStatus(asset.StatusBeforeDisposal) ?? ResolveAssetStatus(InStockStatus) ?? _data.AssetStatuses.FirstOrDefault(x => !IsSystemAssetStatus(x)) ?? "In use";
                var history = asset.History.ToList();
                history.Add(new("Disposal undone", $"The disposal recorded through {RefCore(ticket)} on {(asset.DisposalDate is { } on ? AssetInsights.Format(on) : "an unknown date")} ({asset.DisposalMethod}) was undone. "
                    + (waitsAgain ? "It's waiting on that ticket again." : $"It's back to {status}."), now) { By = CurrentActor() });
                _data.Assets[i] = asset with
                {
                    Status = status,
                    DisposalDate = null, DisposalMethod = "", DisposalProceeds = null, DisposedBy = "", DisposalCertificate = "",
                    DisposalTicket = null,
                    AwaitingDisposalTicket = waitsAgain ? number : null,
                    StatusBeforeDisposal = waitsAgain ? asset.StatusBeforeDisposal : "",
                    History = history
                };
                restored.Add(asset.AssetTag);
            }
            if (restored.Count == 0) return new WorkResult(false, "Nothing disposed of through this ticket is left to undo.");
            var index = _data.Tickets.FindIndex(x => x.Number == number);
            var ticketHistory = _data.Tickets[index].History.ToList();
            ticketHistory.Add(new("Disposal undone", $"{string.Join(", ", restored)} no longer recorded as disposed of.", now) { By = CurrentActor() });
            _data.Tickets[index] = _data.Tickets[index] with { History = ticketHistory };
            Save();
            return new WorkResult(true, $"{Count(restored.Count)} no longer recorded as disposed of.");
        }
    }

    // One line per linked asset for the ticket's Assets tab and the close pop-up: where it stands, and anything that will
    // stop it being disposed of.
    public sealed record DisposalLine(AssetRecord Asset, DisposalState State, string? OtherTicket, string? Problem);
    public enum DisposalState { NotMarked, Waiting, WaitingElsewhere, DisposedHere, DisposedElsewhere }

    public IReadOnlyList<DisposalLine> DisposalLines(int number)
    {
        lock (_sync)
        {
            if (_data.Tickets.FirstOrDefault(x => x.Number == number) is not { } ticket) return [];
            var lines = new List<DisposalLine>();
            foreach (var id in ticket.AssetIds.Distinct())
            {
                if (_data.Assets.FirstOrDefault(x => x.Id == id) is not { } asset) continue;
                if (IsDisposed(asset))
                    lines.Add(new(asset, asset.DisposalTicket == number ? DisposalState.DisposedHere : DisposalState.DisposedElsewhere, asset.DisposalTicket is { } other && other != number ? RefCore(other) : null, null));
                else if (asset.AwaitingDisposalTicket == number)
                    lines.Add(new(asset, DisposalState.Waiting, null, DisposalBlockCore(asset)));
                else if (asset.AwaitingDisposalTicket is { } elsewhere)
                    lines.Add(new(asset, DisposalState.WaitingElsewhere, RefCore(elsewhere), null));
                else
                    lines.Add(new(asset, DisposalState.NotMarked, null, null));
            }
            return lines;
        }
    }

    // ---- The "Dispose of assets" tick ----

    // Before the tick, disposing was Assets: Delete. An upgrade gives the tick to every role that held that, so nobody
    // gains or loses anything; after that it is set by hand like any other tick.
    private void EnsureDisposeFlag()
    {
        lock (_sync)
        {
            if (_data.DisposeFlagVersion >= 1) return;
            foreach (var role in _data.Roles)
                if (role.IsProtected || role.GrantsFor(Modules.Assets).HasFlag(ModulePermission.Delete)) role.Flags.Add(Modules.Flags.DisposeAssets);
            _data.DisposeFlagVersion = 1;
            // Audited, so the roles that got it can be checked.
            Save();
        }
    }

    private static void ReadDisposal(SqliteConnection connection, StoreData data) =>
        data.DisposeFlagVersion = int.TryParse(ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'DisposeFlagVersion';") as string, out var version) ? version : 0;

    private static void WriteDisposal(SqliteConnection connection, SqliteTransaction transaction, StoreData data) =>
        SetMetadata(connection, transaction, "DisposeFlagVersion", data.DisposeFlagVersion.ToString(CultureInfo.InvariantCulture));
}
