using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// Works out what changed by comparing a snapshot of the data taken before a save with one taken after it, so every
// operation is audited without each one having to log itself. Tickets and assets are only tracked for the things their
// own history does not already record (ticket deletion, asset creation and deletion, asset custom attribute values).
internal static class AuditTracker
{
    [Flags]
    internal enum Track { None = 0, Create = 1, Update = 2, Delete = 4, All = 7 }

    internal sealed record Entity(string Area, string Type, string Key, string Label, string LinkType, string LinkKey, Track Track, bool IsListItem, IReadOnlyList<(string Name, string Value)> Fields);

    internal sealed class Snapshot
    {
        public Dictionary<string, Entity> Entities { get; } = new(StringComparer.Ordinal);
    }

    public static Snapshot Take(HelpdeskStore.StoreData d)
    {
        var s = new Snapshot();

        foreach (var x in d.Users)
            Add(s, "Users", "User", x.Id.ToString(), x.Name, Track.All, ("Name", x.Name), ("Email", x.Email), ("Department", x.Department), ("Location", x.Location));
        foreach (var x in d.Technicians)
            // PasswordHash is deliberately excluded - it would otherwise end up as human-readable diff text in the plaintext AuditLog table.
            Add(s, "Technicians", "Technician", x.Id.ToString(), x.Name, Track.All, ("Name", x.Name), ("Email", x.Email), ("Team", x.Team), ("Role", x.Role), ("Active", x.IsActive ? "Yes" : "No"));
        foreach (var x in d.Roles)
            Add(s, "Roles", "Role", x.Name.ToLowerInvariant(), x.Name, x.IsProtected ? Track.None : Track.All,
                ("Settings", Yn(x.AllowSettings)), ("Manage roles", Yn(x.AllowManageRoles)), ("Manage staff accounts", Yn(x.AllowManageStaff)), ("Manage requesters", Yn(x.AllowManageRequesters)),
                ("Manage assets", Yn(x.AllowManageAssets)), ("Manage suppliers", Yn(x.AllowManageSuppliers)), ("Manage parts", Yn(x.AllowManageParts)), ("Delete/merge tickets", Yn(x.AllowTicketDestructive)), ("Change \"Working as\"", Yn(x.AllowChangeWorkingAs)));
        foreach (var x in d.Suppliers)
            Add(s, "Suppliers", "Supplier", x.Id.ToString(), x.Name, Track.All,
                ("Name", x.Name), ("Contact", x.ContactName), ("Email", x.Email), ("Phone", x.Phone), ("Address line 1", x.AddressLine1), ("Address line 2", x.AddressLine2),
                ("City", x.City), ("State / region", x.StateRegion), ("Postal code", x.PostalCode), ("Country", x.Country), ("Website", x.Website), ("Notes", x.Notes));
        foreach (var x in d.Parts)
            Add(s, "Parts", "Part", x.Id.ToString(), x.Name, Track.All, ("Name", x.Name), ("SKU", x.Sku), ("Category", x.Category), ("Quantity on hand", x.QuantityOnHand.ToString()));

        foreach (var x in d.Assets)
            Add(s, "Assets", "Asset", x.Id.ToString(), x.AssetTag, Track.Create | Track.Delete,
                ("Asset tag", x.AssetTag), ("Make", x.Make), ("Model", x.Model), ("Type", x.Type), ("Serial number", x.SerialNumber), ("Location", x.Location), ("Status", x.Status));
        var assetsById = d.Assets.ToDictionary(x => x.Id);
        var assetAttributesById = d.AssetAttributeDefinitions.ToDictionary(x => x.Id);
        foreach (var x in d.AssetAttributeValues)
        {
            if (!assetsById.TryGetValue(x.AssetId, out var asset) || !assetAttributesById.TryGetValue(x.AttributeDefinitionId, out var definition)) continue;
            var link = asset.Id.ToString();
            Add(s, "Assets", "Asset attribute value", $"{x.AssetId}|{x.AttributeDefinitionId}", $"{asset.AssetTag} · {definition.Name}", "Asset", link, Track.Create | Track.Update, false, ("Value", x.Value));
        }

        foreach (var x in d.Tickets)
            Add(s, "Tickets", "Ticket", x.Number.ToString(), $"#{x.Number} {x.Title}", Track.Delete, ("Title", x.Title), ("Status", x.Status), ("Priority", x.Priority), ("Category", x.Category));

        foreach (var x in d.Slas)
            Add(s, "SLAs", "SLA", x.Id.ToString(), x.Name, Track.All,
                ("Name", x.Name), ("Target time", $"{x.Duration} {x.DurationUnit}"), ("Description", x.Description ?? ""), ("Priorities", Joined(x.Priorities)), ("Categories", Joined(x.Categories)));
        foreach (var x in d.TicketTemplates)
            Add(s, "Ticket templates", "Ticket template", x.Id.ToString(), x.Name, Track.All,
                ("Name", x.Name), ("Type", x.Type), ("Title", x.Title), ("Description", x.Description), ("Category", x.Category), ("Priority", x.Priority),
                ("SLA", x.SlaId is { } sla ? d.Slas.FirstOrDefault(y => y.Id == sla)?.Name ?? "" : "Automatic"),
                ("Attribute defaults", x.AttributeValues.Count.ToString()));
        foreach (var x in d.AssetAttributeDefinitions)
            Add(s, "Custom attributes", "Asset attribute", x.Id.ToString(), x.Name, Track.All,
                ("Name", x.Name), ("Field type", x.FieldType), ("Choices", x.Choices), ("Asset types", x.AssetTypes.Count == 0 ? "All asset types" : Joined(x.AssetTypes)));
        foreach (var x in d.TicketAttributeDefinitions)
            Add(s, "Custom attributes", "Ticket attribute", x.Id.ToString(), x.Name, Track.All,
                ("Name", x.Name), ("Field type", x.FieldType), ("Choices", x.Choices), ("Categories", x.Categories.Count == 0 ? "All categories" : Joined(x.Categories)));

        var b = d.Branding ?? new BrandingSettings();
        Add(s, "Settings", "Branding", "branding", "Branding", Track.Update,
            ("Brand name", b.BrandName), ("Dashboard eyebrow", b.DashboardEyebrow), ("Dashboard title", b.DashboardTitle), ("Dashboard description", b.DashboardDescription),
            ("Primary colour", b.PrimaryColor), ("Accent colour", b.AccentColor), ("Background colour", b.BackgroundColor), ("Dark mode", b.DarkMode ? "On" : "Off"));
        foreach (var pair in d.StatusDescriptions)
            Add(s, "Settings", "Status description", pair.Key.ToLowerInvariant(), $"Status description: {pair.Key}", Track.All, ("Description", pair.Value));
        foreach (var pair in d.AssetModelMakes)
            Add(s, "Lists", "Model make", pair.Key.ToLowerInvariant(), $"Make for model: {pair.Key}", Track.All, ("Make", pair.Value));

        foreach (var pair in d.AssetTypeLifespans)
            Add(s, "Lists", "Asset type lifespan", pair.Key.ToLowerInvariant(), $"Lifespan for asset type: {pair.Key}", Track.All, ("Years", pair.Value.ToString()));
        Add(s, "Settings", "Asset review", "asset-review", "Asset review window", Track.Update, ("Days", d.AssetReviewDays.ToString()));
        Add(s, "Settings", "Ticket due soon", "ticket-due-soon", "Ticket due soon window", Track.Update, ("Hours", d.TicketDueSoonHours.ToString()));

        AddList(s, "Lists", "Asset status", d.AssetStatuses);
        AddList(s, "Lists", "Team", d.TechnicianTeams);
        AddList(s, "Lists", "Department", d.Departments);
        AddList(s, "Lists", "Location", d.Locations);
        AddList(s, "Lists", "Asset type", d.AssetTypes);
        AddList(s, "Lists", "Asset make", d.AssetMakes);
        AddList(s, "Lists", "Asset model", d.AssetModels);
        AddList(s, "Lists", "Category", d.Categories);
        AddList(s, "Lists", "Status", d.Statuses);
        AddList(s, "Lists", "Priority", d.Priorities);
        AddList(s, "Settings", "Closing message required for priority", d.RequireCloseMessagePriorities);
        AddList(s, "Settings", "Closing message required for category", d.RequireCloseMessageCategories);
        return s;
    }

