using EduHelpdesk.Models;
using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Services;

// Settings → Go live & reset: emptying one part of the helpdesk without the factory reset's "everything". Each keeps the
// set-up around it (templates, lists, desks, kits, rounds) and empties only the records, so a school can clear out its
// trial tickets or a botched asset import and carry on with everything else as it was. As careful as the factory reset:
// a backup first, the module's name typed to confirm, Settings: Edit (the page), and one audit entry for the lot.
public sealed partial class HelpdeskStore
{
    public static class ResetModules
    {
        public const string Tickets = "tickets";
        public const string Projects = "projects";
        public const string Assets = "assets";
        public const string Onboarding = "onboarding";
        public const string Parts = "parts";
        public const string Contracts = "contracts";
        public const string Access = "access";
        public const string Facilities = "facilities";
    }

    // Name is what is typed to confirm, so it is one plain word. Removes and Keeps are the page's words.
    public sealed record ResetModuleInfo(string Key, string Name, string Removes, string Keeps);

    public static readonly IReadOnlyList<ResetModuleInfo> ResettableModules =
    [
        new(ResetModules.Tickets, "Tickets",
            "every ticket on every desk, with its comments, history, attachments, time, costs, parts used, links and premises check readings; onboarding records (they belong to their tickets); ticket alerts under the bell; and the Spiceworks import history. Numbering starts again at 1 on each desk.",
            "templates, the service catalogue, categories, statuses, priorities, SLAs, recurring schedules, desks and parts stock (parts already used are not put back)."),
        new(ResetModules.Projects, "Projects",
            "every project with its items, supplier quotes and their files, payment lines, notes and history. Numbering starts again at PRJ-0001.",
            "purchasing requirements, spending bands, and contracts made from approved projects (they lose the link to the project)."),
        new(ResetModules.Assets, "Assets",
            "every asset with its comments, history, custom attribute values and loan history, and every kit loan.",
            "kits (emptied, ready to fill again), asset types, models, makes, statuses, custom attributes and loan reasons. Tickets, contracts, schedules and onboarding tasks keep their text but lose the link to the asset."),
        new(ResetModules.Onboarding, "Onboarding",
            "every onboarding with its new-starter ticket and checklist tasks.",
            "onboarding templates, the welcome pack's documents and IT information, and the starters in People."),
        new(ResetModules.Parts, "Parts",
            "every part with its stock level and stock history, and the parts recorded as used on tickets.",
            "part categories, part locations and suppliers."),
        new(ResetModules.Contracts, "Contracts",
            "every contract on the contracts register.",
            "contract types, spend categories, durations and statuses, and suppliers. Assets lose their link to a contract."),
        new(ResetModules.Access, "Access",
            "every system and area on the access control register, every access grant and every termly review.",
            "access categories, revoke reasons and person types, and the people themselves."),
        new(ResetModules.Facilities, "Facilities",
            "what has been recorded on cleaning rounds, and premises check tickets with their readings.",
            "the rounds themselves (they count from today), cleaners, and the recurring premises check schedules."),
    ];

