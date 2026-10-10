using System.Globalization;
using EduHelpdesk.Models;
using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Services;

// The DfE digital technology contracts register, merged from EduInventory: what the school pays for, renewals and
// notice dates (ContractRules), the supplier from the supplier directory, and the leased assets that belong to each.
public sealed partial class HelpdeskStore
{
    public const string BusinessManagerRole = "Business manager";
    public const string GovernorRole = "Governor or auditor";
    public const int MaxContractTextLength = 4000;

    public IReadOnlyList<ContractRecord> Contracts { get { lock (_sync) return _data.Contracts.OrderBy(x => x.Name, NaturalComparer.Instance).ToList(); } }
    public ContractRecord? FindContract(Guid id) { lock (_sync) return _data.Contracts.FirstOrDefault(x => x.Id == id); }
    public IReadOnlyList<string> ContractTypes { get { lock (_sync) return _data.ContractTypes.ToList(); } }
    public IReadOnlyList<string> SpendCategories { get { lock (_sync) return _data.SpendCategories.ToList(); } }
    public IReadOnlyList<string> ContractDurations { get { lock (_sync) return _data.ContractDurations.ToList(); } }
    public IReadOnlyList<string> ContractStatuses { get { lock (_sync) return _data.ContractStatuses.ToList(); } }

    // Put in place once, on new and upgraded installs alike: the DfE template's dropdown lists as each list's starting
    // values, and two roles for the people the registers are for. Nobody holds a role until given it, so adding them
    // grants nothing. Called after EnsureSeedRoles, for the reason given on EnsureOnboardingRole.
    private void EnsureComplianceDefaults()
    {
        if (_data.Roles.Count == 0) return;
        if (_data.ComplianceVersion < 1) EnsureContractDefaults();
        if (_data.ComplianceVersion < 2)
        {
            EnsureAccessDefaults();
            _data.ComplianceVersion = 2;
        }
    }

    private void EnsureContractDefaults()
    {
        EnsureOptions(_data.ContractTypes, ContractStatusDefaults.Types);
        EnsureOptions(_data.SpendCategories, ContractStatusDefaults.SpendCategories);
        EnsureOptions(_data.ContractDurations, ContractStatusDefaults.Durations);
        EnsureOptions(_data.ContractStatuses, ContractStatusDefaults.All);
        // Runs the contracts register and the suppliers behind it, and reads the asset register and the money reports.
        if (!_data.Roles.Any(x => string.Equals(x.Name, BusinessManagerRole, StringComparison.OrdinalIgnoreCase)))
            _data.Roles.Add(Role(BusinessManagerRole,
                new()
                {
                    [Modules.Contracts] = Full, [Modules.Suppliers] = Full, [Modules.Assets] = Read, [Modules.Projects] = Read,
                    [Modules.Reports] = ModulePermission.Access
                },
                Modules.Flags.ReportAssets, Modules.Flags.ReportFinance, Modules.Flags.ReportProjects, Modules.Flags.ReportExport));
        // Reads the registers and the reports an audit or a governors' committee asks for, and changes nothing.
        if (!_data.Roles.Any(x => string.Equals(x.Name, GovernorRole, StringComparison.OrdinalIgnoreCase)))
            _data.Roles.Add(Role(GovernorRole,
                new()
                {
                    [Modules.Contracts] = Read, [Modules.Assets] = Read, [Modules.Suppliers] = Read,
                    [Modules.Reports] = ModulePermission.Access
                },
                Modules.Flags.ReportAssets, Modules.Flags.ReportFinance, Modules.Flags.ReportExport));
        _data.ComplianceVersion = 1;
    }

    // Tidies loaded contracts and keeps links pointing at things that exist. Part of Prepare.
    private void PrepareContracts()
    {
        _data.Contracts ??= [];
        var supplierIds = _data.Suppliers.Select(x => x.Id).ToHashSet();
        _data.Contracts = _data.Contracts.Select(x => x with
        {
            SupplierId = x.SupplierId is { } supplier && supplierIds.Contains(supplier) ? supplier : null,
            CostPeriod = ContractCostPeriods.Find(x.CostPeriod),
            Status = string.IsNullOrWhiteSpace(x.Status) ? ContractStatusDefaults.Active : x.Status.Trim()
        }).ToList();
        var contractIds = _data.Contracts.Select(x => x.Id).ToHashSet();
        _data.Assets = _data.Assets.Select(x => x.ContractId is { } id && !contractIds.Contains(id) ? x with { ContractId = null } : x).ToList();
        EnsureOptions(_data.ContractTypes, _data.Contracts.Select(x => x.ContractType).Where(x => !string.IsNullOrWhiteSpace(x)));
        EnsureOptions(_data.SpendCategories, _data.Contracts.Select(x => x.SpendCategory).Where(x => !string.IsNullOrWhiteSpace(x)));
        EnsureOptions(_data.ContractDurations, _data.Contracts.Select(x => x.Duration).Where(x => !string.IsNullOrWhiteSpace(x)));
        EnsureOptions(_data.ContractStatuses, _data.Contracts.Select(x => x.Status));
    }

