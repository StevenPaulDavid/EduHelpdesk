using EduHelpdesk.Models;
using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Services;

// What a new install starts with (starter options and the worked example), removing the demo records, and the factory reset.
public sealed partial class HelpdeskStore
{
    // The demo records that are still there, as (kind, name) for the "Go live" panel to list. Ids that no longer
    // resolve are skipped: the admin deleted that one by hand, which is fine.
    public IReadOnlyList<(string Kind, string Name)> DemoDataSummary()
    {
        lock (_sync) return _data.DemoRecords.Select(DescribeDemoRecord).OfType<(string, string)>().ToList();
    }
    public bool HasDemoData { get { lock (_sync) return _data.DemoRecords.Any(x => DescribeDemoRecord(x) is not null); } }

    private (string Kind, string Name)? DescribeDemoRecord(DemoRecord record)
    {
        switch (record.EntityType)
        {
            case "Ticket" when int.TryParse(record.EntityKey, out var number):
                return _data.Tickets.FirstOrDefault(x => x.Number == number) is { } ticket ? ("Ticket", $"#{ticket.Number} {ticket.Title}") : null;
            case "Project" when int.TryParse(record.EntityKey, out var projectNumber):
                return _data.Projects.FirstOrDefault(x => x.Number == projectNumber) is { } project ? ("Project", $"{project.Reference} {project.Title}") : null;
            case "Asset" when Guid.TryParse(record.EntityKey, out var assetId):
                return _data.Assets.FirstOrDefault(x => x.Id == assetId) is { } asset ? ("Asset", asset.AssetTag) : null;
            case "User" when Guid.TryParse(record.EntityKey, out var userId):
                return _data.Users.FirstOrDefault(x => x.Id == userId) is { } user ? ("Requester", user.Name) : null;
            case "Technician" when Guid.TryParse(record.EntityKey, out var technicianId):
                return _data.Technicians.FirstOrDefault(x => x.Id == technicianId) is { } tech ? ("Technician", tech.Name) : null;
            case "Supplier" when Guid.TryParse(record.EntityKey, out var supplierId):
                return _data.Suppliers.FirstOrDefault(x => x.Id == supplierId) is { } supplier ? ("Supplier", supplier.Name) : null;
            case "Part" when Guid.TryParse(record.EntityKey, out var partId):
                return _data.Parts.FirstOrDefault(x => x.Id == partId) is { } part ? ("Part", part.Name) : null;
            case "Sla" when Guid.TryParse(record.EntityKey, out var slaId):
                return _data.Slas.FirstOrDefault(x => x.Id == slaId) is { } sla ? ("SLA", sla.Name) : null;
            case "TicketTemplate" when Guid.TryParse(record.EntityKey, out var templateId):
                return _data.TicketTemplates.FirstOrDefault(x => x.Id == templateId) is { } template ? ("Ticket template", template.Name) : null;
            case "LoanKit" when Guid.TryParse(record.EntityKey, out var kitId):
                return _data.LoanKits.FirstOrDefault(x => x.Id == kitId) is { } kit ? ("Loan kit", kit.Name) : null;
            case "AssetAttribute" when Guid.TryParse(record.EntityKey, out var assetAttributeId):
                return _data.AssetAttributeDefinitions.FirstOrDefault(x => x.Id == assetAttributeId) is { } definition ? ("Asset attribute", definition.Name) : null;
            case "TicketAttribute" when Guid.TryParse(record.EntityKey, out var ticketAttributeId):
                return _data.TicketAttributeDefinitions.FirstOrDefault(x => x.Id == ticketAttributeId) is { } ticketDefinition ? ("Ticket attribute", ticketDefinition.Name) : null;
            default:
                return null;
        }
    }