    public static List<AuditEntry> Diff(Snapshot before, Snapshot after, DateTime at)
    {
        var entries = new List<AuditEntry>();
        var addedItems = new List<Entity>();
        var removedItems = new List<Entity>();

        foreach (var (key, now) in after.Entities)
        {
            if (!before.Entities.TryGetValue(key, out var was))
            {
                if (!now.Track.HasFlag(Track.Create)) continue;
                if (now.IsListItem) addedItems.Add(now);
                else entries.Add(Entry(at, now, "Created", Summarise(now.Fields)));
            }
            else if (now.Track.HasFlag(Track.Update))
            {
                var changes = Describe(was.Fields, now.Fields);
                if (changes.Length > 0) entries.Add(Entry(at, now, "Updated", changes));
            }
        }
        foreach (var (key, was) in before.Entities)
        {
            if (after.Entities.ContainsKey(key) || !was.Track.HasFlag(Track.Delete)) continue;
            if (was.IsListItem) removedItems.Add(was);
            else entries.Add(Entry(at, was, "Deleted", Summarise(was.Fields)));
        }

        // Within one list, a single value replaced by another in one save is a rename.
        foreach (var type in addedItems.Select(x => x.Type).Union(removedItems.Select(x => x.Type)).Distinct())
        {
            var added = addedItems.Where(x => x.Type == type).ToList();
            var removed = removedItems.Where(x => x.Type == type).ToList();
            if (added.Count == 1 && removed.Count == 1)
            {
                entries.Add(new AuditEntry(at, added[0].Area, null, null, type, "Renamed", $"{Short(removed[0].Label)} → {Short(added[0].Label)}"));
                continue;
            }
            entries.AddRange(added.Select(x => new AuditEntry(at, x.Area, null, null, type, "Added", Short(x.Label))));
            entries.AddRange(removed.Select(x => new AuditEntry(at, x.Area, null, null, type, "Removed", Short(x.Label))));
        }
        return entries;
    }

