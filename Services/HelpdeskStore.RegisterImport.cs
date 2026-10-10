using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// What an import would do, row by row - shown before anything changes.
public sealed class RegisterImportPreview
{
    public List<string> MatchedColumns { get; } = [];
    public List<string> IgnoredColumns { get; } = [];
    public List<RegisterImportRow> Rows { get; } = [];
    public List<string> NewValues { get; } = [];
    public int Count(string action) => Rows.Count(x => x.Action == action);
}

public sealed class RegisterImportRow
{
    public int Number { get; init; }
    public string Key { get; init; } = "";
    public string Action { get; set; } = "";
    public string Detail { get; set; } = "";
    public List<string> Warnings { get; } = [];
    // Hidden rows and the DfE templates' worked examples are shown but left unticked.
    public string? HeldBack { get; set; }
    public bool CanApply => Action is RegisterImportActions.Add or RegisterImportActions.Update;
}

public static class RegisterImportActions
{
    public const string Add = "Add";
    public const string Update = "Update";
    public const string NoChange = "No change";
    public const string Error = "Error";
}

// Importing the contracts register (most usefully the DfE template, already filled in) and a staff list (an MIS export)
// - merged from EduInventory. Upload, preview exactly what would change, tick the rows to take, apply. Contracts are
// matched by name and people by email, then name. Blank cells never clear anything, and nothing is ever deleted.
public sealed partial class HelpdeskStore
{
    private sealed record Field<T>(string Header, string[] Aliases, Func<T, string, ImportLists, (T Record, string? Warning)> Apply);

    // List values and suppliers an import would add, collected while planning and only created for the rows applied.
    private sealed class ImportLists(HelpdeskStore store)
    {
        public readonly List<(string Kind, string Value)> NewValues = [];
        public readonly Dictionary<string, SupplierRecord> NewSuppliers = new(StringComparer.OrdinalIgnoreCase);
        public List<string> ContractTypes => store._data.ContractTypes;
        public List<string> SpendCategories => store._data.SpendCategories;
        public List<string> Durations => store._data.ContractDurations;
        public List<string> ContractStatuses => store._data.ContractStatuses;
        public List<string> PersonTypes => store._data.PersonTypes;
        public List<string> Departments => store._data.Departments;
        public List<string> Locations => store._data.Locations;
        public string? NewSupplierName(Guid id) => NewSuppliers.Values.FirstOrDefault(x => x.Id == id)?.Name;

        public string Value(string kind, List<string> list, string value)
        {
            var text = value.Trim();
            if (list.FirstOrDefault(x => string.Equals(x, text, StringComparison.OrdinalIgnoreCase)) is { } match) return match;
            if (NewValues.FirstOrDefault(x => x.Kind == kind && string.Equals(x.Value, text, StringComparison.OrdinalIgnoreCase)) is { Value: { } pending }) return pending;
            NewValues.Add((kind, text));
            return text;
        }

        public Guid Supplier(string name)
        {
            var text = name.Trim();
            if (store._data.Suppliers.FirstOrDefault(x => string.Equals(x.Name.Trim(), text, StringComparison.OrdinalIgnoreCase)) is { } known) return known.Id;
            if (!NewSuppliers.TryGetValue(text, out var created))
                NewSuppliers[text] = created = new SupplierRecord(Guid.NewGuid(), text, "", "", "", "", "", "", "", "", "", "", "", DateTime.UtcNow);
            return created.Id;
        }
    }

    private static (T, string?) Ok<T>(T record) => (record, null);
    private static (T, string?) DateField<T>(T record, string raw, string label, Func<DateOnly?, T> set) =>
        SpreadsheetReader.TryParseDate(raw, out var date) ? (set(date), null) : (record, $"{label}: \"{raw}\" is not a date, so it was left as it was.");
    private static (T, string?) YesNoField<T>(T record, string raw, string label, Func<bool, T> set) =>
        SpreadsheetReader.ParseYesNo(raw) is { } value ? (set(value), null) : (record, $"{label}: \"{raw}\" is not Yes or No.");

    // ---- The contracts register: the DfE template's headings first, then other spellings ----