    // "Go live": removes the records SeedDemoData created and nothing else. Option-list values it added (Teaching,
    // Dell, Consumables and so on) are deliberately kept - they are ordinary values a school may already be using,
    // and each can be deleted from its own Settings page if it isn't wanted.
    // Real records that point at demo ones are unpicked rather than deleted: a ticket loses its demo technician, SLA
    // and asset links instead of disappearing. The one thing that cannot be unpicked is a ticket's requester, so that
    // case refuses the whole operation rather than deleting someone's ticket or leaving a row that breaks the save.
    public (bool Ok, string Message) RemoveDemoData()
    {
        lock (_sync)
        {
            if (!_data.DemoRecords.Any(x => DescribeDemoRecord(x) is not null))
            {
                if (_data.DemoRecords.Count > 0) { _data.DemoRecords.Clear(); Save(); }
                return (false, "There is no demo data left to remove.");
            }

            HashSet<Guid> Ids(string type) => _data.DemoRecords.Where(x => x.EntityType == type)
                .Select(x => Guid.TryParse(x.EntityKey, out var id) ? id : Guid.Empty).Where(x => x != Guid.Empty).ToHashSet();
            var ticketNumbers = _data.DemoRecords.Where(x => x.EntityType == "Ticket")
                .Select(x => int.TryParse(x.EntityKey, out var n) ? n : 0).Where(x => x != 0).ToHashSet();
            var assetIds = Ids("Asset");
            var userIds = Ids("User");
            var technicianIds = Ids("Technician");
            var supplierIds = Ids("Supplier");
            var partIds = Ids("Part");
            var slaIds = Ids("Sla");
            var templateIds = Ids("TicketTemplate");
            var kitIds = Ids("LoanKit");
            var assetAttributeIds = Ids("AssetAttribute");
            var ticketAttributeIds = Ids("TicketAttribute");

            var projectNumbers = _data.DemoRecords.Where(x => x.EntityType == "Project")
                .Select(x => int.TryParse(x.EntityKey, out var n) ? n : 0).Where(x => x != 0).ToHashSet();
            // A real project raised by a demo requester can't lose its requester any more than a ticket can.
            if (_data.Projects.Where(x => userIds.Contains(x.RequesterId) && !projectNumbers.Contains(x.Number)).ToList() is { Count: > 0 } blockingProjects)
                return (false, $"Nothing was changed. {string.Join(", ", blockingProjects.Take(5).Select(x => x.Reference))}{(blockingProjects.Count > 5 ? " and others" : "")} "
                    + "still list a demo requester. Delete those projects, and try again.");
            var blocking = _data.Tickets.Where(x => userIds.Contains(x.RequesterId) && !ticketNumbers.Contains(x.Number)).ToList();
            if (blocking.Count > 0)
            {
                var names = string.Join(", ", blocking.Take(5).Select(x => $"#{x.Number}"));
                var one = blocking.Count == 1;
                return (false, $"Nothing was changed. {(one ? "Ticket" : "Tickets")} {names}{(blocking.Count > 5 ? " and others" : "")} "
                    + $"still {(one ? "lists" : "list")} a demo requester. Change the requester on {(one ? "that ticket" : "those tickets")}, or delete "
                    + $"{(one ? "it" : "them")}, and try again.");
            }

            var removed = _data.DemoRecords.Select(DescribeDemoRecord).OfType<(string Kind, string Name)>().Count();

            // Demo tickets and everything hanging off them.
            foreach (var number in ticketNumbers) RemoveTicketExtras(number);
            _data.TicketParts.RemoveAll(x => ticketNumbers.Contains(x.TicketNumber) || partIds.Contains(x.PartId));
            _data.TicketAttributeValues.RemoveAll(x => ticketNumbers.Contains(x.TicketNumber) || ticketAttributeIds.Contains(x.AttributeDefinitionId));
            _data.Tickets.RemoveAll(x => ticketNumbers.Contains(x.Number));
            // Real tickets keep everything they can: only the pointers at demo records go.
            for (var i = 0; i < _data.Tickets.Count; i++)
            {
                var ticket = _data.Tickets[i];
                if (ticket.TechnicianId is { } assigned && technicianIds.Contains(assigned)) ticket = ticket with { TechnicianId = null };
                if (ticket.SlaId is { } sla && slaIds.Contains(sla)) ticket = ticket with { SlaId = null };
                if (ticket.AssetIds.Any(assetIds.Contains)) ticket = ticket with { AssetIds = ticket.AssetIds.Where(x => !assetIds.Contains(x)).ToList() };
                _data.Tickets[i] = ticket;
            }

            // A real onboarding loses a demo line manager or a demo technician's name on its tasks, nothing more.
            for (var i = 0; i < _data.Onboardings.Count; i++)
            {
                var onboarding = _data.Onboardings[i];
                if (onboarding.LineManagerId is { } manager && userIds.Contains(manager)) onboarding = onboarding with { LineManagerId = null };
                if (onboarding.Tasks.Any(x => x.TechnicianId is { } tech && technicianIds.Contains(tech)))
                    onboarding = onboarding with { Tasks = onboarding.Tasks.Select(x => x.TechnicianId is { } tech && technicianIds.Contains(tech) ? x with { TechnicianId = null } : x).ToList() };
                _data.Onboardings[i] = onboarding;
            }

            _data.Projects.RemoveAll(x => projectNumbers.Contains(x.Number));
            for (var i = 0; i < _data.Projects.Count; i++)
            {
                var project = _data.Projects[i];
                if (project.TechnicianId is { } projectTech && technicianIds.Contains(projectTech)) project = project with { TechnicianId = null };
                // A real project that asked the demo supplier for a quote just loses that supplier from the list.
                if (project.Items.Any(x => x.Suppliers.Any(s => supplierIds.Contains(s.SupplierId))))
                {
                    foreach (var id in QuoteFiles(project.Items.SelectMany(x => x.Suppliers).Where(s => supplierIds.Contains(s.SupplierId)))) TryDelete(AttachmentFile(id));
                    project = project with
                    {
                        Items = project.Items.Select(x => x with
                        {
                            Suppliers = x.Suppliers.Where(s => !supplierIds.Contains(s.SupplierId)).ToList(),
                            ChosenSupplierId = x.ChosenSupplierId is { } chosen && supplierIds.Contains(chosen) ? null : x.ChosenSupplierId
                        }).ToList()
                    };
                }
                _data.Projects[i] = project;
            }

            _data.AssetAttributeValues.RemoveAll(x => assetIds.Contains(x.AssetId) || assetAttributeIds.Contains(x.AttributeDefinitionId));
            _data.Assets.RemoveAll(x => assetIds.Contains(x.Id));
            for (var i = 0; i < _data.Assets.Count; i++)
            {
                var asset = _data.Assets[i];
                if (asset.AssignedUserId is { } holder && userIds.Contains(holder)) asset = asset with { AssignedUserId = null, LoanDueDate = null };
                if (asset.SupplierId is { } supplier && supplierIds.Contains(supplier)) asset = asset with { SupplierId = null };
                _data.Assets[i] = asset;
            }

            _data.KitLoans.RemoveAll(x => kitIds.Contains(x.KitId));
            _data.LoanKits.RemoveAll(x => kitIds.Contains(x.Id));
            for (var i = 0; i < _data.LoanKits.Count; i++)
                _data.LoanKits[i] = _data.LoanKits[i] with { AssetIds = _data.LoanKits[i].AssetIds.Where(x => !assetIds.Contains(x)).ToList() };
            // The borrower's name stays on the loan - that is exactly why it is stored alongside the id.
            for (var i = 0; i < _data.KitLoans.Count; i++)
                if (_data.KitLoans[i].BorrowerUserId is { } borrower && userIds.Contains(borrower))
                    _data.KitLoans[i] = _data.KitLoans[i] with { BorrowerUserId = null };

            _data.Parts.RemoveAll(x => partIds.Contains(x.Id));
            for (var i = 0; i < _data.Parts.Count; i++)
                _data.Parts[i] = _data.Parts[i] with { SupplierIds = _data.Parts[i].SupplierIds.Where(x => !supplierIds.Contains(x)).ToList() };

            _data.AssetAttributeDefinitions.RemoveAll(x => assetAttributeIds.Contains(x.Id));
            _data.TicketAttributeDefinitions.RemoveAll(x => ticketAttributeIds.Contains(x.Id));

            _data.TicketTemplates.RemoveAll(x => templateIds.Contains(x.Id));
            for (var i = 0; i < _data.TicketTemplates.Count; i++)
            {
                var template = _data.TicketTemplates[i];
                if (template.SlaId is { } sla && slaIds.Contains(sla)) template = template with { SlaId = null };
                foreach (var key in template.AttributeValues.Keys.Where(ticketAttributeIds.Contains).ToList()) template.AttributeValues.Remove(key);
                _data.TicketTemplates[i] = template;
            }
            _data.Slas.RemoveAll(x => slaIds.Contains(x.Id));

            _data.Suppliers.RemoveAll(x => supplierIds.Contains(x.Id));
            _data.Users.RemoveAll(x => userIds.Contains(x.Id));
            _data.Technicians.RemoveAll(x => technicianIds.Contains(x.Id));

            _data.DemoRecords.Clear();
            _pendingAudit.Add(new AuditEntry(DateTime.UtcNow, "System", null, null, "Helpdesk", "Demo data removed",
                $"The system went live: {removed} demo {(removed == 1 ? "record was" : "records were")} removed. Option list values were kept."));
            Save();
            return (true, $"Demo data removed - {removed} {(removed == 1 ? "record is" : "records are")} gone and the system is live. "
                + "The option lists it added (departments, makes, part categories) were kept; delete any you don't want from their own settings pages.");
        }
    }