    public (bool Ok, string Message, Guid? Id) AddContract(ContractRecord contract)
    {
        lock (_sync)
        {
            var (clean, error) = CleanContract(contract, null);
            if (clean is null) return (false, error!, null);
            clean = clean with { Id = contract.Id == Guid.Empty ? Guid.NewGuid() : contract.Id, CreatedAt = DateTime.UtcNow };
            if (_data.Contracts.Any(x => x.Id == clean.Id)) clean = clean with { Id = Guid.NewGuid() };
            _data.Contracts.Add(clean);
            Save();
            return (true, $"{clean.Name} added to the contracts register.", clean.Id);
        }
    }

    public (bool Ok, string Message) UpdateContract(ContractRecord contract)
    {
        lock (_sync)
        {
            var index = _data.Contracts.FindIndex(x => x.Id == contract.Id);
            if (index < 0) return (false, "Contract was not found.");
            var current = _data.Contracts[index];
            var (clean, error) = CleanContract(contract, current);
            if (clean is null) return (false, error!);
            // Where it came from and when it was first recorded aren't edited.
            clean = clean with { CreatedAt = current.CreatedAt, ProjectNumber = current.ProjectNumber, ProjectItemId = current.ProjectItemId };
            if (clean == current) return (true, "Nothing had changed.");
            _data.Contracts[index] = clean;
            Save();
            return (true, "Contract updated.");
        }
    }

    // Leased assets linked to it are unlinked rather than refused: the asset still exists, it just loses the pointer.
    public string? DeleteContract(Guid id)
    {
        lock (_sync)
        {
            var index = _data.Contracts.FindIndex(x => x.Id == id);
            if (index < 0) return "Contract was not found.";
            for (var i = 0; i < _data.Assets.Count; i++)
                if (_data.Assets[i].ContractId == id) ApplyAssetUpdate(i, _data.Assets[i] with { ContractId = null });
            _data.Contracts.RemoveAt(index);
            Save();
            return null;
        }
    }

    // Links (or with null unlinks) an asset to the contract it is leased or loaned under.
    public string? CheckContractLink(Guid? contractId)
    {
        lock (_sync) return contractId is { } id && !_data.Contracts.Any(x => x.Id == id) ? "Select a contract from the register." : null;
    }

    // The contract as it can be saved, or why not. Values from the Settings lists are matched to the list's spelling; a
    // value that has since left its list is kept while unchanged, so tidying a list never blocks saving a contract.
    private (ContractRecord? Contract, string? Error) CleanContract(ContractRecord input, ContractRecord? current)
    {
        static string Text(string? value) => (value ?? string.Empty).Trim();
        var name = Text(input.Name);
        if (name.Length == 0) return (null, "Enter the name of the contract or service.");
        if (name.Length > 200) return (null, "Keep the name under 200 characters.");
        foreach (var long_ in new[] { input.Description, input.Notes, input.CostNotes })
            if (Text(long_).Length > MaxContractTextLength) return (null, $"Keep each text box under {MaxContractTextLength} characters.");

        string? FromList(string? value, string? was, List<string> list, string label, out string? error)
        {
            error = null;
            var text = Text(value);
            if (text.Length == 0) return string.Empty;
            if (list.FirstOrDefault(x => string.Equals(x, text, StringComparison.OrdinalIgnoreCase)) is { } match) return match;
            if (string.Equals(text, Text(was), StringComparison.OrdinalIgnoreCase)) return Text(was);
            error = $"Choose a {label} from the list.";
            return null;
        }
        var type = FromList(input.ContractType, current?.ContractType, _data.ContractTypes, "contract type", out var e1);
        var spend = FromList(input.SpendCategory, current?.SpendCategory, _data.SpendCategories, "spend category", out var e2);
        var duration = FromList(input.Duration, current?.Duration, _data.ContractDurations, "duration", out var e3);
        var status = FromList(input.Status, current?.Status, _data.ContractStatuses, "status", out var e4);
        if ((e1 ?? e2 ?? e3 ?? e4) is { } listError) return (null, listError);
        if (string.IsNullOrEmpty(status)) status = _data.ContractStatuses.FirstOrDefault() ?? ContractStatusDefaults.Active;

        if (input.SupplierId is { } supplier && !_data.Suppliers.Any(x => x.Id == supplier)) return (null, "Choose a supplier from the directory.");
        if (input.Cost is < 0 or > 100_000_000m) return (null, "Enter the cost as a positive amount, or leave it blank.");
        if (input.NoticeMonths is < 0 or > 60) return (null, "Enter the notice period as a number of months between 0 and 60, or leave it blank.");
        if (input.StartDate is { } start && input.EndDate is { } end && end < start) return (null, "The end date is before the start date.");

        return (input with
        {
            Name = name,
            Description = Text(input.Description),
            ContractType = type!, SpendCategory = spend!, Duration = duration!, Status = status!,
            SupplierContact = Text(input.SupplierContact),
            Cost = input.Cost is { } cost ? Math.Round(cost, 2) : null,
            CostPeriod = ContractCostPeriods.Find(input.CostPeriod),
            CostNotes = Text(input.CostNotes),
            RenewalType = ContractRenewalTypes.Find(input.RenewalType),
            NoticeMonths = input.NoticeMonths is 0 ? null : input.NoticeMonths,
            ContractOwner = Text(input.ContractOwner),
            ProcurementApproach = Text(input.ProcurementApproach),
            RelatedPartyReportedOn = input.RelatedParty ? input.RelatedPartyReportedOn : null,
            Notes = Text(input.Notes)
        }, null);
    }

