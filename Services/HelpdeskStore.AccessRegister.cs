using System.Globalization;
using EduHelpdesk.Models;
using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Services;

// The DfE access control register, merged from EduInventory: the systems and physical areas access is given to, each
// person's grants (people are the People directory), and the termly review. A grant is revoked, never deleted, when
// access ends - deleting is only for one recorded by mistake.
public sealed partial class HelpdeskStore
{
    public IReadOnlyList<AccessResource> AccessResources { get { lock (_sync) return _data.AccessResources.OrderBy(x => x.Name, NaturalComparer.Instance).ToList(); } }
    public IReadOnlyList<AccessGrant> AccessGrants { get { lock (_sync) return _data.AccessGrants.ToList(); } }
    public IReadOnlyList<AccessReview> AccessReviews { get { lock (_sync) return _data.AccessReviews.OrderByDescending(x => x.ReviewedOn).ThenByDescending(x => x.CreatedAt).ToList(); } }
    public AccessResource? FindAccessResource(Guid id) { lock (_sync) return _data.AccessResources.FirstOrDefault(x => x.Id == id); }
    public AccessGrant? FindAccessGrant(Guid id) { lock (_sync) return _data.AccessGrants.FirstOrDefault(x => x.Id == id); }
    public IReadOnlyList<string> PersonTypes { get { lock (_sync) return _data.PersonTypes.ToList(); } }
    public IReadOnlyList<string> AccessCategories { get { lock (_sync) return _data.AccessCategories.ToList(); } }
    public IReadOnlyList<string> RevokeReasons { get { lock (_sync) return _data.RevokeReasons.ToList(); } }
    public int AccessReviewDays { get { lock (_sync) return _data.AccessReviewDays; } }

    // Every problem with a person's live access, for the People list, the leaver check and the Overview.
    public int ActiveGrantCount(Guid personId)
    {
        lock (_sync) return _data.AccessGrants.Count(x => x.PersonId == personId && x.IsActive(AssetInsights.Today));
    }

    // Version 2 of the compliance defaults (see EnsureComplianceDefaults): the access register's lists, and the
    // business manager - who the standard says reviews access with IT - able to read the register and record reviews.
    private void EnsureAccessDefaults()
    {
        EnsureOptions(_data.PersonTypes, AccessDefaults.PersonTypes);
        EnsureOptions(_data.AccessCategories, AccessDefaults.Categories);
        EnsureOptions(_data.RevokeReasons, AccessDefaults.RevokeReasons);
        if (_data.Roles.FirstOrDefault(x => string.Equals(x.Name, BusinessManagerRole, StringComparison.OrdinalIgnoreCase)) is { } manager
            && manager.GrantsFor(Modules.Access) == ModulePermission.None)
            manager.Grants[Modules.Access] = Read | ModulePermission.Edit;
    }