    private static readonly Field<ContractRecord>[] ContractFields =
    [
        new("Name of contract or service", ["name of contract or service", "contract name", "name", "contract"], (x, v, _) => Ok(x with { Name = v })),
        new("Description of contract or service", ["description of contract or service", "description"], (x, v, _) => Ok(x with { Description = v })),
        new("Contract type", ["contract type", "type"], (x, v, l) => Ok(x with { ContractType = l.Value("Contract type", l.ContractTypes, v) })),
        new("Spend category", ["spend category", "category"], (x, v, l) => Ok(x with { SpendCategory = l.Value("Spend category", l.SpendCategories, v) })),
        new("Supplier contact", ["supplier contact", "contact"], (x, v, _) => Ok(x with { SupplierContact = v })),
        new("Supplier", ["supplier", "supplier name", "provider"], (x, v, l) => Ok(x with { SupplierId = l.Supplier(v) })),
        new("Cost", ["cost"], (x, v, _) =>
        {
            // The DfE column is free text. The amount and period are taken out of it, and the wording kept if there was more.
            var (amount, period) = SpreadsheetReader.ParseCost(v);
            if (amount is null) return (x with { CostNotes = v }, "Cost: no amount found, so the text was kept as a cost note.");
            var next = x with { Cost = amount, CostPeriod = period ?? x.CostPeriod };
            if (System.Text.RegularExpressions.Regex.Matches(v, @"\d[\d,.]*").Count > 1) next = next with { CostNotes = v };
            return (next, period is null ? $"Cost: \"{v}\" doesn't say how often, so it was taken as {next.CostPeriod.ToLowerInvariant()}." : null);
        }),
        new("Cost (£)", ["cost"], (x, v, _) => SpreadsheetReader.TryParseMoney(v, out var amount) ? Ok(x with { Cost = amount }) : (x, $"Cost (£): \"{v}\" is not an amount.")),
        new("Cost period", ["cost period"], (x, v, _) => ContractCostPeriods.All.FirstOrDefault(p => string.Equals(p, v, StringComparison.OrdinalIgnoreCase)) is { } period
            ? Ok(x with { CostPeriod = period }) : (x, $"Cost period: \"{v}\" is not one of {string.Join(", ", ContractCostPeriods.All)}.")),
        new("Cost notes", ["cost notes"], (x, v, _) => Ok(x with { CostNotes = v })),
        new("Duration", ["duration", "term"], (x, v, l) => Ok(x with { Duration = l.Value("Contract duration", l.Durations, v) })),
        new("Renewal frequency", ["renewal frequency", "renewal", "renews"], (x, v, _) =>
        {
            var type = v.Contains("auto", StringComparison.OrdinalIgnoreCase) ? ContractRenewalTypes.Automatic
                : v.Contains("manual", StringComparison.OrdinalIgnoreCase) ? ContractRenewalTypes.Manual
                : v.Contains("not", StringComparison.OrdinalIgnoreCase) || v.Contains("none", StringComparison.OrdinalIgnoreCase) ? ContractRenewalTypes.None : null;
            return type is null ? (x, $"Renewal frequency: \"{v}\" is neither automatic nor manual.") : Ok(x with { RenewalType = type });
        }),
        new("Contract start date", ["contract start date", "start date", "start"], (x, v, _) => DateField(x, v, "Start date", d => x with { StartDate = d })),
        new("Contract end date", ["contract end date", "end date", "end", "expiry date"], (x, v, _) => DateField(x, v, "End date", d => x with { EndDate = d })),
        new("Next renewal date", ["next renewal date", "renewal date"], (x, v, _) => DateField(x, v, "Next renewal date", d => x with { NextRenewalDate = d })),
        new("Notice required (in months) for renewal or cancellation", ["notice required in months for renewal or cancellation", "notice required", "notice months", "notice period"], (x, v, _) =>
            int.TryParse(new string(v.Trim().TakeWhile(char.IsDigit).ToArray()), out var months) && months <= 60
                ? Ok(x with { NoticeMonths = months == 0 ? null : months }) : (x, $"Notice required: \"{v}\" is not a number of months.")),
        new("Contract owner (within the school or college)", ["contract owner within the school or college", "contract owner", "owner"], (x, v, _) => Ok(x with { ContractOwner = v })),
        new("Procurement approach used", ["procurement approach used", "procurement approach", "procurement"], (x, v, _) => Ok(x with { ProcurementApproach = v })),
        new("Contract status", ["contract status", "status"], (x, v, l) => Ok(x with { Status = l.Value("Contract status", l.ContractStatuses, v) })),
        new("Additional comments", ["additional comments", "comments", "notes"], (x, v, _) => Ok(x with { Notes = v })),
        new("Approved app", ["approved app"], (x, v, _) => YesNoField(x, v, "Approved app", b => x with { ApprovedApp = b })),
        new("Processes personal data", ["processes personal data", "personal data"], (x, v, _) => YesNoField(x, v, "Processes personal data", b => x with { ProcessesPersonalData = b })),
        new("Related party transaction", ["related party transaction", "related party"], (x, v, _) => YesNoField(x, v, "Related party", b => x with { RelatedParty = b })),
        new("Reported to DfE on", ["reported to dfe on"], (x, v, _) => DateField(x, v, "Reported to DfE on", d => x with { RelatedPartyReportedOn = d }))
    ];