    // A new contract filled in from the quote chosen for one item of an approved project, or why there isn't one.
    public (ContractRecord? Contract, string? Error) ContractFromProject(int number, Guid itemId)
    {
        lock (_sync)
        {
            var project = _data.Projects.FirstOrDefault(x => x.Number == number);
            if (project is null) return (null, "Project was not found.");
            if (project.Outcome != ProjectOutcomes.Approved) return (null, $"{project.Reference} wasn't closed as approved.");
            var item = project.Items.FirstOrDefault(x => x.Id == itemId);
            if (item?.Chosen is not { } quote) return (null, "That item has no chosen quote.");
            var start = project.ClosedAt is { } closed ? DateOnly.FromDateTime(closed.ToLocalTime()) : AssetInsights.Today;
            return (ContractRules.FromQuote(project, item, quote, start, _data.ContractDurations), null);
        }
    }

    // ---- Storage ----

    private static void EnsureContractSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ContractTypes (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS SpendCategories (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS ContractDurations (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS ContractStatuses (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS Contracts (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, Description TEXT NOT NULL DEFAULT '',
                ContractType TEXT NOT NULL DEFAULT '', SpendCategory TEXT NOT NULL DEFAULT '', Duration TEXT NOT NULL DEFAULT '',
                Status TEXT NOT NULL DEFAULT 'Active', SupplierId TEXT NULL, SupplierContact TEXT NOT NULL DEFAULT '',
                Cost TEXT NULL, CostPeriod TEXT NOT NULL DEFAULT '', CostNotes TEXT NOT NULL DEFAULT '', RenewalType TEXT NOT NULL DEFAULT '',
                StartDate TEXT NULL, EndDate TEXT NULL, NextRenewalDate TEXT NULL, NoticeMonths INTEGER NULL,
                ContractOwner TEXT NOT NULL DEFAULT '', ProcurementApproach TEXT NOT NULL DEFAULT '',
                ApprovedApp INTEGER NOT NULL DEFAULT 0, ProcessesPersonalData INTEGER NOT NULL DEFAULT 0,
                RelatedParty INTEGER NOT NULL DEFAULT 0, RelatedPartyReportedOn TEXT NULL, Notes TEXT NOT NULL DEFAULT '',
                ProjectNumber INTEGER NULL, ProjectItemId TEXT NULL, CreatedAt TEXT NOT NULL);
            """;
        command.ExecuteNonQuery();
    }

    private static void ReadContracts(SqliteConnection connection, StoreData data)
    {
        ReadStrings(connection, "ContractTypes", data.ContractTypes);
        ReadStrings(connection, "SpendCategories", data.SpendCategories);
        ReadStrings(connection, "ContractDurations", data.ContractDurations);
        ReadStrings(connection, "ContractStatuses", data.ContractStatuses);
        if (int.TryParse(ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'ComplianceVersion';") as string, out var version)) data.ComplianceVersion = version;
        using var command = connection.CreateCommand();
        // Ordinal-indexed: add new columns to the END of this list (see the note on the Assets reader).
        command.CommandText = "SELECT Id, Name, Description, ContractType, SpendCategory, Duration, Status, SupplierId, SupplierContact, Cost, CostPeriod, CostNotes, RenewalType, StartDate, EndDate, NextRenewalDate, NoticeMonths, ContractOwner, ProcurementApproach, ApprovedApp, ProcessesPersonalData, RelatedParty, RelatedPartyReportedOn, Notes, ProjectNumber, ProjectItemId, CreatedAt FROM Contracts;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            data.Contracts.Add(new ContractRecord(Guid.Parse(reader.GetString(0)), reader.GetString(1), Date(reader, 26))
            {
                Description = reader.GetString(2), ContractType = reader.GetString(3), SpendCategory = reader.GetString(4),
                Duration = reader.GetString(5), Status = reader.GetString(6), SupplierId = NullableGuid(reader, 7),
                SupplierContact = reader.GetString(8),
                Cost = NullableString(reader, 9) is { } cost && decimal.TryParse(cost, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null,
                CostPeriod = reader.GetString(10), CostNotes = reader.GetString(11), RenewalType = reader.GetString(12),
                StartDate = NullableDateOnly(reader, 13), EndDate = NullableDateOnly(reader, 14), NextRenewalDate = NullableDateOnly(reader, 15),
                NoticeMonths = reader.IsDBNull(16) ? null : reader.GetInt32(16),
                ContractOwner = reader.GetString(17), ProcurementApproach = reader.GetString(18),
                ApprovedApp = reader.GetInt32(19) != 0, ProcessesPersonalData = reader.GetInt32(20) != 0, RelatedParty = reader.GetInt32(21) != 0,
                RelatedPartyReportedOn = NullableDateOnly(reader, 22), Notes = reader.GetString(23),
                ProjectNumber = reader.IsDBNull(24) ? null : reader.GetInt32(24), ProjectItemId = NullableGuid(reader, 25)
            });
    }

    private static void WriteContracts(SqliteConnection connection, SqliteTransaction transaction, StoreData data)
    {
        InsertStrings(connection, transaction, "ContractTypes", data.ContractTypes);
        InsertStrings(connection, transaction, "SpendCategories", data.SpendCategories);
        InsertStrings(connection, transaction, "ContractDurations", data.ContractDurations);
        InsertStrings(connection, transaction, "ContractStatuses", data.ContractStatuses);
        SetMetadata(connection, transaction, "ComplianceVersion", data.ComplianceVersion.ToString(CultureInfo.InvariantCulture));
        foreach (var x in data.Contracts)
            Execute(connection, transaction, "INSERT INTO Contracts (Id, Name, Description, ContractType, SpendCategory, Duration, Status, SupplierId, SupplierContact, Cost, CostPeriod, CostNotes, RenewalType, StartDate, EndDate, NextRenewalDate, NoticeMonths, ContractOwner, ProcurementApproach, ApprovedApp, ProcessesPersonalData, RelatedParty, RelatedPartyReportedOn, Notes, ProjectNumber, ProjectItemId, CreatedAt) VALUES ($id,$name,$description,$type,$spend,$duration,$status,$supplier,$contact,$cost,$period,$costnotes,$renewal,$start,$end,$next,$notice,$owner,$procurement,$app,$personal,$related,$reported,$notes,$project,$item,$created);",
                ("$id", x.Id.ToString()), ("$name", x.Name), ("$description", x.Description ?? ""), ("$type", x.ContractType ?? ""),
                ("$spend", x.SpendCategory ?? ""), ("$duration", x.Duration ?? ""), ("$status", x.Status ?? ContractStatusDefaults.Active),
                ("$supplier", x.SupplierId?.ToString()), ("$contact", x.SupplierContact ?? ""),
                ("$cost", x.Cost?.ToString(CultureInfo.InvariantCulture)), ("$period", x.CostPeriod ?? ""), ("$costnotes", x.CostNotes ?? ""),
                ("$renewal", x.RenewalType ?? ""), ("$start", IsoDay(x.StartDate)), ("$end", IsoDay(x.EndDate)), ("$next", IsoDay(x.NextRenewalDate)),
                ("$notice", x.NoticeMonths), ("$owner", x.ContractOwner ?? ""), ("$procurement", x.ProcurementApproach ?? ""),
                ("$app", x.ApprovedApp ? 1 : 0), ("$personal", x.ProcessesPersonalData ? 1 : 0), ("$related", x.RelatedParty ? 1 : 0),
                ("$reported", IsoDay(x.RelatedPartyReportedOn)), ("$notes", x.Notes ?? ""), ("$project", x.ProjectNumber),
                ("$item", x.ProjectItemId?.ToString()), ("$created", Iso(x.CreatedAt)));
    }
}