    // Part of Prepare: grants pointing at nobody or nothing are dropped, and list values in use are kept on their lists.
    private void PrepareAccess()
    {
        _data.AccessResources ??= [];
        _data.AccessGrants ??= [];
        _data.AccessReviews ??= [];
        var contractIds = _data.Contracts.Select(x => x.Id).ToHashSet();
        _data.AccessResources = _data.AccessResources.Select(x => x with
        {
            Kind = AccessKinds.Find(x.Kind),
            MfaRequired = AccessKinds.Find(x.Kind) == AccessKinds.System && x.MfaRequired,
            ContractId = x.ContractId is { } id && contractIds.Contains(id) ? id : null
        }).ToList();
        var people = _data.Users.Select(x => x.Id).ToHashSet();
        var resources = _data.AccessResources.Select(x => x.Id).ToHashSet();
        _data.AccessGrants = _data.AccessGrants.Where(x => people.Contains(x.PersonId) && resources.Contains(x.ResourceId)).ToList();
        if (_data.AccessReviewDays is < 7 or > 730) _data.AccessReviewDays = AccessDefaults.ReviewDays;
        EnsureOptions(_data.PersonTypes, _data.Users.Select(x => x.PersonType).Where(x => !string.IsNullOrWhiteSpace(x)));
        EnsureOptions(_data.AccessCategories, _data.AccessResources.Select(x => x.Category).Where(x => !string.IsNullOrWhiteSpace(x)));
        EnsureOptions(_data.RevokeReasons, _data.AccessGrants.Select(x => x.RevokeReason).Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    // ---- Systems and areas ----

    public (bool Ok, string Message, Guid? Id) SaveAccessResource(AccessResource input)
    {
        lock (_sync)
        {
            var name = (input.Name ?? "").Trim();
            if (name.Length == 0) return (false, "Give the system or area a name.", null);
            if (name.Length > 150) return (false, "Keep the name under 150 characters.", null);
            var existing = _data.AccessResources.FirstOrDefault(x => x.Id == input.Id);
            if (_data.AccessResources.Any(x => x.Id != input.Id && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
                return (false, $"There is already a system or area called {name}.", null);
            var category = (input.Category ?? "").Trim();
            if (category.Length > 0)
            {
                var match = _data.AccessCategories.FirstOrDefault(x => string.Equals(x, category, StringComparison.OrdinalIgnoreCase));
                if (match is null && !string.Equals(category, existing?.Category, StringComparison.OrdinalIgnoreCase)) return (false, "Choose a category from the list.", null);
                category = match ?? category;
            }
            if (input.ContractId is { } contract && !_data.Contracts.Any(x => x.Id == contract)) return (false, "Choose a contract from the register.", null);
            var kind = AccessKinds.Find(input.Kind);
            var item = input with
            {
                Name = name, Kind = kind, Category = category, Owner = (input.Owner ?? "").Trim(), Notes = (input.Notes ?? "").Trim(),
                MfaRequired = kind == AccessKinds.System && input.MfaRequired
            };
            if (existing is null)
            {
                item = item with { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
                _data.AccessResources.Add(item);
                Save();
                return (true, $"{name} added.", item.Id);
            }
            item = item with { CreatedAt = existing.CreatedAt };
            if (item == existing) return (true, "Nothing had changed.", item.Id);
            _data.AccessResources[_data.AccessResources.IndexOf(existing)] = item;
            Save();
            return (true, $"{name} saved.", item.Id);
        }
    }

    // Only something nobody was ever given can go. Otherwise it is retired, so the history stays.
    public string? DeleteAccessResource(Guid id)
    {
        lock (_sync)
        {
            var existing = _data.AccessResources.FirstOrDefault(x => x.Id == id);
            if (existing is null) return "That system or area wasn't found.";
            var grants = _data.AccessGrants.Count(x => x.ResourceId == id);
            if (grants > 0) return $"{existing.Name} has {Plural(grants, "access record")}. Mark it as retired instead, so the history is kept.";
            _data.AccessResources.Remove(existing);
            Save();
            return null;
        }
    }

    // ---- Grants ----

    public (bool Ok, string Message, Guid? Id) AddAccessGrant(AccessGrant input)
    {
        lock (_sync)
        {
            var person = _data.Users.FirstOrDefault(x => x.Id == input.PersonId);
            if (person is null) return (false, "Choose a person.", null);
            var resource = _data.AccessResources.FirstOrDefault(x => x.Id == input.ResourceId);
            if (resource is null) return (false, "Choose a system or area.", null);
            if (resource.IsRetired) return (false, $"{resource.Name} is retired, so access can't be granted to it.", null);
            var (item, error) = CleanGrant(input with { GrantedOn = input.GrantedOn ?? AssetInsights.Today, GrantedBy = string.IsNullOrWhiteSpace(input.GrantedBy) ? CurrentActor().Name : input.GrantedBy }, resource);
            if (item is null) return (false, error!, null);
            var today = AssetInsights.Today;
            if (_data.AccessGrants.Any(x => x.PersonId == person.Id && x.ResourceId == resource.Id && x.IsActive(today)
                                           && string.Equals(x.AccessLevel, item.AccessLevel, StringComparison.OrdinalIgnoreCase)))
                return (false, $"{person.Name} already has that access to {resource.Name}.", null);
            item = item with { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow, RevokedOn = null, RevokedBy = "", RevokeReason = "" };
            _data.AccessGrants.Add(item);
            Save();
            return (true, $"Access to {resource.Name} recorded for {person.Name}.", item.Id);
        }
    }

    public (bool Ok, string Message) UpdateAccessGrant(AccessGrant input)
    {
        lock (_sync)
        {
            var index = _data.AccessGrants.FindIndex(x => x.Id == input.Id);
            if (index < 0) return (false, "That access record wasn't found.");
            var existing = _data.AccessGrants[index];
            var resource = _data.AccessResources.FirstOrDefault(x => x.Id == existing.ResourceId);
            if (resource is null) return (false, "That system or area wasn't found.");
            // Who and what it is for are fixed: a different person or system is a different grant.
            var (item, error) = CleanGrant(input with { PersonId = existing.PersonId, ResourceId = existing.ResourceId }, resource);
            if (item is null) return (false, error!);
            if (item.RevokedOn is not null && existing.RevokedOn is null && item.RevokedBy.Length == 0) item = item with { RevokedBy = CurrentActor().Name };
            item = item with { CreatedAt = existing.CreatedAt, OnboardingTicket = existing.OnboardingTicket };
            if (item == existing) return (true, "Nothing had changed.");
            _data.AccessGrants[index] = item;
            Save();
            return (true, "Access record saved.");
        }
    }

    private (AccessGrant? Grant, string? Error) CleanGrant(AccessGrant input, AccessResource resource)
    {
        static string Text(string? value) => (value ?? "").Trim();
        if (input.GrantedOn is null) return (null, "Enter the date access was granted.");
        if (input.GrantedOn > AssetInsights.Today.AddYears(1)) return (null, "The grant date is too far ahead.");
        if (input.RevokedOn is { } revoked && revoked < input.GrantedOn) return (null, "Access can't be removed before it was granted.");
        if (Text(input.Notes).Length > MaxContractTextLength) return (null, $"Keep the notes under {MaxContractTextLength} characters.");
        var reason = Text(input.RevokeReason);
        if (input.RevokedOn is not null && reason.Length > 0)
            reason = _data.RevokeReasons.FirstOrDefault(x => string.Equals(x, reason, StringComparison.OrdinalIgnoreCase)) ?? reason;
        var approvedBy = Text(input.ApprovedBy);
        return (input with
        {
            AccessLevel = Text(input.AccessLevel), Identifier = Text(input.Identifier), GrantedBy = Text(input.GrantedBy),
            ApprovedBy = approvedBy, ApprovedOn = approvedBy.Length == 0 ? null : input.ApprovedOn,
            LastReviewedBy = Text(input.LastReviewedBy),
            RevokedBy = input.RevokedOn is null ? "" : Text(input.RevokedBy),
            RevokeReason = input.RevokedOn is null ? "" : reason,
            // MFA is about signing in to a system; for a key it means nothing.
            Mfa = resource.IsSystem ? MfaStates.Find(input.Mfa) : MfaStates.NotApplicable,
            Notes = Text(input.Notes)
        }, null);
    }

    public (bool Ok, string Message) RevokeAccessGrant(Guid id, DateOnly? on, string? reason)
    {
        lock (_sync)
        {
            if (on is null) return (false, "Enter the date access was removed.");
            var index = _data.AccessGrants.FindIndex(x => x.Id == id);
            if (index < 0) return (false, "That access record wasn't found.");
            if (_data.AccessGrants[index].RevokedOn is not null) return (false, "That access has already been removed.");
            _data.AccessGrants[index] = Revoked(_data.AccessGrants[index], on.Value, reason);
            Save();
            return (true, "Access removed.");
        }
    }

    // The leaver check's "remove all access": everything they still hold, as of the day they left (or today).
    public (bool Ok, string Message) RevokeAllAccess(Guid personId, DateOnly? on, string? reason)
    {
        lock (_sync)
        {
            var person = _data.Users.FirstOrDefault(x => x.Id == personId);
            if (person is null) return (false, "That person was not found.");
            var date = on ?? (person.IsActive ? AssetInsights.Today : AccessRules.LeaveDate(person) ?? AssetInsights.Today);
            var count = 0;
            for (var i = 0; i < _data.AccessGrants.Count; i++)
            {
                var grant = _data.AccessGrants[i];
                if (grant.PersonId != personId || grant.RevokedOn is not null) continue;
                _data.AccessGrants[i] = Revoked(grant, date, reason ?? (person.IsActive ? null : AccessDefaults.Leaver));
                count++;
            }
            if (count == 0) return (false, $"{person.Name} holds no access to remove.");
            Save();
            return (true, $"{Plural(count, "access record")} removed from {person.Name}, as of {date:d MMM yyyy}.");
        }
    }

    private AccessGrant Revoked(AccessGrant grant, DateOnly on, string? reason)
    {
        var text = (reason ?? "").Trim();
        return grant with
        {
            // Never before it was granted - that would say they never had it.
            RevokedOn = grant.GrantedOn is { } granted && on < granted ? granted : on,
            RevokedBy = CurrentActor().Name,
            RevokeReason = _data.RevokeReasons.FirstOrDefault(x => string.Equals(x, text, StringComparison.OrdinalIgnoreCase)) ?? text
        };
    }

    // For a grant recorded by mistake. Access that ended is revoked instead, so the history stays.
    public string? DeleteAccessGrant(Guid id)
    {
        lock (_sync)
        {
            var existing = _data.AccessGrants.FirstOrDefault(x => x.Id == id);
            if (existing is null) return "That access record wasn't found.";
            _data.AccessGrants.Remove(existing);
            Save();
            return null;
        }
    }

    // ---- Reviews ----

    // One step: every grant confirmed gets the review's date, everything marked for removal is revoked, and the review
    // itself is kept as the evidence.
    public (bool Ok, string Message) CompleteAccessReview(DateOnly? reviewedOn, string? reviewedWith, string? scope, string? notes,
        IReadOnlyCollection<Guid> confirmIds, IReadOnlyCollection<Guid> revokeIds, string? revokeReason)
    {
        lock (_sync)
        {
            if (reviewedOn is not { } on) return (false, "Enter the date of the review.");
            if (on > AssetInsights.Today) return (false, "The review date can't be in the future.");
            if (string.IsNullOrWhiteSpace(reviewedWith)) return (false, "Say who the review was carried out with. The standard asks for reviews with business or finance staff.");
            if (confirmIds.Count + revokeIds.Count == 0) return (false, "Nothing was reviewed.");
            var who = CurrentActor().Name;
            int confirmed = 0, revoked = 0;
            var reason = string.IsNullOrWhiteSpace(revokeReason) ? AccessDefaults.RemovedAtReview : revokeReason;
            for (var i = 0; i < _data.AccessGrants.Count; i++)
            {
                var grant = _data.AccessGrants[i];
                if (grant.RevokedOn is not null) continue;
                if (revokeIds.Contains(grant.Id)) { _data.AccessGrants[i] = Revoked(grant, on, reason) with { LastReviewedOn = on, LastReviewedBy = who }; revoked++; }
                else if (confirmIds.Contains(grant.Id)) { _data.AccessGrants[i] = grant with { LastReviewedOn = on, LastReviewedBy = who }; confirmed++; }
            }
            if (confirmed + revoked == 0) return (false, "None of that access is still live - reload the page and try again.");
            _data.AccessReviews.Add(new AccessReview(Guid.NewGuid(), on, DateTime.UtcNow)
            {
                ReviewedBy = who, ReviewedWith = reviewedWith.Trim(), Scope = (scope ?? "").Trim(), Notes = (notes ?? "").Trim(),
                Confirmed = confirmed, Revoked = revoked
            });
            Save();
            return (true, $"Review recorded: {confirmed} confirmed, {revoked} removed.");
        }
    }

    public string SetAccessReviewDays(int days)
    {
        lock (_sync)
        {
            if (days is < 7 or > 730) return "Enter a number of days between 7 and 730.";
            _data.AccessReviewDays = days;
            Save();
            return "Access review interval saved.";
        }
    }

    // ---- Onboarding: a task that records access when ticked ----

    // Ticking the task records the new starter's access to the task's system, granted today by whoever ticked it.
    // Returns the grant to add, or why not. The caller holds the lock and saves.
    private (AccessGrant? Grant, string? Error) OnboardingGrant(OnboardingRecord record, OnboardingTask task)
    {
        var resource = task.AccessResourceId is { } id ? _data.AccessResources.FirstOrDefault(x => x.Id == id) : null;
        if (resource is null) return (null, "The system this task gives access to is no longer in the access register. Choose another in Settings → Onboarding checklists.");
        if (resource.IsRetired) return (null, $"{resource.Name} is retired, so access can't be granted to it.");
        if (_data.AccessGrants.Any(x => x.PersonId == record.StarterId && x.ResourceId == resource.Id && x.IsActive(AssetInsights.Today)))
            return (null, null);
        return (new AccessGrant(Guid.NewGuid(), record.StarterId, resource.Id, DateTime.UtcNow)
        {
            GrantedOn = AssetInsights.Today, GrantedBy = CurrentActor().Name, Mfa = resource.IsSystem ? MfaStates.NotEnabled : MfaStates.NotApplicable,
            Notes = $"Recorded by the onboarding task \"{task.Title}\".", OnboardingTicket = record.TicketNumber
        }, null);
    }

    // ---- Storage ----

    private static void EnsureAccessSchema(SqliteConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS PersonTypes (Name TEXT PRIMARY KEY);
                CREATE TABLE IF NOT EXISTS AccessCategories (Name TEXT PRIMARY KEY);
                CREATE TABLE IF NOT EXISTS RevokeReasons (Name TEXT PRIMARY KEY);
                CREATE TABLE IF NOT EXISTS AccessResources (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, Kind TEXT NOT NULL, Category TEXT NOT NULL DEFAULT '',
                    Owner TEXT NOT NULL DEFAULT '', MfaRequired INTEGER NOT NULL DEFAULT 0, HoldsPersonalData INTEGER NOT NULL DEFAULT 0, ContractId TEXT NULL,
                    Notes TEXT NOT NULL DEFAULT '', IsRetired INTEGER NOT NULL DEFAULT 0, CreatedAt TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS AccessGrants (Id TEXT PRIMARY KEY, PersonId TEXT NOT NULL, ResourceId TEXT NOT NULL, AccessLevel TEXT NOT NULL DEFAULT '',
                    Identifier TEXT NOT NULL DEFAULT '', Privileged INTEGER NOT NULL DEFAULT 0, Mfa TEXT NOT NULL DEFAULT '', GrantedOn TEXT NULL, GrantedBy TEXT NOT NULL DEFAULT '',
                    ApprovedBy TEXT NOT NULL DEFAULT '', ApprovedOn TEXT NULL, LastReviewedOn TEXT NULL, LastReviewedBy TEXT NOT NULL DEFAULT '',
                    RevokedOn TEXT NULL, RevokedBy TEXT NOT NULL DEFAULT '', RevokeReason TEXT NOT NULL DEFAULT '', Notes TEXT NOT NULL DEFAULT '',
                    OnboardingTicket INTEGER NULL, CreatedAt TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS AccessReviews (Id TEXT PRIMARY KEY, ReviewedOn TEXT NOT NULL, ReviewedBy TEXT NOT NULL DEFAULT '', ReviewedWith TEXT NOT NULL DEFAULT '',
                    Scope TEXT NOT NULL DEFAULT '', Confirmed INTEGER NOT NULL DEFAULT 0, Revoked INTEGER NOT NULL DEFAULT 0, Notes TEXT NOT NULL DEFAULT '', CreatedAt TEXT NOT NULL);
                """;
            command.ExecuteNonQuery();
        }
        foreach (var sql in new[]
        {
            "ALTER TABLE Users ADD COLUMN PersonType TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE Users ADD COLUMN StartDate TEXT NULL;"
        })
        {
            using var migration = connection.CreateCommand();
            migration.CommandText = sql;
            try { migration.ExecuteNonQuery(); } catch (SqliteException ex) when (ex.SqliteErrorCode == 1) { }
        }
    }

    private static void ReadAccess(SqliteConnection connection, StoreData data)
    {
        ReadStrings(connection, "PersonTypes", data.PersonTypes);
        ReadStrings(connection, "AccessCategories", data.AccessCategories);
        ReadStrings(connection, "RevokeReasons", data.RevokeReasons);
        if (int.TryParse(ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'AccessReviewDays';") as string, out var days)) data.AccessReviewDays = days;
        using (var command = connection.CreateCommand())
        {
            // Ordinal-indexed: add new columns to the END.
            command.CommandText = "SELECT Id, Name, Kind, Category, Owner, MfaRequired, HoldsPersonalData, ContractId, Notes, IsRetired, CreatedAt FROM AccessResources;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                data.AccessResources.Add(new AccessResource(Guid.Parse(reader.GetString(0)), reader.GetString(1), Date(reader, 10))
                {
                    Kind = reader.GetString(2), Category = reader.GetString(3), Owner = reader.GetString(4), MfaRequired = reader.GetInt32(5) != 0,
                    HoldsPersonalData = reader.GetInt32(6) != 0, ContractId = NullableGuid(reader, 7), Notes = reader.GetString(8), IsRetired = reader.GetInt32(9) != 0
                });
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, PersonId, ResourceId, AccessLevel, Identifier, Privileged, Mfa, GrantedOn, GrantedBy, ApprovedBy, ApprovedOn, LastReviewedOn, LastReviewedBy, RevokedOn, RevokedBy, RevokeReason, Notes, OnboardingTicket, CreatedAt FROM AccessGrants ORDER BY CreatedAt;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                data.AccessGrants.Add(new AccessGrant(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), Guid.Parse(reader.GetString(2)), Date(reader, 18))
                {
                    AccessLevel = reader.GetString(3), Identifier = reader.GetString(4), Privileged = reader.GetInt32(5) != 0, Mfa = reader.GetString(6),
                    GrantedOn = NullableDateOnly(reader, 7), GrantedBy = reader.GetString(8), ApprovedBy = reader.GetString(9), ApprovedOn = NullableDateOnly(reader, 10),
                    LastReviewedOn = NullableDateOnly(reader, 11), LastReviewedBy = reader.GetString(12), RevokedOn = NullableDateOnly(reader, 13),
                    RevokedBy = reader.GetString(14), RevokeReason = reader.GetString(15), Notes = reader.GetString(16),
                    OnboardingTicket = reader.IsDBNull(17) ? null : reader.GetInt32(17)
                });
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, ReviewedOn, ReviewedBy, ReviewedWith, Scope, Confirmed, Revoked, Notes, CreatedAt FROM AccessReviews;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                data.AccessReviews.Add(new AccessReview(Guid.Parse(reader.GetString(0)), NullableDateOnly(reader, 1) ?? DateOnly.MinValue, Date(reader, 8))
                {
                    ReviewedBy = reader.GetString(2), ReviewedWith = reader.GetString(3), Scope = reader.GetString(4),
                    Confirmed = reader.GetInt32(5), Revoked = reader.GetInt32(6), Notes = reader.GetString(7)
                });
        }
    }

    private static void WriteAccess(SqliteConnection connection, SqliteTransaction transaction, StoreData data)
    {
        InsertStrings(connection, transaction, "PersonTypes", data.PersonTypes);
        InsertStrings(connection, transaction, "AccessCategories", data.AccessCategories);
        InsertStrings(connection, transaction, "RevokeReasons", data.RevokeReasons);
        SetMetadata(connection, transaction, "AccessReviewDays", data.AccessReviewDays.ToString(CultureInfo.InvariantCulture));
        foreach (var x in data.AccessResources)
            Execute(connection, transaction, "INSERT INTO AccessResources (Id, Name, Kind, Category, Owner, MfaRequired, HoldsPersonalData, ContractId, Notes, IsRetired, CreatedAt) VALUES ($id,$name,$kind,$category,$owner,$mfa,$personal,$contract,$notes,$retired,$created);",
                ("$id", x.Id.ToString()), ("$name", x.Name), ("$kind", x.Kind), ("$category", x.Category ?? ""), ("$owner", x.Owner ?? ""),
                ("$mfa", x.MfaRequired ? 1 : 0), ("$personal", x.HoldsPersonalData ? 1 : 0), ("$contract", x.ContractId?.ToString()),
                ("$notes", x.Notes ?? ""), ("$retired", x.IsRetired ? 1 : 0), ("$created", Iso(x.CreatedAt)));
        foreach (var x in data.AccessGrants)
            Execute(connection, transaction, "INSERT INTO AccessGrants (Id, PersonId, ResourceId, AccessLevel, Identifier, Privileged, Mfa, GrantedOn, GrantedBy, ApprovedBy, ApprovedOn, LastReviewedOn, LastReviewedBy, RevokedOn, RevokedBy, RevokeReason, Notes, OnboardingTicket, CreatedAt) VALUES ($id,$person,$resource,$level,$identifier,$privileged,$mfa,$granted,$grantedby,$approvedby,$approvedon,$reviewed,$reviewedby,$revoked,$revokedby,$reason,$notes,$onboarding,$created);",
                ("$id", x.Id.ToString()), ("$person", x.PersonId.ToString()), ("$resource", x.ResourceId.ToString()), ("$level", x.AccessLevel ?? ""),
                ("$identifier", x.Identifier ?? ""), ("$privileged", x.Privileged ? 1 : 0), ("$mfa", x.Mfa ?? ""), ("$granted", IsoDay(x.GrantedOn)),
                ("$grantedby", x.GrantedBy ?? ""), ("$approvedby", x.ApprovedBy ?? ""), ("$approvedon", IsoDay(x.ApprovedOn)), ("$reviewed", IsoDay(x.LastReviewedOn)),
                ("$reviewedby", x.LastReviewedBy ?? ""), ("$revoked", IsoDay(x.RevokedOn)), ("$revokedby", x.RevokedBy ?? ""), ("$reason", x.RevokeReason ?? ""),
                ("$notes", x.Notes ?? ""), ("$onboarding", x.OnboardingTicket), ("$created", Iso(x.CreatedAt)));
        foreach (var x in data.AccessReviews)
            Execute(connection, transaction, "INSERT INTO AccessReviews (Id, ReviewedOn, ReviewedBy, ReviewedWith, Scope, Confirmed, Revoked, Notes, CreatedAt) VALUES ($id,$on,$by,$with,$scope,$confirmed,$revoked,$notes,$created);",
                ("$id", x.Id.ToString()), ("$on", IsoDay(x.ReviewedOn)), ("$by", x.ReviewedBy ?? ""), ("$with", x.ReviewedWith ?? ""), ("$scope", x.Scope ?? ""),
                ("$confirmed", x.Confirmed), ("$revoked", x.Revoked), ("$notes", x.Notes ?? ""), ("$created", Iso(x.CreatedAt)));
    }
}