    // Removes everything and puts the system back to how a new install starts (the same demo records Seed creates).
    // The audit log survives unless eraseAudit is set; either way the reset itself is recorded as the first entry.
    public (bool Ok, string Message) ResetFactory(string? confirmation, bool keepBackup = true, bool eraseAudit = false)
    {
        lock (_sync)
        {
            if (!string.Equals(confirmation?.Trim(), "DELETE", StringComparison.Ordinal))
                return (false, "Nothing was changed. Type DELETE in capitals to reset the system.");

            string? backupName = null;
            if (keepBackup)
            {
                try { backupName = WriteBackup("before-reset").Name; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException or InvalidOperationException)
                {
                    return (false, $"Nothing was changed. The backup could not be saved ({ex.Message}). Fix that, or untick the backup option to reset without one.");
                }
            }

            if (eraseAudit)
            {
                using var connection = new SqliteConnection($"Data Source={_path}");
                connection.Open();
                using var transaction = connection.BeginTransaction();
                using var statements = new StatementScope(transaction);
                Execute(connection, transaction, "DELETE FROM AuditLog;");
                transaction.Commit();
                _audit.Clear();
            }
            // The wipe is not audited record by record.
            _pendingAudit.Clear();
            _pendingAudit.Add(new AuditEntry(DateTime.UtcNow, "System", null, null, "Helpdesk", "Factory reset",
                "All data was reset to factory settings." + (eraseAudit ? " The previous audit log was erased." : "") + (backupName is null ? " No backup was kept." : $" A backup was saved as {backupName}.")));
            var previous = _data;
            _data = new StoreData();
            CarryBackupSettings(previous, _data);
            SeedStarterData();
            // Without these two the reset leaves no Administrator role and no account that can sign in, which locked
            // the system until the app was restarted. They only ran at startup before.
            EnsureSeedRoles();
            EnsureOnboardingRole();
            EnsureBootstrapAdministrator();
            // Deleted rows otherwise linger in the file's free pages, which defeats the point of a purge.
            using (var connection = new SqliteConnection($"Data Source={_path}"))
            {
                connection.Open();
                using var vacuum = connection.CreateCommand();
                vacuum.CommandText = "VACUUM;";
                vacuum.ExecuteNonQuery();
            }
            if (File.Exists(_templatePath))
                File.Delete(_templatePath);
            if (File.Exists(LogoPath))
                File.Delete(LogoPath);
            if (File.Exists(_legacyPath))
                File.Delete(_legacyPath);
            DeleteAllAttachmentFiles();
            // Every account was replaced, so nobody's sign-in carries on.
        EndAllSessions();
        return (true, $"System reset to factory settings. Sign in again as {BootstrapAdminEmail} with the password {BootstrapAdminPassword}, and change it straight away."
                + (backupName is null ? "" : $" A backup of the old data was saved as {backupName} in {BackupFolder}."));
        }
    }