    public static ResetModuleInfo? FindResetModule(string? key) => ResettableModules.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));

    // How many records a reset of each module would remove, for the page to show beside each one.
    public IReadOnlyDictionary<string, int> ResetCounts()
    {
        lock (_sync)
        {
            var checkTickets = _data.CheckLines.Select(x => x.TicketNumber).ToHashSet();
            return new Dictionary<string, int>
            {
                [ResetModules.Tickets] = _data.Tickets.Count,
                [ResetModules.Projects] = _data.Projects.Count,
                [ResetModules.Assets] = _data.Assets.Count,
                [ResetModules.Onboarding] = _data.Onboardings.Count,
                [ResetModules.Parts] = _data.Parts.Count,
                [ResetModules.Contracts] = _data.Contracts.Count,
                [ResetModules.Access] = _data.AccessResources.Count + _data.AccessGrants.Count,
                [ResetModules.Facilities] = _data.RoundVisits.Count + _data.Tickets.Count(x => checkTickets.Contains(x.Number)),
            };
        }
    }

    public (bool Ok, string Message) ResetModule(string? key, string? confirmation, bool keepBackup = true)
    {
        if (FindResetModule(key) is not { } module) return (false, "Nothing was changed. Choose what to reset.");
        if (!string.Equals(confirmation?.Trim(), module.Name, StringComparison.OrdinalIgnoreCase))
            return (false, $"Nothing was changed. Type {module.Name} to reset {module.Name.ToLowerInvariant()}.");
        lock (_sync)
        {
            string? backupName = null;
            if (keepBackup)
            {
                try { backupName = WriteBackup($"before-reset-{module.Key}").Name; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException or InvalidOperationException)
                {
                    return (false, $"Nothing was changed. The backup could not be saved ({ex.Message}). Fix that, or untick the backup option to reset without one.");
                }
            }

            var files = new List<Guid>();
            var removed = module.Key switch
            {
                ResetModules.Tickets => ResetTicketsCore(files),
                ResetModules.Projects => ResetProjectsCore(files),
                ResetModules.Assets => ResetAssetsCore(),
                ResetModules.Onboarding => ResetOnboardingCore(files),
                ResetModules.Parts => ResetPartsCore(),
                ResetModules.Contracts => ResetContractsCore(),
                ResetModules.Access => ResetAccessCore(),
                _ => ResetFacilitiesCore(files),
            };

            // One entry for the lot, not one per record: SaveBaseline writes the rows without diffing them into the log.
            _pendingAudit.Add(new AuditEntry(DateTime.UtcNow, "System", null, null, "Helpdesk", $"{module.Name} reset",
                $"{removed} {(removed == 1 ? "record was" : "records were")} removed by a reset of {module.Name.ToLowerInvariant()}."
                + (backupName is null ? " No backup was kept." : $" A backup was saved as {backupName}.")) { By = CurrentActor() });
            SaveBaseline();
            foreach (var id in files) TryDelete(AttachmentFile(id));
            // Deleted rows otherwise linger in the file's free pages, as for the factory reset.
            using (var connection = new SqliteConnection($"Data Source={_path}"))
            {
                connection.Open();
                using var vacuum = connection.CreateCommand();
                vacuum.CommandText = "VACUUM;";
                vacuum.ExecuteNonQuery();
            }
            if (module.Key is ResetModules.Tickets) RemoveTicketNotifications();
            return (true, $"{module.Name} reset: {removed} {(removed == 1 ? "record was" : "records were")} removed."
                + (backupName is null ? "" : $" A backup of the old data was saved as {backupName} in {BackupFolder}."));
        }
    }

    // ---- Each module. Called with the lock held; the caller saves. Each returns how many records went. ----

    // A ticket and everything hanging off it, as DeleteTicket does - except parts used are not put back in stock: a
    // reset clears records of work that was really done, unlike deleting a ticket raised by mistake.
    private void RemoveTicketRecordsCore(IReadOnlySet<int> numbers, List<Guid> files)
    {
        files.AddRange(_data.TicketAttachments.Where(x => numbers.Contains(x.TicketNumber)).Select(x => x.Id));
        _data.TicketAttachments.RemoveAll(x => numbers.Contains(x.TicketNumber));
        _data.TicketParts.RemoveAll(x => numbers.Contains(x.TicketNumber));
        _data.TicketAttributeValues.RemoveAll(x => numbers.Contains(x.TicketNumber));
        _data.TicketCosts.RemoveAll(x => numbers.Contains(x.TicketNumber));
        _data.CheckLines.RemoveAll(x => numbers.Contains(x.TicketNumber));
        _data.TicketLinks.RemoveAll(x => numbers.Contains(x.TicketNumber) || numbers.Contains(x.LinkedNumber));
        _data.Onboardings.RemoveAll(x => numbers.Contains(x.TicketNumber));
        for (var i = 0; i < _data.Projects.Count; i++)
            if (_data.Projects[i].TicketNumbers.Any(numbers.Contains))
                _data.Projects[i] = _data.Projects[i] with { TicketNumbers = _data.Projects[i].TicketNumbers.Where(x => !numbers.Contains(x)).ToList() };
        for (var i = 0; i < _data.RecurringTickets.Count; i++)
            if (_data.RecurringTickets[i].LastTicketNumber is { } last && numbers.Contains(last))
                _data.RecurringTickets[i] = _data.RecurringTickets[i] with { LastTicketNumber = null };
        _data.TechnicianPingSeen.RemoveWhere(numbers.Contains);
        // StaffNotices stay: a notice whose ticket is gone is what StaffNoticesToRecall looks for, so EduNotify takes the
        // notice off the staff PCs within a few seconds and then forgets it.
        _data.Tickets.RemoveAll(x => numbers.Contains(x.Number));
        RemoveDemoRecords("Ticket", numbers.Select(x => x.ToString()));
    }

    private int ResetTicketsCore(List<Guid> files)
    {
        var count = _data.Tickets.Count;
        RemoveTicketRecordsCore(_data.Tickets.Select(x => x.Number).ToHashSet(), files);
        // Back to 1 on every desk, switched-off ones too. The key behind each ticket (LastTicketNumber) carries on
        // counting, so a bookmark, an alert or a phone holding an old ticket's key never opens a new ticket instead.
        for (var i = 0; i < _data.Desks.Count; i++) _data.Desks[i] = _data.Desks[i] with { NextNumber = 1 };
        for (var i = 0; i < _data.OffDesks.Count; i++) _data.OffDesks[i] = _data.OffDesks[i] with { Desk = _data.OffDesks[i].Desk with { NextNumber = 1 } };
        _data.SpiceworksImports.Clear();
        _data.SpiceworksLinks.Clear();
        _data.SpiceworksTicketStates.Clear();
        _data.SpiceworksUndo.Clear();
        return count;
    }

    private int ResetProjectsCore(List<Guid> files)
    {
        var count = _data.Projects.Count;
        files.AddRange(QuoteFiles(_data.Projects.SelectMany(x => x.Items).SelectMany(x => x.Suppliers)));
        _data.Projects.Clear();
        // Back to PRJ-0001, as asked for - the one place a project number is ever handed out twice.
        _data.LastProjectNumber = 0;
        for (var i = 0; i < _data.Contracts.Count; i++)
            if (_data.Contracts[i].ProjectNumber is not null) _data.Contracts[i] = _data.Contracts[i] with { ProjectNumber = null };
        RemoveDemoRecords("Project", null);
        return count;
    }

    private int ResetAssetsCore()
    {
        var count = _data.Assets.Count;
        _data.Assets.Clear();
        _data.AssetAttributeValues.Clear();
        _data.KitLoans.Clear();
        for (var i = 0; i < _data.LoanKits.Count; i++) _data.LoanKits[i] = _data.LoanKits[i] with { AssetIds = [] };
        for (var i = 0; i < _data.Tickets.Count; i++)
            if (_data.Tickets[i].AssetIds.Count > 0) _data.Tickets[i] = _data.Tickets[i] with { AssetIds = [] };
        for (var i = 0; i < _data.RecurringTickets.Count; i++)
            if (_data.RecurringTickets[i].AssetIds.Count > 0) _data.RecurringTickets[i] = _data.RecurringTickets[i] with { AssetIds = [] };
        for (var i = 0; i < _data.CheckLines.Count; i++)
            if (_data.CheckLines[i].AssetId is not null) _data.CheckLines[i] = _data.CheckLines[i] with { AssetId = null };
        for (var i = 0; i < _data.Onboardings.Count; i++)
            if (_data.Onboardings[i].Tasks.Any(x => x.AssetId is not null))
                _data.Onboardings[i] = _data.Onboardings[i] with { Tasks = _data.Onboardings[i].Tasks.Select(x => x with { AssetId = null }).ToList() };
        RemoveDemoRecords("Asset", null);
        return count;
    }

    private int ResetOnboardingCore(List<Guid> files)
    {
        var numbers = _data.Onboardings.Select(x => x.TicketNumber).ToHashSet();
        var count = _data.Onboardings.Count;
        RemoveTicketRecordsCore(numbers, files);
        return count;
    }

    private int ResetPartsCore()
    {
        var count = _data.Parts.Count;
        _data.Parts.Clear();
        _data.TicketParts.Clear();
        RemoveDemoRecords("Part", null);
        return count;
    }

    private int ResetContractsCore()
    {
        var count = _data.Contracts.Count;
        _data.Contracts.Clear();
        for (var i = 0; i < _data.Assets.Count; i++)
            if (_data.Assets[i].ContractId is not null) _data.Assets[i] = _data.Assets[i] with { ContractId = null };
        return count;
    }

    private int ResetAccessCore()
    {
        var count = _data.AccessResources.Count + _data.AccessGrants.Count;
        _data.AccessResources.Clear();
        _data.AccessGrants.Clear();
        _data.AccessReviews.Clear();
        return count;
    }

    private int ResetFacilitiesCore(List<Guid> files)
    {
        var checks = _data.CheckLines.Select(x => x.TicketNumber).ToHashSet();
        var count = _data.RoundVisits.Count + _data.Tickets.Count(x => checks.Contains(x.Number));
        _data.RoundVisits.Clear();
        // A round counts missed days from the day it started; with its history gone, every earlier day would be "missed".
        var today = DateOnly.FromDateTime(DateTime.Now);
        for (var i = 0; i < _data.CleaningRounds.Count; i++) _data.CleaningRounds[i] = _data.CleaningRounds[i] with { StartedOn = today };
        RemoveTicketRecordsCore(checks, files);
        return count;
    }

    // What the Go live list would otherwise still name. Keys null: every record of that type.
    private void RemoveDemoRecords(string type, IEnumerable<string>? keys)
    {
        var set = keys?.ToHashSet(StringComparer.Ordinal);
        _data.DemoRecords.RemoveAll(x => x.EntityType == type && (set is null || set.Contains(x.EntityKey)));
    }
}
