using System.Globalization;
using System.Text.RegularExpressions;
using EduHelpdesk.Models;
using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Services;

// People and data lifecycle: what a leaver still holds, a copy of everything held about one person (subject access),
// and retention - deleting old closed tickets, anonymising people who left long ago, and trimming the audit log.
public partial class HelpdeskStore
{
    // What an anonymised requester is called from then on, everywhere their name was.
    public const string AnonymisedName = "Former staff member";
    public const int MaxRetentionMonths = 120;
    // The audit log is how the school shows who did what, so it can't be trimmed to less than a year.
    public const int MinAuditRetentionMonths = 12;

    // ---- Leavers ----

    // Whoever is inactive has the date they left; whoever is active has none. Keeps the existing date when an
    // inactive person is edited, and the anonymised date always carries over (an edit form doesn't send it).
    private static UserRecord WithLeaverDates(UserRecord item, UserRecord? previous) => item with
    {
        LeftAt = item.IsActive ? null : previous is { IsActive: false, LeftAt: { } left } ? left : item.LeftAt ?? DateTime.UtcNow,
        AnonymisedAt = previous?.AnonymisedAt ?? item.AnonymisedAt
    };

    // At load: people who were already inactive before leaver dates existed count as leaving now, so retention
    // counts from the upgrade rather than treating them as long gone.
    private void ReconcileLeaverDates()
    {
        for (var i = 0; i < _data.Users.Count; i++)
        {
            var user = _data.Users[i];
            if (!user.IsActive && user.LeftAt is null) _data.Users[i] = user with { LeftAt = DateTime.UtcNow };
            else if (user.IsActive && user.LeftAt is not null) _data.Users[i] = user with { LeftAt = null };
        }
    }

    // Everything a person still has, for the leaver check on their page.
    public sealed record LeaverHoldings(UserRecord Person, IReadOnlyList<AssetRecord> Assets, IReadOnlyList<(LoanKit Kit, KitLoan Loan)> Kits,
        IReadOnlyList<TicketRecord> OpenTickets, IReadOnlyList<ProjectRecord> ActiveProjects, bool OnlyProjectLead)
    {
        public int EquipmentCount => Assets.Count + Kits.Count;
        public bool HoldsAnything => EquipmentCount + OpenTickets.Count + ActiveProjects.Count > 0 || OnlyProjectLead;

        // "2 assets and 1 kit" - blank when there is no equipment.
        public string EquipmentSummary()
        {
            var parts = new List<string>();
            if (Assets.Count > 0) parts.Add($"{Assets.Count} asset{(Assets.Count == 1 ? "" : "s")}");
            if (Kits.Count > 0) parts.Add($"{Kits.Count} loan kit{(Kits.Count == 1 ? "" : "s")}");
            return string.Join(" and ", parts);
        }
    }

    public LeaverHoldings? GetHoldings(Guid userId)
    {
        lock (_sync) return _data.Users.FirstOrDefault(x => x.Id == userId) is { } user ? HoldingsCore(user) : null;
    }

    private LeaverHoldings HoldingsCore(UserRecord user)
    {
        // Assets inside a kit that is out are listed under the kit, not one by one.
        var assets = _data.Assets.Where(x => x.AssignedUserId == user.Id && !IsDisposed(x) && KitLoanHoldingCore(x.Id) is null)
            .OrderBy(x => x.AssetTag, NaturalComparer.Instance).ToList();
        var kits = _data.KitLoans.Where(x => x.IsOut && x.BorrowerUserId == user.Id)
            .Select(x => (Kit: _data.LoanKits.FirstOrDefault(k => k.Id == x.KitId), Loan: x))
            .Where(x => x.Kit is not null).Select(x => (x.Kit!, x.Loan)).ToList();
        var tickets = _data.Tickets.Where(x => x.RequesterId == user.Id && !TicketInsights.IsClosed(x)).OrderBy(x => x.Number).ToList();
        var projects = _data.Projects.Where(x => x.RequesterId == user.Id && x.IsActive).OrderBy(x => x.Number).ToList();
        var onlyLead = user.IsProjectLead && !_data.Users.Any(x => x.Id != user.Id && x.IsActive && x.IsProjectLead);
        return new LeaverHoldings(user, assets, kits, tickets, projects, onlyLead);
    }