    // What a brand-new system and a factory reset both start from: the option lists a working system needs, one team so
    // technicians have something to belong to, and a worked demo example (see SeedDemoData).
    // The lists here are seeded rather than put in EnsureFactoryOptions because that runs on every startup: a school
    // that deletes an asset type it does not own should not find it back the next morning.
    private void SeedStarterData()
    {
        // A brand-new set of data is already in the per-module model, so it has to say so. A factory reset replaces
        // _data with a fresh StoreData, whose version starts at 0, and MigrateRolePermissions only runs at startup -
        // so without this the database claimed the old model while holding new-model roles, and the next restart
        // "converted" them from legacy grants that are no longer written, throwing away whatever had been set up.
        _data.PermissionModelVersion = PermissionModelVersion;
        EnsureFactoryOptions();
        EnsureProjectDefaults();
        EnsureOnboardingDefaults();
        EnsureOptions(_data.TechnicianTeams, ["IT Support"]);
        EnsureOptions(_data.AssetTypes, ["Laptop", "Desktop", "Tablet", "Monitor", "Printer", "Projector",
            "Interactive display", "Phone", "Server", "Networking", "Peripheral", "Other"]);
        // A typical secondary-school day as a starting point for period SLAs. Every school's differs, so it is meant to
        // be edited, and it is only seeded here (not on every startup) so a school that clears it doesn't get it back.
        if (_data.Periods.Count == 0)
            _data.Periods =
            [
                new("Period 1", new(8, 50), new(9, 50)), new("Period 2", new(9, 50), new(10, 50)),
                new("Period 3", new(11, 10), new(12, 10)), new("Period 4", new(12, 10), new(13, 10)),
                new("Period 5", new(13, 55), new(14, 55)),
            ];
        SeedDemoData();
        SaveBaseline();
    }