    private static AuditEntry Entry(DateTime at, Entity e, string action, string details) =>
        new(at, e.Area, e.LinkType, e.LinkKey, e.Label, action, details);

    private static void Add(Snapshot s, string area, string type, string key, string label, Track track, params (string Name, string? Value)[] fields) =>
        Add(s, area, type, key, label, type, key, track, false, fields);

    private static void Add(Snapshot s, string area, string type, string key, string label, string linkType, string linkKey, Track track, bool isListItem, params (string Name, string? Value)[] fields) =>
        s.Entities[$"{type}|{key}"] = new Entity(area, type, key, label ?? string.Empty, linkType, linkKey, track, isListItem, fields.Select(f => (f.Name, f.Value ?? string.Empty)).ToList());

    private static void AddList(Snapshot s, string area, string type, IEnumerable<string> values)
    {
        foreach (var value in values.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
            Add(s, area, type, value.ToLowerInvariant(), value, type, value, Track.Create | Track.Delete, true);
    }

    private static string Joined(IEnumerable<string> values) => string.Join(", ", values.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));

    private static string Summarise(IReadOnlyList<(string Name, string Value)> fields) =>
        string.Join("; ", fields.Where(f => f.Value.Length > 0).Select(f => $"{f.Name}: {Short(f.Value)}"));

    private static string Describe(IReadOnlyList<(string Name, string Value)> before, IReadOnlyList<(string Name, string Value)> after)
    {
        var old = before.ToDictionary(f => f.Name, f => f.Value);
        return string.Join("; ", after
            .Where(f => !string.Equals(old.GetValueOrDefault(f.Name, string.Empty), f.Value, StringComparison.Ordinal))
            .Select(f => $"{f.Name}: {Show(old.GetValueOrDefault(f.Name, string.Empty))} → {Show(f.Value)}"));
    }

    private static string Show(string value) => value.Length == 0 ? "(empty)" : Short(value);

    private static string Short(string value) => value.Length <= 120 ? value : value[..117] + "...";

    private static string Yn(bool value) => value ? "Yes" : "No";
}