    // People who have left but still have equipment, with how many items - the People list flags them.
    public IReadOnlyDictionary<Guid, int> LeaversStillHolding()
    {
        lock (_sync)
            return _data.Users.Where(x => !x.IsActive).Select(HoldingsCore).Where(x => x.EquipmentCount > 0)
                .ToDictionary(x => x.Person.Id, x => x.EquipmentCount);
    }

    // Ids of everyone inactive, for the asset review list's "held by someone who has left".
    public IReadOnlySet<Guid> DepartedUserIds
    {
        get { lock (_sync) return _data.Users.Where(x => !x.IsActive).Select(x => x.Id).ToHashSet(); }
    }

    // Books back in every kit they have out and every asset they hold, in one save. Something that was simply in use
    // or on loan goes back to stock; anything with another status (in repair, lost) keeps it, because that was a
    // decision somebody made.
    public (bool Ok, string Message) BookEverythingBackIn(Guid userId)
    {
        lock (_sync)
        {
            var user = _data.Users.FirstOrDefault(x => x.Id == userId);
            if (user is null) return (false, "That person was not found.");
            var held = HoldingsCore(user);
            if (held.EquipmentCount == 0) return (false, $"{user.Name} has no assets or kits to book back in.");

            foreach (var (kit, _) in held.Kits) ReturnKitCore(kit.Id, "Booked back in with the leaver check.");
            var inStock = ResolveAssetStatus(InStockStatus);
            foreach (var asset in held.Assets)
            {
                var index = _data.Assets.FindIndex(x => x.Id == asset.Id);
                if (index < 0) continue;
                var current = _data.Assets[index];
                var backToStock = inStock is not null && (string.Equals(current.Status, OnLoanStatus, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(current.Status, "In use", StringComparison.OrdinalIgnoreCase));
                ApplyAssetUpdate(index, current with { AssignedUserId = null, LoanDueDate = null, Status = backToStock ? inStock! : current.Status });
            }
            Save();
            return (true, $"Booked back in from {user.Name}: {held.EquipmentSummary()}.");
        }
    }

    // ---- Subject access ----

    public sealed record SubjectAccessAttachment(TicketAttachment Attachment, string FilePath);
    public sealed record SubjectAccessAsset(AssetRecord Asset, IReadOnlyList<AssetAssignment> Periods, bool HeldNow);
    public sealed record SubjectAccessMention(TicketRecord Ticket, string Where);
    public sealed record SubjectAccessData(
        UserRecord Person, DateTime GeneratedAt, Actor GeneratedBy,
        IReadOnlyList<TicketRecord> Tickets,
        IReadOnlyList<SubjectAccessAttachment> Attachments,
        IReadOnlyDictionary<int, IReadOnlyList<(string Field, string Value)>> TicketFields,
        IReadOnlyList<SubjectAccessAsset> Assets,
        IReadOnlyList<(KitLoan Loan, string KitName)> KitLoans,
        IReadOnlyList<ProjectRecord> Projects,
        IReadOnlyList<SubjectAccessMention> Mentions,
        IReadOnlyList<AuditEntry> AuditEntries,
        IReadOnlyDictionary<Guid, string> TechnicianNames,
        IReadOnlyDictionary<Guid, string> SlaNames);

    // Everything the helpdesk holds about one requester, gathered under the lock so it is one consistent moment. The
    // export (SubjectAccessExport) turns it into a zip; recording that it was taken is the caller's job.
    public SubjectAccessData? GatherSubjectAccess(Guid userId)
    {
        lock (_sync)
        {
            var person = _data.Users.FirstOrDefault(x => x.Id == userId);
            if (person is null) return null;
            var tickets = _data.Tickets.Where(x => x.RequesterId == userId).OrderBy(x => x.Number).ToList();
            var numbers = tickets.Select(x => x.Number).ToHashSet();
            var attachments = _data.TicketAttachments.Where(x => numbers.Contains(x.TicketNumber)).OrderBy(x => x.TicketNumber).ThenBy(x => x.UploadedAt)
                .Select(x => new SubjectAccessAttachment(x, AttachmentFile(x.Id))).ToList();
            var definitions = _data.TicketAttributeDefinitions.ToDictionary(x => x.Id, x => x.Name);
            var fields = _data.TicketAttributeValues.Where(x => numbers.Contains(x.TicketNumber) && !string.IsNullOrWhiteSpace(x.Value))
                .GroupBy(x => x.TicketNumber)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<(string, string)>)g.Select(x => (definitions.GetValueOrDefault(x.AttributeDefinitionId, "Field"), x.Value)).ToList());
            var assets = _data.Assets
                .Where(x => x.AssignedUserId == userId || x.Assignments.Any(a => a.UserId == userId))
                .OrderBy(x => x.AssetTag, NaturalComparer.Instance)
                .Select(x => new SubjectAccessAsset(x, x.Assignments.Where(a => a.UserId == userId).ToList(), x.AssignedUserId == userId)).ToList();
            var kitNames = _data.LoanKits.ToDictionary(x => x.Id, x => x.Name);
            var loans = _data.KitLoans.Where(x => x.BorrowerUserId == userId).OrderBy(x => x.IssuedAt)
                .Select(x => (x, kitNames.GetValueOrDefault(x.KitId, "Loan kit"))).ToList();
            var projects = _data.Projects.Where(x => x.RequesterId == userId).OrderBy(x => x.Number).ToList();

            // Other people's tickets that name them. Only a full name (two words or more) is looked for, and the email:
            // a first name alone would match half the school.
            var mentions = new List<SubjectAccessMention>();
            foreach (var ticket in _data.Tickets.Where(x => x.RequesterId != userId).OrderBy(x => x.Number))
            {
                var where = new List<string>();
                if (Mentions(ticket.Title, person)) where.Add("title");
                if (Mentions(ticket.Description, person)) where.Add("description");
                var comments = ticket.Comments.Count(x => Mentions(x.Text, person));
                if (comments > 0) where.Add(comments == 1 ? "a comment" : $"{comments} comments");
                if (where.Count > 0) mentions.Add(new SubjectAccessMention(ticket, string.Join(", ", where)));
            }

            var audit = _audit.Where(x => AuditConcerns(x, person)).OrderBy(x => x.At).ToList();
            return new SubjectAccessData(person, DateTime.UtcNow, CurrentActor(), tickets, attachments, fields, assets, loans, projects, mentions, audit,
                _data.Technicians.ToDictionary(x => x.Id, x => x.Name), _data.Slas.ToDictionary(x => x.Id, x => x.Name));
        }
    }

    private static bool Mentions(string? text, UserRecord person) =>
        !string.IsNullOrEmpty(text) && ((IsFullName(person.Name) && NamePattern(person.Name).IsMatch(text))
            || (person.Email.Length > 0 && text.Contains(person.Email, StringComparison.OrdinalIgnoreCase)));

    // Audit lines about this person: changes to their record, what they did in the staff portal (recorded under their
    // name with no account id), and sign-in events that name their email.
    private static bool AuditConcerns(AuditEntry entry, UserRecord person) =>
        (entry.EntityType == "User" && entry.EntityKey == person.Id.ToString())
        || (entry.By is { Id: null } by && string.Equals(by.Name, person.Name, StringComparison.Ordinal))
        || (person.Email.Length > 0 && entry.Details.Contains(person.Email, StringComparison.OrdinalIgnoreCase));

    private static bool IsFullName(string name) => name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2;
    private static Regex NamePattern(string name) => new($@"\b{Regex.Escape(name.Trim())}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Records that a copy was taken, with who took it. Written straight to the log, as there is nothing to save.
    public void RecordSubjectAccessExport(SubjectAccessData data) =>
        RecordEvent(new AuditEntry(DateTime.UtcNow, "Users", "User", data.Person.Id.ToString(), data.Person.Name, "Subject access export",
            $"A copy of everything held about them was downloaded: {data.Tickets.Count} ticket(s), {data.Attachments.Count} file(s), {data.Assets.Count} asset(s), {data.KitLoans.Count} kit loan(s), {data.Projects.Count} project(s), {data.Mentions.Count} mention(s) on other tickets, {data.AuditEntries.Count} audit line(s).")
        { By = CurrentActor() });

    // ---- Retention ----

    // 0 means the rule is off. Months, counted back from the moment the rule runs.
    public sealed record RetentionSettings(int TicketMonths, int LeaverMonths, int AuditMonths, DateTime? LastRunAt, string LastSummary)
    {
        public bool AnyOn => TicketMonths > 0 || LeaverMonths > 0 || AuditMonths > 0;
    }

    public RetentionSettings Retention
    {
        get
        {
            lock (_sync) return new(_data.RetentionTicketMonths, _data.RetentionLeaverMonths, _data.RetentionAuditMonths, _data.LastRetentionRunAt, _data.LastRetentionSummary);
        }
    }

    public string SetRetention(int ticketMonths, int leaverMonths, int auditMonths)
    {
        lock (_sync)
        {
            if (ticketMonths is < 0 or > MaxRetentionMonths || leaverMonths is < 0 or > MaxRetentionMonths || auditMonths is < 0 or > MaxRetentionMonths)
                return $"Enter a number of months from 0 (keep forever) to {MaxRetentionMonths}.";
            if (auditMonths is > 0 and < MinAuditRetentionMonths)
                return $"Keep the audit log for at least {MinAuditRetentionMonths} months, or 0 to keep it forever.";
            _data.RetentionTicketMonths = ticketMonths;
            _data.RetentionLeaverMonths = leaverMonths;
            _data.RetentionAuditMonths = auditMonths;
            Save();
            return "Retention rules saved. " + (Retention.AnyOn ? "They run each night after the backup, or now with Apply now." : "Everything is kept.");
        }
    }

    // What each rule would take if it ran now.
    public sealed record RetentionPreview(DateTime? TicketCutoff, int Tickets, DateTime? LeaverCutoff, int People, int LeaversWaiting, DateTime? AuditCutoff, int AuditEntries);

    public RetentionPreview PreviewRetention()
    {
        lock (_sync)
        {
            var now = DateTime.UtcNow;
            var ticketCutoff = _data.RetentionTicketMonths > 0 ? now.AddMonths(-_data.RetentionTicketMonths) : (DateTime?)null;
            var leaverCutoff = _data.RetentionLeaverMonths > 0 ? now.AddMonths(-_data.RetentionLeaverMonths) : (DateTime?)null;
            var auditCutoff = _data.RetentionAuditMonths > 0 ? now.AddMonths(-_data.RetentionAuditMonths) : (DateTime?)null;
            var tickets = ticketCutoff is { } tc ? ExpiredTickets(tc).Count : 0;
            var (people, waiting) = leaverCutoff is { } lc ? ExpiredLeavers(lc) : ([], 0);
            var audit = auditCutoff is { } ac ? _audit.Count(x => x.At < ac) : 0;
            return new(ticketCutoff, tickets, leaverCutoff, people.Count, waiting, auditCutoff, audit);
        }
    }

    // Once a day, after the nightly backup has had its chance - and, if backups are on, only once one has worked in
    // the last day, so nothing is deleted that no backup holds.
    public bool RetentionDue(DateTime localNow)
    {
        lock (_sync)
        {
            if (_data.RetentionTicketMonths <= 0 && _data.RetentionLeaverMonths <= 0 && _data.RetentionAuditMonths <= 0) return false;
            if (_data.LastRetentionRunAt is { } last && last.ToLocalTime().Date >= localNow.Date) return false;
            if (localNow.Hour < _data.BackupHour) return false;
            return RetentionBackupReady();
        }
    }

    // Backups on and none has worked in the last 26 hours: the scheduled run waits for one.
    private bool RetentionBackupReady() =>
        !_data.BackupsEnabled || (_data.LastBackupAt is { } ok && DateTime.UtcNow - ok < TimeSpan.FromHours(26));

    public bool RetentionWaitingForBackup { get { lock (_sync) return !RetentionBackupReady(); } }

    public (bool Ok, string Message) ApplyRetention()
    {
        lock (_sync)
        {
            if (_data.RetentionTicketMonths <= 0 && _data.RetentionLeaverMonths <= 0 && _data.RetentionAuditMonths <= 0)
                return (false, "No retention rules are switched on.");
            var now = DateTime.UtcNow;
            var done = new List<string>();
            var files = new List<string>();
            var purgedTickets = new List<int>();

            if (_data.RetentionTicketMonths > 0)
            {
                var cutoff = now.AddMonths(-_data.RetentionTicketMonths);
                var expired = ExpiredTickets(cutoff);
                foreach (var ticket in expired) files.AddRange(PurgeTicketCore(ticket.Number));
                purgedTickets.AddRange(expired.Select(x => x.Number));
                done.Add(expired.Count == 0 ? $"no tickets closed before {Day(cutoff)}"
                    : $"deleted {expired.Count} ticket{(expired.Count == 1 ? "" : "s")} closed before {Day(cutoff)} ({Numbers(expired.Select(x => x.Number))})");
            }

            var anonymised = new List<(Guid Id, string Name, string Email)>();
            if (_data.RetentionLeaverMonths > 0)
            {
                var cutoff = now.AddMonths(-_data.RetentionLeaverMonths);
                var (people, waiting) = ExpiredLeavers(cutoff);
                foreach (var person in people) anonymised.Add((person.Id, person.Name, person.Email));
                foreach (var person in people) AnonymiseCore(person, now);
                done.Add((people.Count == 0 ? $"nobody who left before {Day(cutoff)} to anonymise" : $"anonymised {people.Count} {(people.Count == 1 ? "person" : "people")} who left before {Day(cutoff)}")
                    + (waiting > 0 ? $" ({waiting} more left that long ago but still hold something, so were left alone)" : ""));
            }

            DateTime? auditCutoff = _data.RetentionAuditMonths > 0 ? now.AddMonths(-_data.RetentionAuditMonths) : null;
            var auditOld = auditCutoff is { } ac ? _audit.Count(x => x.At < ac) : 0;
            if (auditCutoff is { } cut) done.Add(auditOld == 0 ? $"no audit entries from before {Day(cut)}" : $"deleted {auditOld} audit entr{(auditOld == 1 ? "y" : "ies")} from before {Day(cut)}");

            var summary = "Retention: " + string.Join("; ", done) + ".";
            _data.LastRetentionRunAt = now;
            _data.LastRetentionSummary = summary;
            // Nobody's name goes into the log here: the summary counts, and the field-by-field diff that a normal save
            // writes (which would record the very names being removed) is skipped.
            _pendingAudit.Add(new AuditEntry(now, "System", null, null, "Data retention", "Applied", summary));
            SaveBaseline();

            // The data is saved; now the audit log, which is kept apart from it. Files go last, once nothing refers to them.
            try { TrimAuditLog(auditCutoff, purgedTickets, anonymised); }
            catch (SqliteException ex) { Console.Error.WriteLine($"EduHelpdesk: retention saved the data but couldn't update the audit log: {ex.Message}"); }
            foreach (var file in files) TryDelete(file);
            return (true, summary);
        }
    }

    private static string Day(DateTime utc) => utc.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture);

    // "#1001-#1003, #1010" rather than a list of hundreds.
    private static string Numbers(IEnumerable<int> numbers)
    {
        var sorted = numbers.OrderBy(x => x).ToList();
        var ranges = new List<string>();
        for (var i = 0; i < sorted.Count;)
        {
            var j = i;
            while (j + 1 < sorted.Count && sorted[j + 1] == sorted[j] + 1) j++;
            ranges.Add(i == j ? $"#{sorted[i]}" : $"#{sorted[i]}-#{sorted[j]}");
            i = j + 1;
        }
        return ranges.Count <= 12 ? string.Join(", ", ranges) : string.Join(", ", ranges.Take(12)) + $" and {ranges.Count - 12} more ranges";
    }

    private List<TicketRecord> ExpiredTickets(DateTime cutoff) =>
        _data.Tickets.Where(x => TicketInsights.IsClosed(x) && TicketReports.ClosedTime(x) is { } closed && closed < cutoff).ToList();

    // People who left before the cutoff and hold nothing. Someone who left that long ago but still has a laptop or
    // an open ticket is counted as waiting instead: that needs a person to sort out, not a rule.
    private (List<UserRecord> People, int Waiting) ExpiredLeavers(DateTime cutoff)
    {
        var due = _data.Users.Where(x => !x.IsActive && x.AnonymisedAt is null && x.LeftAt is { } left && left < cutoff).ToList();
        var clear = due.Where(x => HoldingsCore(x) is { EquipmentCount: 0, OpenTickets.Count: 0, ActiveProjects.Count: 0 }).ToList();
        return (clear, due.Count - clear.Count);
    }

    // Removes a ticket for retention and returns the attachment files to delete once the save has worked. Unlike
    // DeleteTicket, parts are not put back in stock - they were used, years ago.
    private List<string> PurgeTicketCore(int number)
    {
        var files = _data.TicketAttachments.Where(x => x.TicketNumber == number).Select(x => AttachmentFile(x.Id)).ToList();
        _data.TicketParts.RemoveAll(x => x.TicketNumber == number);
        _data.TicketAttributeValues.RemoveAll(x => x.TicketNumber == number);
        _data.TicketAttachments.RemoveAll(x => x.TicketNumber == number);
        _data.TicketLinks.RemoveAll(x => x.TicketNumber == number || x.LinkedNumber == number);
        RemoveProjectTicketLinks(number);
        _data.Tickets.RemoveAll(x => x.Number == number);
        return files;
    }

    // Replaces a leaver's name and contact details everywhere the helpdesk stored them as data: their record, the
    // tickets and projects they raised, loans and asset holding periods, and asset history. Free text that other
    // people wrote about them on other tickets is not rewritten - the subject access export lists where it is.
    private void AnonymiseCore(UserRecord person, DateTime now)
    {
        var index = _data.Users.FindIndex(x => x.Id == person.Id);
        if (index < 0) return;
        var scrub = Scrubber(person);
        _data.Users[index] = person with
        {
            Name = AnonymisedName, Email = $"anonymised-{person.Id:N}@invalid", Department = "", Location = "", PasswordHash = null,
            CanRaiseProjects = false, IsProjectLead = false, RequirePasswordChange = false, AnonymisedAt = now
        };
        Actor? Rename(Actor? by) => by is { Id: null } a && string.Equals(a.Name, person.Name, StringComparison.Ordinal) ? new Actor(null, AnonymisedName) : by;

        for (var i = 0; i < _data.Tickets.Count; i++)
        {
            var t = _data.Tickets[i];
            if (t.RequesterId != person.Id) continue;
            _data.Tickets[i] = t with
            {
                Title = scrub(t.Title), Description = scrub(t.Description),
                Comments = t.Comments.Select(c => c with { Text = scrub(c.Text), By = Rename(c.By) }).ToList(),
                History = t.History.Select(h => h with { Details = scrub(h.Details), By = Rename(h.By) }).ToList()
            };
        }
        for (var i = 0; i < _data.Projects.Count; i++)
        {
            var p = _data.Projects[i];
            if (p.RequesterId != person.Id) continue;
            _data.Projects[i] = p with
            {
                Title = scrub(p.Title), ItemsWanted = scrub(p.ItemsWanted),
                Notes = p.Notes.Select(n => n with { Text = scrub(n.Text), By = Rename(n.By) }).ToList(),
                History = p.History.Select(h => h with { Details = scrub(h.Details), By = Rename(h.By) }).ToList()
            };
        }
        for (var i = 0; i < _data.KitLoans.Count; i++)
            if (_data.KitLoans[i].BorrowerUserId == person.Id)
                _data.KitLoans[i] = _data.KitLoans[i] with { BorrowerName = AnonymisedName, Notes = scrub(_data.KitLoans[i].Notes) };
        for (var i = 0; i < _data.Assets.Count; i++)
        {
            var a = _data.Assets[i];
            if (!a.Assignments.Any(x => x.UserId == person.Id)) continue;
            _data.Assets[i] = a with
            {
                Assignments = a.Assignments.Select(x => x.UserId == person.Id ? x with { UserName = AnonymisedName } : x).ToList(),
                History = a.History.Select(h => h with { Details = scrub(h.Details) }).ToList(),
                Comments = a.Comments.Select(c => c with { Text = scrub(c.Text) }).ToList()
            };
        }
    }

    // Replaces the person's email, and their full name as a whole phrase, with the anonymised name. A one-word name is
    // left alone in free text, where it would also match other people and ordinary words.
    private static Func<string, string> Scrubber(UserRecord person)
    {
        var name = IsFullName(person.Name) ? NamePattern(person.Name) : null;
        var email = person.Email.Length > 0 ? new Regex(Regex.Escape(person.Email), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) : null;
        return text =>
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (email is not null) text = email.Replace(text, AnonymisedName);
            if (name is not null) text = name.Replace(text, AnonymisedName);
            return text;
        };
    }

    // The audit log half of a retention run: old entries go, entries about deleted tickets go, and entries about
    // anonymised people lose their names. Then the in-memory copy is reloaded from the table.
    private void TrimAuditLog(DateTime? cutoff, IReadOnlyCollection<int> purgedTickets, IReadOnlyCollection<(Guid Id, string Name, string Email)> anonymised)
    {
        if (cutoff is null && purgedTickets.Count == 0 && anonymised.Count == 0) return;
        using var connection = new SqliteConnection($"Data Source={_path}");
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var statements = new StatementScope(transaction);
        if (cutoff is { } before)
            Execute(connection, transaction, "DELETE FROM AuditLog WHERE At < $cutoff;", ("$cutoff", Iso(before)));
        foreach (var number in purgedTickets)
            Execute(connection, transaction, "DELETE FROM AuditLog WHERE EntityType = 'Ticket' AND EntityKey = $key;", ("$key", number.ToString(CultureInfo.InvariantCulture)));
        foreach (var (id, name, email) in anonymised)
        {
            var person = new UserRecord(id, name, email, "", "");
            var scrub = Scrubber(person);
            var rows = new List<(long Id, string Entity, string Details, string? Actor, bool AboutThem)>();
            using (var select = connection.CreateCommand())
            {
                select.Transaction = transaction;
                select.CommandText = "SELECT Id, Entity, Details, Actor, EntityType = 'User' AND EntityKey = $key FROM AuditLog WHERE (EntityType = 'User' AND EntityKey = $key) OR (ActorId IS NULL AND Actor = $name) OR Details LIKE $email OR Details LIKE $namelike;";
                select.Parameters.AddWithValue("$key", id.ToString());
                select.Parameters.AddWithValue("$name", name);
                select.Parameters.AddWithValue("$email", email.Length > 0 ? $"%{email}%" : "\u0001");
                select.Parameters.AddWithValue("$namelike", IsFullName(name) ? $"%{name}%" : "\u0001");
                using var reader = select.ExecuteReader();
                while (reader.Read()) rows.Add((reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), !reader.IsDBNull(4) && reader.GetInt64(4) != 0));
            }
            foreach (var row in rows)
                Execute(connection, transaction, "UPDATE AuditLog SET Entity = $entity, Details = $details, Actor = $actor WHERE Id = $id;",
                    ("$entity", string.Equals(row.Entity, name, StringComparison.Ordinal) ? AnonymisedName : scrub(row.Entity)),
                    // A change to their own record lists their details field by field, so the whole line goes rather than
                    // just the name - what is left (a department, an old email) could still point at them.
                    ("$details", row.AboutThem ? "Details removed when this person's record was anonymised." : scrub(row.Details)),
                    ("$actor", string.Equals(row.Actor, name, StringComparison.Ordinal) ? AnonymisedName : row.Actor),
                    ("$id", row.Id));
        }
        transaction.Commit();
        _audit.Clear();
        _audit.AddRange(LoadAudit());
    }

    // ---- Storage ----

    private static void EnsureLifecycleSchema(SqliteConnection connection)
    {
        foreach (var sql in new[]
        {
            "ALTER TABLE Users ADD COLUMN LeftAt TEXT NULL;",
            "ALTER TABLE Users ADD COLUMN AnonymisedAt TEXT NULL;"
        })
        {
            using var migration = connection.CreateCommand();
            migration.CommandText = sql;
            try { migration.ExecuteNonQuery(); } catch (SqliteException ex) when (ex.SqliteErrorCode == 1) { }
        }
    }

    private static void ReadLifecycle(SqliteConnection connection, StoreData data)
    {
        static int Months(SqliteConnection c, string key) =>
            int.TryParse(ExecuteScalar(c, $"SELECT Value FROM Metadata WHERE Key = '{key}';") as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
        data.RetentionTicketMonths = Months(connection, "RetentionTicketMonths");
        data.RetentionLeaverMonths = Months(connection, "RetentionLeaverMonths");
        data.RetentionAuditMonths = Months(connection, "RetentionAuditMonths");
        if (ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'LastRetentionRunAt';") is string last
            && DateTime.TryParse(last, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at))
            data.LastRetentionRunAt = at.ToUniversalTime();
        data.LastRetentionSummary = ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'LastRetentionSummary';") as string ?? "";
    }

    private static void WriteLifecycleSettings(SqliteConnection connection, SqliteTransaction transaction, StoreData data)
    {
        SetMetadata(connection, transaction, "RetentionTicketMonths", data.RetentionTicketMonths.ToString(CultureInfo.InvariantCulture));
        SetMetadata(connection, transaction, "RetentionLeaverMonths", data.RetentionLeaverMonths.ToString(CultureInfo.InvariantCulture));
        SetMetadata(connection, transaction, "RetentionAuditMonths", data.RetentionAuditMonths.ToString(CultureInfo.InvariantCulture));
        SetMetadata(connection, transaction, "LastRetentionRunAt", data.LastRetentionRunAt is { } at ? Iso(at) : "");
        SetMetadata(connection, transaction, "LastRetentionSummary", data.LastRetentionSummary);
    }
}