    // Marks every seeded record so a school can find the lot by searching "demo" and delete it once they are ready to
    // start for real. Option-list values (departments, makes, part categories) are deliberately NOT marked: those are
    // meant to be kept and edited, while these records are meant to be thrown away.
    private const string DemoSuffix = " (demo)";

    // One worked example of everything, so a new system can be shown to someone rather than described. Records point at
    // each other on purpose - the ticket has the asset, the asset has the requester and supplier, the part is on the
    // ticket - because a pile of disconnected rows demonstrates nothing.
    // Not seeded: attachments (they need a real file on disk) and ticket links (they need a second ticket).
    private void SeedDemoData()
    {
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        void Remember(string type, string key) => _data.DemoRecords.Add(new DemoRecord(type, key));

        // Option-list values the demo records need. Realistic and worth keeping, unlike the records themselves.
        EnsureOptions(_data.Departments, ["Teaching"]);
        EnsureOptions(_data.Locations, ["Main Building"]);
        EnsureOptions(_data.AssetMakes, ["Dell"]);
        EnsureOptions(_data.AssetModels, ["Latitude 5440"]);
        _data.AssetModelMakes["Latitude 5440"] = "Dell";
        _data.AssetTypeLifespans["Laptop"] = 4;
        EnsureOptions(_data.PartCategories, ["Consumables"]);
        EnsureOptions(_data.PartLocations, ["IT Store"]);
        _data.StatusDescriptions["On Hold"] = "Waiting on the requester, a part, or a third party.";

        var supplier = new SupplierRecord(Guid.NewGuid(), "Demo Supplies Ltd", "Pat Brennan", "sales@demo-supplies.test",
            "01234 567890", "Unit 4, Trade Park", "", "Exampleton", "", "EX1 2AB", "United Kingdom",
            "https://demo-supplies.test", "Seeded as a demo record - safe to delete.", now.AddDays(-120));
        _data.Suppliers.Add(supplier);
        Remember("Supplier", supplier.Id.ToString());

        var requester = new UserRecord(Guid.NewGuid(), "Sam Taylor" + DemoSuffix, "sam.taylor@demo.local", "Teaching", "Main Building") { CanRaiseProjects = true };
        _data.Users.Add(requester);
        Remember("User", requester.Id.ToString());

        // No password, so the demo technician cannot be signed in as - it exists to be assigned work, not to be a
        // second way into the system. The bootstrap administrator stays the only account with credentials.
        var technician = new TechnicianRecord(Guid.NewGuid(), "Jo Bennett" + DemoSuffix, "jo.bennett@demo.local", "IT Support", StaffRoles.DefaultRole);
        _data.Technicians.Add(technician);
        Remember("Technician", technician.Id.ToString());
        var actor = new Actor(technician.Id, technician.Name);

        var sla = new SlaDefinition(Guid.NewGuid(), "Demo SLA - respond in 8 hours", 8, "hours", "Seeded as a demo record - safe to delete.")
        {
            Categories = ["Hardware"]
        };
        _data.Slas.Add(sla);
        Remember("Sla", sla.Id.ToString());

        var ticketAttribute = new TicketAttributeDefinition(Guid.NewGuid(), "Room number" + DemoSuffix, "single-line") { Categories = ["Hardware"] };
        _data.TicketAttributeDefinitions.Add(ticketAttribute);
        Remember("TicketAttribute", ticketAttribute.Id.ToString());
        var assetAttribute = new AssetAttributeDefinition(Guid.NewGuid(), "Warranty provider" + DemoSuffix, "dropdown", "Manufacturer, Supplier, None") { AssetTypes = ["Laptop"] };
        _data.AssetAttributeDefinitions.Add(assetAttribute);
        Remember("AssetAttribute", assetAttribute.Id.ToString());

        var asset = new AssetRecord(Guid.NewGuid(), "DEMO-LT-001", "Dell", "Latitude 5440", "Laptop", "DEMO-SN-0001", "Main Building", requester.Id, supplier.Id)
        {
            Status = "In use",
            PurchaseDate = today.AddYears(-2),
            PurchasePrice = 649.00m,
            PurchaseOrder = "PO-DEMO-001",
            WarrantyEnd = today.AddDays(45),
            Assignments = { new AssetAssignment(requester.Id, requester.Name, now.AddDays(-400), null, null) },
            History = { new AssetActivity("Assigned user changed", $"Assigned to {requester.Name}.", now.AddDays(-400)) { By = actor } },
            Comments = { new AssetComment("Battery health checked at the last service.", now.AddDays(-30)) { By = actor } }
        };
        _data.Assets.Add(asset);
        Remember("Asset", asset.Id.ToString());
        _data.AssetAttributeValues.Add(new AssetAttributeValue(asset.Id, assetAttribute.Id, "Supplier"));

        // A second asset so the demo loan kit has something in it. A kit with no contents would show the feature
        // without showing what it is for - and an empty child collection is exactly what hid an earlier foreign key bug.
        // Out with the demo kit below, so its status and holder match the open loan - the same state IssueKit produces.
        // The assignment carries the kit loan's id, exactly as IssueKit stamps it, so the loan report counts this as one
        // kit loan rather than a kit loan plus a separate loan of the laptop inside it.
        var demoKitLoanId = Guid.NewGuid();
        var loanAsset = new AssetRecord(Guid.NewGuid(), "DEMO-LT-002", "Dell", "Latitude 5440", "Laptop", "DEMO-SN-0002", "Main Building", requester.Id, supplier.Id)
        {
            Status = OnLoanStatus,
            LoanDueDate = today.AddDays(4),
            Assignments = { new AssetAssignment(requester.Id, requester.Name, now.AddDays(-3), null, today.AddDays(4)) { Reason = "Forgot own device", KitLoanId = demoKitLoanId } },
            History = { new AssetActivity("Assigned user changed", $"Assigned to {requester.Name}.", now.AddDays(-3)) { By = actor } },
            PurchaseDate = today.AddYears(-1),
            PurchasePrice = 649.00m,
            PurchaseOrder = "PO-DEMO-001",
            WarrantyEnd = today.AddYears(2)
        };
        _data.Assets.Add(loanAsset);
        Remember("Asset", loanAsset.Id.ToString());

        // Five received, one fitted to the demo ticket below, so the stock history and the ticket agree with each other.
        var part = new PartRecord(Guid.NewGuid(), "Demo laptop charger", "DEMO-CHG-65W", "Consumables", 4, now.AddDays(-60))
        {
            Location = "IT Store",
            ReorderThreshold = 2,
            SupplierIds = [supplier.Id],
            AssetTypes = ["Laptop"],
            History = { new PartActivity("Stock adjusted", "0 -> 5 (Opening stock)", now.AddDays(-60)) { By = actor } }
        };
        _data.Parts.Add(part);
        Remember("Part", part.Id.ToString());

        var createdAt = now.AddDays(-2);
        var number = ++_data.LastTicketNumber;
        var ticket = new TicketRecord(number, "Demo ticket - laptop will not charge",
            "Sam reports the laptop runs from the battery but will not charge from the mains.",
            requester.Id, [asset.Id], technician.Id, "High", "Open", "Hardware", createdAt, null,
            sla.Id, CalculateDueDate(sla.Id, createdAt), false, false, "IT Support", "Main Building")
        {
            Type = TicketTypes.Incident,
            History =
            {
                new TicketActivity("Ticket created", "The ticket was created.", createdAt) { By = actor },
                new TicketActivity("Part assigned", $"Assigned 1x {part.Name}.", createdAt.AddHours(3)) { By = actor }
            },
            Comments = { new TicketComment("Swapped the charger, monitoring before closing.", createdAt.AddHours(3)) { By = actor } }
        };
        _data.Tickets.Add(ticket);
        Remember("Ticket", number.ToString());
        _data.TicketParts.Add(new TicketPartAssignment(number, part.Id, 1));
        _data.TicketAttributeValues.Add(new TicketAttributeValue(number, ticketAttribute.Id, "12"));

        var template = new TicketTemplate(Guid.NewGuid(), "Demo template - new starter laptop", TicketTypes.Request,
            "Laptop for new starter", "Please prepare and hand over a laptop for a new member of staff.", "Hardware", "Normal", sla.Id)
        {
            AttributeValues = { [ticketAttribute.Id] = "" }
        };
        _data.TicketTemplates.Add(template);
        Remember("TicketTemplate", template.Id.ToString());

        var kit = new LoanKit(Guid.NewGuid(), "Demo loan kit A", "Laptop and charger for anyone without a device.", now.AddDays(-90))
        {
            AssetIds = [loanAsset.Id]
        };
        _data.LoanKits.Add(kit);
        Remember("LoanKit", kit.Id.ToString());
        // Currently out and due back shortly, so the loan list and the repeat-borrower report both have something to show.
        _data.KitLoans.Add(new KitLoan(demoKitLoanId, kit.Id, requester.Id, requester.Name, "Forgot own device",
            now.AddDays(-3), today.AddDays(4), null, technician.Name, "Seeded as a demo record - safe to delete."));

        // A project the lead has already assigned, so the list, the workload picker and the portal all have one to show.
        var requesterActor = new Actor(null, requester.Name);
        var project = new ProjectRecord(NextProjectNumber(), "Class set of iPads for Year 7" + DemoSuffix, requester.Id, today.AddDays(21),
            "30 iPads with keyboards, pencils, chargers and screen protectors\nA charging trolley to keep them in", now.AddDays(-5))
        {
            PurchasingRequirements = [.. _data.PurchasingRequirements.Take(1)],
            PurchasingOther = "Delivery before the start of next half term",
            SuggestedPriority = 2,
            Priority = 2,
            TechnicianId = technician.Id,
            Status = ProjectStatuses.GatheringQuotes,
            History =
            [
                new ProjectActivity("Project raised", $"Raised by {requester.Name} with a suggested priority of {ProjectPriorities.Label(2)}.", now.AddDays(-5)) { By = requesterActor },
                new ProjectActivity("Assignment", $"Priority confirmed as {ProjectPriorities.Label(2)} (as suggested); Assigned to {technician.Name}; Status: {ProjectStatuses.New} → {ProjectStatuses.GatheringQuotes}.", now.AddDays(-4)) { By = actor }
            ],
            Notes = [new ProjectNote("Asking two suppliers for quotes this week.", now.AddDays(-3)) { By = actor }],
            Items =
            [
                new ProjectItem(Guid.NewGuid(), "iPad (10th generation)", 30)
                {
                    SubItems =
                    [
                        new ProjectSubItem(Guid.NewGuid(), "Keyboard case", 30), new ProjectSubItem(Guid.NewGuid(), "Apple Pencil", 30),
                        new ProjectSubItem(Guid.NewGuid(), "Charging plug", 30), new ProjectSubItem(Guid.NewGuid(), "Screen protector", 30)
                    ],
                    Suppliers =
                    [
                        new ItemSupplier(supplier.Id)
                        {
                            StatusHistory =
                            [
                                new QuoteStatusChange(QuoteStatuses.NotRequested, now.AddDays(-4)) { By = actor },
                                new QuoteStatusChange(QuoteStatuses.Requested, now.AddDays(-3)) { By = actor }
                            ]
                        }
                    ]
                },
                new ProjectItem(Guid.NewGuid(), "Charging trolley", 1)
            ]
        };
        _data.Projects.Add(project);
        Remember("Project", project.Number.ToString());

        // A teacher starting in two weeks, from the example Teacher template, with the first jobs already ticked - so the
        // Onboarding list, the Overview's New starters card and the ticket list's Onboarding queue all have one to show.
        // Sam is the line manager; the laptop task is Jo's. The laptop and portal tasks are left for someone to try. Two weeks
        // out, the earliest task falls due today, so what is ticked was done on time and nothing starts overdue.
        var starter = new UserRecord(Guid.NewGuid(), "Alex Morgan" + DemoSuffix, "alex.morgan@demo.local", "Teaching", "Main Building");
        _data.Users.Add(starter);
        Remember("User", starter.Id.ToString());
        var teacher = _data.OnboardingTemplates.FirstOrDefault(x => string.Equals(x.Name, "Teacher", StringComparison.OrdinalIgnoreCase));
        var startDate = DateOnly.FromDateTime(DateTime.Now).AddDays(14);
        var onboardingNumber = ++_data.LastTicketNumber;
        var ticked = 0;
        var onboarding = new OnboardingRecord(onboardingNumber, starter.Id, startDate, now.AddDays(-1))
        {
            JobTitle = "Teacher of Science",
            TemplateName = teacher?.Name ?? "",
            LineManagerId = requester.Id,
            PackDocumentIds = teacher?.DocumentIds.Where(id => _data.OnboardingDocuments.Any(x => x.Id == id)).ToList() ?? [],
            Tasks = (teacher?.Tasks ?? []).Select(x =>
            {
                var task = new OnboardingTask(Guid.NewGuid(), x.Title, x.Stage, x.OffsetDays, x.Owner) { Action = x.Action, AssetType = x.AssetType };
                if (x.Action == OnboardingActions.IssueAsset) return task with { TechnicianId = technician.Id };
                // The two earliest plain tasks are done, so the progress bars have something in them.
                if (x.Action == OnboardingActions.None && x.Stage == OnboardingStages.BeforeArrival && ticked < 2)
                {
                    ticked++;
                    return task with { CompletedAt = now.AddMinutes(-30 + ticked * 10), CompletedBy = actor };
                }
                return task;
            }).ToList()
        };
        _data.Onboardings.Add(onboarding);
        var onboardingTicket = new TicketRecord(onboardingNumber, OnboardingTitle(onboarding, starter), OnboardingDescription(onboarding, starter), starter.Id, [], technician.Id,
            "Normal", OpenStatus() ?? "Open", OnboardingCategory, now.AddDays(-1), null, null, OnboardingDue(onboarding), true, true, null, starter.Location)
        {
            Type = TicketTypes.Request,
            History =
            [
                new("Ticket created", "The ticket was created.", now.AddDays(-1)) { By = actor },
                new("Onboarding started", $"{starter.Name} starts on {startDate:dddd d MMMM yyyy}. " +
                    (teacher is null ? "No checklist template was used." : $"Checklist from the {teacher.Name} template: {Plural(onboarding.Tasks.Count, "task")}."), now.AddDays(-1)) { By = actor }
            ]
        };
        _data.Tickets.Add(WithSlaClock(onboardingTicket, now));
        Remember("Ticket", onboardingNumber.ToString());
    }
}