    // The DfE contracts template's fictional examples, recognised by their very specific descriptions and held back.
    private static readonly string[] ExampleContractDescriptions =
    [
        "1GB Bearer, Internet service, Firewall and Filtering, Intrusion detection, in-line antivirus and anti-malware",
        "VOIP Telephony services (Cloud hosted)", "Multi-Functional Printers (MFD)", "Office Productivity software licence (per FTE)",
        "SSL Certificate", "Online Maths service", "Online SEND Assessment recording", "Operational lease for 30 Tablets"
    ];

    // ---- A staff list ----

    private static readonly Field<UserRecord>[] StaffFields =
    [
        new("Name", ["name", "full name", "person", "staff name", "display name"], (x, v, _) => Ok(x with { Name = v })),
        new("Email", ["email", "email address", "e-mail", "work email", "school email"], (x, v, _) => Ok(x with { Email = v })),
        new("Type", ["type", "person type", "staff type"], (x, v, l) => Ok(x with { PersonType = l.Value("Person type", l.PersonTypes, v) })),
        new("Department", ["department", "dept", "faculty"], (x, v, l) => Ok(x with { Department = l.Value("Department", l.Departments, v) })),
        new("Location", ["location", "room", "base"], (x, v, l) => Ok(x with { Location = l.Value("Location", l.Locations, v) })),
        new("Start date", ["start date", "started", "date started", "employment start date"], (x, v, _) => DateField(x, v, "Start date", d => x with { StartDate = d })),
        // A leave date marks them as having left - inactive, from that day - which is what the leaver check then works from.
        new("Leave date", ["leave date", "left", "leaving date", "end date", "date left", "employment end date"], (x, v, _) =>
            SpreadsheetReader.TryParseDate(v, out var left)
                ? Ok(left is { } day ? x with { IsActive = false, LeftAt = day.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Local).ToUniversalTime() } : x)
                : (x, $"Leave date: \"{v}\" is not a date, so it was left as it was."))
    ];

    public const string ContractImport = "contracts";
    public const string StaffImport = "staff";

    public static IEnumerable<string> ImportHeaders(string kind) => kind == ContractImport
        ? ContractFields.SelectMany(f => f.Aliases.Append(f.Header))
        : StaffFields.SelectMany(f => f.Aliases.Append(f.Header));

    public static string[] ImportSheetNames(string kind) => kind == ContractImport ? ["Contracts"] : ["Staff", "People"];

    public RegisterImportPreview PreviewRegisterImport(string kind, SheetTable table)
    {
        lock (_sync) return PlanRegisterImport(kind, table, out _, out _);
    }

    public (bool Ok, string Message) ApplyRegisterImport(string kind, SheetTable table, IReadOnlyCollection<int> rowNumbers)
    {
        lock (_sync)
        {
            PlanRegisterImport(kind, table, out var planned, out var lists);
            var chosen = planned.Where(x => rowNumbers.Contains(x.Row.Number) && x.Row.CanApply).ToList();
            if (chosen.Count == 0) return (false, "No rows were ticked that could be applied.");

            // Only the list values and suppliers the chosen rows use are added.
            foreach (var (listKind, value) in lists.NewValues)
                if (chosen.Any(c => Uses(c.Record, listKind, value))) ListFor(listKind).Add(value);
            foreach (var supplier in lists.NewSuppliers.Values)
                if (chosen.Any(c => c.Record is ContractRecord r && r.SupplierId == supplier.Id)) _data.Suppliers.Add(supplier);

            foreach (var (row, record) in chosen)
                switch (record)
                {
                    case ContractRecord contract:
                        var index = _data.Contracts.FindIndex(x => x.Id == contract.Id);
                        if (index >= 0) _data.Contracts[index] = contract; else _data.Contracts.Add(contract);
                        break;
                    case UserRecord person:
                        var userIndex = _data.Users.FindIndex(x => x.Id == person.Id);
                        if (userIndex >= 0) _data.Users[userIndex] = WithLeaverDates(person, _data.Users[userIndex]);
                        else _data.Users.Add(WithLeaverDates(person, null));
                        break;
                }
            var adds = chosen.Count(c => c.Row.Action == RegisterImportActions.Add);
            _pendingAudit.Add(new AuditEntry(DateTime.UtcNow, kind == ContractImport ? "Contracts" : "Users", null, null,
                kind == ContractImport ? "Contracts register import" : "Staff list import", "Imported", $"{adds} added, {chosen.Count - adds} updated."));
            Save();
            return (true, $"Import applied: {adds} added, {chosen.Count - adds} updated.");
        }
    }

    private List<string> ListFor(string kind) => kind switch
    {
        "Contract type" => _data.ContractTypes,
        "Spend category" => _data.SpendCategories,
        "Contract duration" => _data.ContractDurations,
        "Contract status" => _data.ContractStatuses,
        "Person type" => _data.PersonTypes,
        "Department" => _data.Departments,
        _ => _data.Locations
    };

    private static bool Uses(object record, string kind, string value)
    {
        bool Is(string? x) => string.Equals(x, value, StringComparison.OrdinalIgnoreCase);
        return (record, kind) switch
        {
            (ContractRecord c, "Contract type") => Is(c.ContractType),
            (ContractRecord c, "Spend category") => Is(c.SpendCategory),
            (ContractRecord c, "Contract duration") => Is(c.Duration),
            (ContractRecord c, "Contract status") => Is(c.Status),
            (UserRecord u, "Person type") => Is(u.PersonType),
            (UserRecord u, "Department") => Is(u.Department),
            (UserRecord u, "Location") => Is(u.Location),
            _ => false
        };
    }

    // Caller holds the lock.
    private RegisterImportPreview PlanRegisterImport(string kind, SheetTable table, out List<(RegisterImportRow Row, object Record)> planned, out ImportLists lists)
    {
        lists = new ImportLists(this);
        var now = DateTime.UtcNow;
        if (kind == ContractImport)
            return Plan(table, ContractFields, lists, out planned,
                create: () => new ContractRecord(Guid.NewGuid(), "", now) { Status = _data.ContractStatuses.FirstOrDefault() ?? ContractStatusDefaults.Active },
                keyOf: c => c.Name,
                find: c => _data.Contracts.FirstOrDefault(x => string.Equals(x.Name, c.Name, StringComparison.OrdinalIgnoreCase)),
                validate: c => c.Name.Trim().Length == 0 ? "No contract name." : c.StartDate is { } s && c.EndDate is { } e && e < s ? "The end date is before the start date." : null,
                describe: DescribeContract,
                isExample: c => ExampleContractDescriptions.Contains(c.Description.Trim(), StringComparer.OrdinalIgnoreCase));
        return Plan(table, StaffFields, lists, out planned,
            create: () => new UserRecord(Guid.NewGuid(), "", "", "", ""),
            keyOf: p => p.Email.Trim().Length > 0 ? p.Email.Trim() : p.Name.Trim(),
            find: p => (p.Email.Trim().Length > 0 ? _data.Users.FirstOrDefault(x => string.Equals(x.Email, p.Email.Trim(), StringComparison.OrdinalIgnoreCase)) : null)
                       ?? _data.Users.FirstOrDefault(x => x.AnonymisedAt is null && string.Equals(x.Name, p.Name.Trim(), StringComparison.OrdinalIgnoreCase)),
            validate: p => p.Name.Trim().Length == 0 ? "No name."
                : p.Email.Trim().Length == 0 ? "No email address - people in the helpdesk need one."
                : !p.Email.Contains('@') || p.Email.Contains(' ') ? $"\"{p.Email}\" doesn't look like an email address." : null,
            describe: DescribePerson,
            isExample: _ => false);
    }

    private RegisterImportPreview Plan<T>(SheetTable table, Field<T>[] fields, ImportLists lists, out List<(RegisterImportRow Row, object Record)> planned,
        Func<T> create, Func<T, string> keyOf, Func<T, T?> find, Func<T, string?> validate, Func<T?, T, ImportLists, string> describe, Func<T, bool> isExample) where T : class
    {
        var preview = new RegisterImportPreview();
        planned = [];
        // Each column goes to the first field whose heading or alias it matches, the exact DfE heading first; a field
        // takes one column only.
        var columnField = new Dictionary<int, Field<T>>();
        var taken = new HashSet<Field<T>>();
        for (var pass = 0; pass < 2; pass++)
            for (var i = 0; i < table.Headers.Count; i++)
            {
                if (columnField.ContainsKey(i)) continue;
                var header = SpreadsheetReader.NormaliseHeader(table.Headers[i]);
                if (header.Length == 0) continue;
                var field = fields.FirstOrDefault(f => !taken.Contains(f) && (pass == 0
                    ? SpreadsheetReader.NormaliseHeader(f.Header) == header
                    : f.Aliases.Any(a => SpreadsheetReader.NormaliseHeader(a) == header)));
                if (field is null) continue;
                columnField[i] = field;
                taken.Add(field);
            }
        for (var i = 0; i < table.Headers.Count; i++)
            if (table.Headers[i].Trim().Length > 0) (columnField.ContainsKey(i) ? preview.MatchedColumns : preview.IgnoredColumns).Add(table.Headers[i].Trim());

        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheetRow in table.Rows)
        {
            // Read once onto a blank record to learn the key, then onto the existing record so blank cells leave its
            // values alone.
            var probe = create();
            foreach (var (index, field) in columnField)
                if (sheetRow.Cell(index) is { Length: > 0 } v) probe = field.Apply(probe, v, lists).Record;
            var key = keyOf(probe).Trim();
            var row = new RegisterImportRow { Number = sheetRow.Number, Key = key.Length > 0 ? key : "(none)" };
            preview.Rows.Add(row);
            if (key.Length > 0 && seen.TryGetValue(key, out var firstRow))
            {
                row.Action = RegisterImportActions.Error;
                row.Detail = $"Repeats row {firstRow}; only the first is used.";
                continue;
            }
            if (key.Length > 0) seen[key] = sheetRow.Number;

            var existing = find(probe);
            var record = existing ?? create();
            foreach (var (index, field) in columnField)
            {
                if (sheetRow.Cell(index) is not { Length: > 0 } value) continue;
                var (next, warning) = field.Apply(record, value, lists);
                record = next;
                if (warning is not null) row.Warnings.Add(warning);
            }
            if (validate(record) is { } error)
            {
                row.Action = RegisterImportActions.Error;
                row.Detail = error;
                continue;
            }
            row.Detail = describe(existing, record, lists);
            row.Action = existing is null ? RegisterImportActions.Add : row.Detail.Length == 0 ? RegisterImportActions.NoChange : RegisterImportActions.Update;
            if (sheetRow.Hidden) row.HeldBack = "Hidden in the spreadsheet";
            else if (isExample(record)) row.HeldBack = "One of the DfE template's examples";
            planned.Add((row, record));
        }
        preview.NewValues.AddRange(lists.NewValues.Select(x => $"{x.Kind}: {x.Value}"));
        preview.NewValues.AddRange(lists.NewSuppliers.Keys.Select(x => $"Supplier: {x}"));
        return preview;
    }

    // "Field: before → after" for each change, or every filled field for a new record.
    private string DescribeContract(ContractRecord? before, ContractRecord after, ImportLists lists)
    {
        string Supplier(Guid? id) => id is { } x ? _data.Suppliers.FirstOrDefault(s => s.Id == x)?.Name ?? $"{lists.NewSupplierName(x)} (new supplier)" : "";
        static string D(DateOnly? d) => d?.ToString("d MMM yyyy") ?? "";
        static string M(decimal? m) => m?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? "";
        (string, string, string)[] fields =
        [
            ("Name", before?.Name ?? "", after.Name), ("Description", before?.Description ?? "", after.Description),
            ("Type", before?.ContractType ?? "", after.ContractType), ("Spend category", before?.SpendCategory ?? "", after.SpendCategory),
            ("Supplier", Supplier(before?.SupplierId), Supplier(after.SupplierId)),
            ("Supplier contact", before?.SupplierContact ?? "", after.SupplierContact), ("Cost", M(before?.Cost), M(after.Cost)),
            ("Cost period", before is null || before.Cost is null ? "" : before.CostPeriod, after.Cost is null ? "" : after.CostPeriod),
            ("Cost notes", before?.CostNotes ?? "", after.CostNotes), ("Duration", before?.Duration ?? "", after.Duration),
            ("Renewal", before?.RenewalType ?? "", after.RenewalType), ("Start", D(before?.StartDate), D(after.StartDate)),
            ("End", D(before?.EndDate), D(after.EndDate)), ("Next renewal", D(before?.NextRenewalDate), D(after.NextRenewalDate)),
            ("Notice (months)", before?.NoticeMonths?.ToString() ?? "", after.NoticeMonths?.ToString() ?? ""),
            ("Owner", before?.ContractOwner ?? "", after.ContractOwner), ("Procurement", before?.ProcurementApproach ?? "", after.ProcurementApproach),
            ("Status", before?.Status ?? "", after.Status), ("Comments", before?.Notes ?? "", after.Notes),
            ("Approved app", before is null ? "" : Yn(before.ApprovedApp), before is null && !after.ApprovedApp ? "" : Yn(after.ApprovedApp)),
            ("Personal data", before is null ? "" : Yn(before.ProcessesPersonalData), before is null && !after.ProcessesPersonalData ? "" : Yn(after.ProcessesPersonalData)),
            ("Related party", before is null ? "" : Yn(before.RelatedParty), before is null && !after.RelatedParty ? "" : Yn(after.RelatedParty)),
            ("Reported to DfE", D(before?.RelatedPartyReportedOn), D(after.RelatedPartyReportedOn))
        ];
        return Changes(before is null, fields);
    }

    private static string DescribePerson(UserRecord? before, UserRecord after, ImportLists lists)
    {
        static string D(DateOnly? d) => d?.ToString("d MMM yyyy") ?? "";
        static string Left(UserRecord? u) => u is { IsActive: false, LeftAt: { } left } ? left.ToLocalTime().ToString("d MMM yyyy") : "";
        (string, string, string)[] fields =
        [
            ("Name", before?.Name ?? "", after.Name), ("Email", before?.Email ?? "", after.Email), ("Type", before?.PersonType ?? "", after.PersonType),
            ("Department", before?.Department ?? "", after.Department), ("Location", before?.Location ?? "", after.Location),
            ("Start date", D(before?.StartDate), D(after.StartDate)), ("Left", Left(before), Left(after))
        ];
        return Changes(before is null, fields);
    }

    private static string Yn(bool value) => value ? "Yes" : "No";

    private static string Changes(bool isNew, IEnumerable<(string Label, string Before, string After)> fields) => isNew
        ? string.Join("; ", fields.Where(x => x.After.Length > 0).Select(x => $"{x.Label}: {x.After}"))
        : string.Join("; ", fields.Where(x => !string.Equals(x.Before, x.After, StringComparison.Ordinal)).Select(x => $"{x.Label}: {(x.Before.Length == 0 ? "(blank)" : x.Before)} → {(x.After.Length == 0 ? "(blank)" : x.After)}"));
}
