using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using EduHelpdesk.Models;
using Microsoft.Data.Sqlite;
using System.Net;
using System.Text;

namespace EduHelpdesk.Services;

public sealed class HelpdeskStore
{
    private readonly string _path;
    private readonly string _legacyPath;
    private readonly object _sync = new();
    private StoreData _data;
    private readonly string _templatePath;

    public HelpdeskStore(IHostEnvironment environment)
    {
        _path = Path.Combine(environment.ContentRootPath, "App_Data", "helpdesk.db");
        _legacyPath = Path.Combine(environment.ContentRootPath, "App_Data", "helpdesk.json");
        _templatePath = Path.Combine(environment.ContentRootPath, "App_Data", "print-template.docx");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        _data = Load();
        _data.Assets = _data.Assets.Select(x => x with
        {
            Make = x.Make ?? string.Empty,
            Model = x.Model ?? string.Empty,
            Type = x.Type ?? string.Empty,
            SerialNumber = x.SerialNumber ?? string.Empty,
            Location = x.Location ?? string.Empty
        }).ToList();
        _data.Suppliers ??= [];
        _data.Suppliers = _data.Suppliers.Select(x => x with
        {
            Name = x.Name?.Trim() ?? string.Empty, ContactName = x.ContactName?.Trim() ?? string.Empty,
            Email = x.Email?.Trim() ?? string.Empty, Phone = x.Phone?.Trim() ?? string.Empty,
            AddressLine1 = x.AddressLine1?.Trim() ?? string.Empty, AddressLine2 = x.AddressLine2?.Trim() ?? string.Empty,
            City = x.City?.Trim() ?? string.Empty, StateRegion = x.StateRegion?.Trim() ?? string.Empty,
            PostalCode = x.PostalCode?.Trim() ?? string.Empty, Country = x.Country?.Trim() ?? string.Empty,
            Website = x.Website?.Trim() ?? string.Empty, Notes = x.Notes?.Trim() ?? string.Empty
        }).Where(x => !string.IsNullOrWhiteSpace(x.Name)).ToList();
        _data.Users = _data.Users.Select(x => x with
        {
            Department = x.Department ?? string.Empty,
            Location = x.Location ?? string.Empty
        }).ToList();
        _data.Branding ??= new BrandingSettings();
        _data.AssetAttributeDefinitions ??= [];
        _data.AssetAttributeValues ??= [];
        _data.Slas ??= [];
        _data.Slas = _data.Slas.Select(x => x with { Name = x.Name?.Trim() ?? string.Empty, Duration = Math.Max(1, x.Duration), DurationUnit = NormalizeDurationUnit(x.DurationUnit), Priority = string.IsNullOrWhiteSpace(x.Priority) ? null : x.Priority.Trim() }).Where(x => !string.IsNullOrWhiteSpace(x.Name)).ToList();
        _data.TicketAttributeDefinitions ??= [];
        _data.TicketAttributeValues ??= [];
        _data.TicketAttributeDefinitions = _data.TicketAttributeDefinitions.Select(x => x with { Name = x.Name.Trim(), Category = string.IsNullOrWhiteSpace(x.Category) ? null : x.Category.Trim(), FieldType = NormalizeAttributeType(x.FieldType), Choices = NormalizeChoices(x.Choices) }).ToList();
        _data.AssetAttributeDefinitions = _data.AssetAttributeDefinitions.Select(x => x with
        {
            AssetType = string.IsNullOrWhiteSpace(x.AssetType) ? null : x.AssetType.Trim(),
            FieldType = NormalizeAttributeType(x.FieldType),
            Choices = NormalizeChoices(x.Choices)
        }).ToList();
        foreach (var team in _data.Technicians.Select(x => x.Team).Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            if (!_data.TechnicianTeams.Contains(team, StringComparer.OrdinalIgnoreCase))
                _data.TechnicianTeams.Add(team);
        }
        foreach (var department in _data.Users.Select(x => x.Department).Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            if (!_data.Departments.Contains(department, StringComparer.OrdinalIgnoreCase))
                _data.Departments.Add(department);
        }
        EnsureOptions(_data.Locations, _data.Users.Select(x => x.Location).Concat(_data.Assets.Select(x => x.Location)));
        EnsureOptions(_data.AssetTypes, _data.Assets.Select(x => x.Type));
        EnsureOptions(_data.AssetMakes, _data.Assets.Select(x => x.Make));
        EnsureOptions(_data.AssetModels, _data.Assets.Select(x => x.Model));
        EnsureOptions(_data.Categories, ["Hardware", "Software", "Account", "Network", "Classroom AV", "Other"]);
        EnsureOptions(_data.Statuses, ["Open", "In Progress", "On Hold", "Closed"]);
        EnsureOptions(_data.Priorities, ["Normal", "Low", "High", "Urgent"]);
        if (_data.Users.Count == 0 && _data.Technicians.Count == 0)
        {
            Seed();
        }
        else
        {
            Save();
        }
    }

    public IReadOnlyList<UserRecord> Users { get { lock (_sync) return _data.Users; } }
    public IReadOnlyList<TechnicianRecord> Technicians { get { lock (_sync) return _data.Technicians; } }
    public IReadOnlyList<string> TechnicianTeams { get { lock (_sync) return _data.TechnicianTeams.OrderBy(x => x).ToList(); } }
    public IReadOnlyList<string> Departments { get { lock (_sync) return _data.Departments.OrderBy(x => x).ToList(); } }
    public IReadOnlyList<string> Locations { get { lock (_sync) return _data.Locations.OrderBy(x => x).ToList(); } }
    public IReadOnlyList<string> AssetTypes { get { lock (_sync) return _data.AssetTypes.OrderBy(x => x).ToList(); } }
    public IReadOnlyList<string> AssetMakes { get { lock (_sync) return _data.AssetMakes.OrderBy(x => x).ToList(); } }
    public IReadOnlyList<string> AssetModels { get { lock (_sync) return _data.AssetModels.OrderBy(x => x).ToList(); } }
    public IReadOnlyList<string> Categories { get { lock (_sync) return _data.Categories.ToList(); } }
    public IReadOnlyList<string> Statuses { get { lock (_sync) return _data.Statuses.ToList(); } }
    public IReadOnlyDictionary<string, string> StatusDescriptions { get { lock (_sync) return new Dictionary<string, string>(_data.StatusDescriptions, StringComparer.OrdinalIgnoreCase); } }
    public IReadOnlyList<string> Priorities { get { lock (_sync) return _data.Priorities.ToList(); } }
    public IReadOnlyList<string> RequireCloseMessagePriorities { get { lock (_sync) return _data.RequireCloseMessagePriorities.ToList(); } }
    public IReadOnlyList<string> RequireCloseMessageCategories { get { lock (_sync) return _data.RequireCloseMessageCategories.ToList(); } }
    public IReadOnlyList<AssetRecord> Assets { get { lock (_sync) return _data.Assets; } }
    public IReadOnlyList<SupplierRecord> Suppliers { get { lock (_sync) return _data.Suppliers.OrderBy(x => x.Name).ToList(); } }
    public IReadOnlyList<PartRecord> Parts { get { lock (_sync) return _data.Parts.OrderBy(x => x.Name).ToList(); } }
    public IReadOnlyList<AssetAttributeDefinition> AssetAttributeDefinitions { get { lock (_sync) return _data.AssetAttributeDefinitions.OrderBy(x => x.AssetType).ThenBy(x => x.Name).ToList(); } }
    public IReadOnlyList<SlaDefinition> Slas { get { lock (_sync) return _data.Slas.OrderBy(x => x.Name).ToList(); } }
    public IReadOnlyList<TicketAttributeDefinition> TicketAttributeDefinitions { get { lock (_sync) return _data.TicketAttributeDefinitions.OrderBy(x => x.Category).ThenBy(x => x.Name).ToList(); } }
    public IReadOnlyList<TicketRecord> Tickets { get { lock (_sync) return _data.Tickets.OrderByDescending(x => x.Number).ToList(); } }
    public BrandingSettings Branding { get { lock (_sync) return _data.Branding; } }
    public bool HasPrintTemplate => File.Exists(_templatePath);
    public IReadOnlyList<TicketAttributeDefinition> GetTicketAttributes(string category) => TicketAttributeDefinitions.Where(x => x.Category is null || x.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();
    public IReadOnlyDictionary<Guid, string> GetTicketAttributeValues(int number) { lock (_sync) return _data.TicketAttributeValues.Where(x => x.TicketNumber == number).ToDictionary(x => x.AttributeDefinitionId, x => x.Value); }

    public void AddUser(UserRecord item) { lock (_sync) { _data.Users.Add(item); Save(); } }
    public void AddTechnician(TechnicianRecord item) { lock (_sync) { _data.Technicians.Add(item); Save(); } }
    public string AddTechnicianTeam(string team)
    {
        lock (_sync)
        {
            var value = team.Trim();
            if (string.IsNullOrWhiteSpace(value)) return "Team name is required.";
            if (_data.TechnicianTeams.Contains(value, StringComparer.OrdinalIgnoreCase))
                return "That team already exists.";
            _data.TechnicianTeams.Add(value);
            Save();
            return "Technician team added.";
        }
    }
    public string UpdateTechnicianTeam(string currentTeam, string team)
    {
        lock (_sync)
        {
            var oldValue = currentTeam.Trim();
            var newValue = team.Trim();
            if (string.IsNullOrWhiteSpace(newValue)) return "Team name is required.";
            if (string.Equals(oldValue, newValue, StringComparison.OrdinalIgnoreCase))
                return "Technician team updated.";
            if (_data.TechnicianTeams.Contains(newValue, StringComparer.OrdinalIgnoreCase))
                return "That team already exists.";
            var index = _data.TechnicianTeams.FindIndex(x => string.Equals(x, oldValue, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return "Technician team was not found.";
            _data.TechnicianTeams[index] = newValue;
            for (var i = 0; i < _data.Technicians.Count; i++)
            {
                if (string.Equals(_data.Technicians[i].Team, oldValue, StringComparison.OrdinalIgnoreCase))
                    _data.Technicians[i] = _data.Technicians[i] with { Team = newValue };
            }
            Save();
            return "Technician team updated.";
        }
    }
    public string DeleteTechnicianTeam(string team)
    {
        lock (_sync)
        {
            var value = (team ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value)) return "Team name is required.";
            var index = _data.TechnicianTeams.FindIndex(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return "Technician team was not found.";
            if (_data.Technicians.Any(x => string.Equals(x.Team, value, StringComparison.OrdinalIgnoreCase))) return "That team cannot be deleted because technicians use it.";
            _data.TechnicianTeams.RemoveAt(index);
            Save();
            return "Technician team deleted.";
        }
    }
    public string AddDepartment(string department)
    {
        lock (_sync)
        {
            var value = department.Trim();
            if (string.IsNullOrWhiteSpace(value)) return "Department name is required.";
            if (_data.Departments.Contains(value, StringComparer.OrdinalIgnoreCase)) return "That department already exists.";
            _data.Departments.Add(value);
            Save();
            return "Department added.";
        }
    }
    public string UpdateDepartment(string currentDepartment, string department)
    {
        lock (_sync)
        {
            var oldValue = currentDepartment.Trim();
            var newValue = department.Trim();
            if (string.IsNullOrWhiteSpace(newValue)) return "Department name is required.";
            if (string.Equals(oldValue, newValue, StringComparison.OrdinalIgnoreCase)) return "Department updated.";
            if (_data.Departments.Contains(newValue, StringComparer.OrdinalIgnoreCase)) return "That department already exists.";
            var index = _data.Departments.FindIndex(x => string.Equals(x, oldValue, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return "Department was not found.";
            _data.Departments[index] = newValue;
            for (var i = 0; i < _data.Users.Count; i++)
            {
                if (string.Equals(_data.Users[i].Department, oldValue, StringComparison.OrdinalIgnoreCase))
                    _data.Users[i] = _data.Users[i] with { Department = newValue };
            }
            Save();
            return "Department updated.";
        }
    }
    public string DeleteDepartment(string department)
    {
        lock (_sync)
        {
            var value = (department ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value)) return "Department name is required.";
            var index = _data.Departments.FindIndex(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return "Department was not found.";
            if (_data.Users.Any(x => string.Equals(x.Department, value, StringComparison.OrdinalIgnoreCase))) return "That department cannot be deleted because users belong to it.";
            _data.Departments.RemoveAt(index);
            Save();
            return "Department deleted.";
        }
    }
    public string AddManagedOption(string kind, string value)
    {
        lock (_sync)
        {
            kind = NormalizeManagedOptionKind(kind);
            if (!IsManagedOptionKind(kind)) return "Invalid managed option.";
            var options = GetManagedOptions(kind);
            var item = value.Trim();
            if (string.IsNullOrWhiteSpace(item)) return $"{kind} name is required.";
            if (options.Contains(item, StringComparer.OrdinalIgnoreCase)) return $"That {kind.ToLowerInvariant()} already exists.";
            options.Add(item);
            Save();
            return $"{kind} added.";
        }
    }
    public string UpdateManagedOption(string kind, string currentValue, string value)
    {
        lock (_sync)
        {
            kind = NormalizeManagedOptionKind(kind);
            if (!IsManagedOptionKind(kind)) return "Invalid managed option.";
            var options = GetManagedOptions(kind);
            var oldValue = currentValue.Trim();
            var newValue = value.Trim();
            if (string.IsNullOrWhiteSpace(newValue)) return $"{kind} name is required.";
            if (string.Equals(oldValue, newValue, StringComparison.OrdinalIgnoreCase)) return $"{kind} updated.";
            if (options.Contains(newValue, StringComparer.OrdinalIgnoreCase)) return $"That {kind.ToLowerInvariant()} already exists.";
            var index = options.FindIndex(x => string.Equals(x, oldValue, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return $"{kind} was not found.";
            options[index] = newValue;
            for (var i = 0; i < _data.Users.Count; i++)
                if (kind == "Location" && string.Equals(_data.Users[i].Location, oldValue, StringComparison.OrdinalIgnoreCase))
                    _data.Users[i] = _data.Users[i] with { Location = newValue };
            for (var i = 0; i < _data.Assets.Count; i++)
            {
                var asset = _data.Assets[i];
                _data.Assets[i] = kind switch
                {
                    "Location" when string.Equals(asset.Location, oldValue, StringComparison.OrdinalIgnoreCase) => asset with { Location = newValue },
                    "Asset make" when string.Equals(asset.Make, oldValue, StringComparison.OrdinalIgnoreCase) => asset with { Make = newValue },
                    "Asset type" when string.Equals(asset.Type, oldValue, StringComparison.OrdinalIgnoreCase) => asset with { Type = newValue },
                    "Asset model" when string.Equals(asset.Model, oldValue, StringComparison.OrdinalIgnoreCase) => asset with { Model = newValue },
                    _ => asset
                };
            }
            Save();
            return $"{kind} updated.";
        }
    }
    public string DeleteManagedOption(string kind, string value)
    {
        lock (_sync)
        {
            kind = NormalizeManagedOptionKind(kind);
            if (!IsManagedOptionKind(kind)) return "Invalid managed option.";
            var item = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(item)) return $"{kind} name is required.";
            var options = GetManagedOptions(kind);
            var index = options.FindIndex(x => string.Equals(x, item, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return $"{kind} was not found.";
            var inUse = kind switch
            {
                "Location" => _data.Users.Any(x => string.Equals(x.Location, item, StringComparison.OrdinalIgnoreCase)) || _data.Assets.Any(x => string.Equals(x.Location, item, StringComparison.OrdinalIgnoreCase)),
                "Asset make" => _data.Assets.Any(x => string.Equals(x.Make, item, StringComparison.OrdinalIgnoreCase)),
                "Asset type" => _data.Assets.Any(x => string.Equals(x.Type, item, StringComparison.OrdinalIgnoreCase)) || _data.AssetAttributeDefinitions.Any(x => string.Equals(x.AssetType, item, StringComparison.OrdinalIgnoreCase)),
                "Asset model" => _data.Assets.Any(x => string.Equals(x.Model, item, StringComparison.OrdinalIgnoreCase)),
                _ => false
            };
            if (inUse) return $"That {kind.ToLowerInvariant()} cannot be deleted because it is in use.";
            options.RemoveAt(index);
            Save();
            return $"{kind} deleted.";
        }
    }
    public string AddTicketOption(string kind, string value)
    {
        lock (_sync)
        {
            var options = GetOptions(kind);
            var item = value.Trim();
            if (string.IsNullOrWhiteSpace(item)) return $"{kind} name is required.";
            if (options.Contains(item, StringComparer.OrdinalIgnoreCase)) return $"That {kind.ToLowerInvariant()} already exists.";
            options.Add(item);
            Save();
            return $"{kind} added.";
        }
    }
    public string UpdateTicketOption(string kind, string currentValue, string value)
    {
        lock (_sync)
        {
            var options = GetOptions(kind);
            var oldValue = currentValue.Trim();
            var newValue = value.Trim();
            if (string.IsNullOrWhiteSpace(newValue)) return $"{kind} name is required.";
            if (string.Equals(oldValue, newValue, StringComparison.OrdinalIgnoreCase)) return $"{kind} updated.";
            if (options.Contains(newValue, StringComparer.OrdinalIgnoreCase)) return $"That {kind.ToLowerInvariant()} already exists.";
            var index = options.FindIndex(x => string.Equals(x, oldValue, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return $"{kind} was not found.";
            options[index] = newValue;
            if (kind == "Status" && _data.StatusDescriptions.Remove(oldValue, out var existingDescription))
                _data.StatusDescriptions[newValue] = existingDescription;
            if (kind == "Priority")
            {
                var priorityIndex = _data.RequireCloseMessagePriorities.FindIndex(x => string.Equals(x, oldValue, StringComparison.OrdinalIgnoreCase));
                if (priorityIndex >= 0) _data.RequireCloseMessagePriorities[priorityIndex] = newValue;
            }
            if (kind == "Category")
            {
                var categoryIndex = _data.RequireCloseMessageCategories.FindIndex(x => string.Equals(x, oldValue, StringComparison.OrdinalIgnoreCase));
                if (categoryIndex >= 0) _data.RequireCloseMessageCategories[categoryIndex] = newValue;
            }
            for (var i = 0; i < _data.Tickets.Count; i++)
            {
                var ticket = _data.Tickets[i];
                _data.Tickets[i] = kind switch
                {
                    "Category" when string.Equals(ticket.Category, oldValue, StringComparison.OrdinalIgnoreCase) => ticket with { Category = newValue },
                    "Status" when string.Equals(ticket.Status, oldValue, StringComparison.OrdinalIgnoreCase) => ticket with { Status = newValue },
                    "Priority" when string.Equals(ticket.Priority, oldValue, StringComparison.OrdinalIgnoreCase) => ticket with { Priority = newValue },
                    _ => ticket
                };
            }
            Save();
            return $"{kind} updated.";
        }
    }
    public string DeleteTicketOption(string kind, string value)
    {
        lock (_sync)
        {
            if (!IsTicketOptionKind(kind)) return "Invalid ticket option.";
            var item = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(item)) return $"{kind} name is required.";
            var options = GetOptions(kind);
            var index = options.FindIndex(x => string.Equals(x, item, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return $"{kind} was not found.";
            if (_data.Tickets.Any(x => kind switch
            {
                "Category" => string.Equals(x.Category, item, StringComparison.OrdinalIgnoreCase),
                "Status" => string.Equals(x.Status, item, StringComparison.OrdinalIgnoreCase),
                "Priority" => string.Equals(x.Priority, item, StringComparison.OrdinalIgnoreCase),
                _ => false
            })) return $"That {kind.ToLowerInvariant()} cannot be deleted because tickets use it.";
            options.RemoveAt(index);
            if (kind == "Status") _data.StatusDescriptions.Remove(item);
            if (kind == "Priority") _data.RequireCloseMessagePriorities.RemoveAll(x => string.Equals(x, item, StringComparison.OrdinalIgnoreCase));
            if (kind == "Category") _data.RequireCloseMessageCategories.RemoveAll(x => string.Equals(x, item, StringComparison.OrdinalIgnoreCase));
            Save();
            return $"{kind} deleted.";
        }
    }
    public string SetStatusDescription(string status, string? description)
    {
        lock (_sync)
        {
            var name = (status ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name)) return "Status is required.";
            if (!_data.Statuses.Contains(name, StringComparer.OrdinalIgnoreCase)) return "Status was not found.";
            var text = (description ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text)) _data.StatusDescriptions.Remove(name);
            else _data.StatusDescriptions[name] = text;
            Save();
            return "Status description saved.";
        }
    }
    public string SetCloseMessageRequirements(IEnumerable<string>? priorities, IEnumerable<string>? categories)
    {
        lock (_sync)
        {
            _data.RequireCloseMessagePriorities = (priorities ?? [])
                .Select(x => x.Trim())
                .Where(x => _data.Priorities.Contains(x, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            _data.RequireCloseMessageCategories = (categories ?? [])
                .Select(x => x.Trim())
                .Where(x => _data.Categories.Contains(x, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            Save();
            return "Closing requirements saved.";
        }
    }
    public bool RequiresCloseMessage(TicketRecord ticket)
    {
        lock (_sync)
        {
            return _data.RequireCloseMessagePriorities.Contains(ticket.Priority, StringComparer.OrdinalIgnoreCase)
                || _data.RequireCloseMessageCategories.Contains(ticket.Category, StringComparer.OrdinalIgnoreCase);
        }
    }
    public void AddAsset(AssetRecord item) { lock (_sync) { _data.Assets.Add(item); Save(); } }
    public void AddSupplier(SupplierRecord item) { lock (_sync) { _data.Suppliers.Add(item); Save(); } }
    public bool UpdateSupplier(SupplierRecord item) => Update(item, _data.Suppliers, x => x.Id == item.Id);
    public string? DeleteSupplier(Guid id)
    {
        lock (_sync)
        {
            if (_data.Assets.Any(x => x.SupplierId == id)) return "This supplier is linked to assets and cannot be deleted.";
            var item = _data.Suppliers.FirstOrDefault(x => x.Id == id);
            if (item is null) return "Supplier was not found.";
            _data.Suppliers.Remove(item); Save(); return null;
        }
    }
    public void AddPart(PartRecord item) { lock (_sync) { _data.Parts.Add(item); Save(); } }
    public bool UpdatePart(PartRecord item) => Update(item, _data.Parts, x => x.Id == item.Id);
    public string? DeletePart(Guid id)
    {
        lock (_sync)
        {
            if (_data.TicketParts.Any(x => x.PartId == id)) return "This part is assigned to a ticket and cannot be deleted.";
            var item = _data.Parts.FirstOrDefault(x => x.Id == id);
            if (item is null) return "Part was not found.";
            _data.Parts.Remove(item); Save(); return null;
        }
    }
    public IReadOnlyList<(PartRecord Part, int Quantity)> GetTicketParts(int number)
    {
        lock (_sync)
        {
            return _data.TicketParts
                .Where(x => x.TicketNumber == number)
                .Join(_data.Parts, x => x.PartId, p => p.Id, (x, p) => (p, x.Quantity))
                .OrderBy(x => x.p.Name)
                .ToList();
        }
    }
    public string? SetTicketPartQuantity(int number, Guid partId, int quantity)
    {
        lock (_sync)
        {
            var ticketIndex = _data.Tickets.FindIndex(x => x.Number == number);
            if (ticketIndex < 0) return "Ticket was not found.";
            var partIndex = _data.Parts.FindIndex(x => x.Id == partId);
            if (partIndex < 0) return "Part was not found.";

            var part = _data.Parts[partIndex];
            var assignmentIndex = _data.TicketParts.FindIndex(x => x.TicketNumber == number && x.PartId == partId);
            var previousQuantity = assignmentIndex >= 0 ? _data.TicketParts[assignmentIndex].Quantity : 0;
            var delta = quantity - previousQuantity;

            if (delta > 0 && part.QuantityOnHand < delta) return $"Only {part.QuantityOnHand} {part.Name} left in stock.";

            _data.Parts[partIndex] = part with { QuantityOnHand = part.QuantityOnHand - delta };

            var history = _data.Tickets[ticketIndex].History.ToList();
            if (quantity <= 0)
            {
                if (assignmentIndex >= 0)
                {
                    _data.TicketParts.RemoveAt(assignmentIndex);
                    history.Add(new("Part removed", $"Removed {previousQuantity}x {part.Name} - returned to stock.", DateTime.UtcNow));
                }
            }
            else if (assignmentIndex >= 0)
            {
                _data.TicketParts[assignmentIndex] = _data.TicketParts[assignmentIndex] with { Quantity = quantity };
                history.Add(new("Part quantity changed", $"{part.Name}: {previousQuantity} -> {quantity}", DateTime.UtcNow));
            }
            else
            {
                _data.TicketParts.Add(new(number, partId, quantity));
                history.Add(new("Part assigned", $"Assigned {quantity}x {part.Name}.", DateTime.UtcNow));
            }
            _data.Tickets[ticketIndex] = _data.Tickets[ticketIndex] with { History = history };

            Save();
            return null;
        }
    }
    public int AddTicket(TicketRecord item)
    {
        lock (_sync)
        {
            var number = ++_data.LastTicketNumber;
            var history = new List<TicketActivity>
            {
                new("Ticket created", "The ticket was created.", DateTime.UtcNow)
            };
            _data.Tickets.Add(item with { Number = number, History = history });
            Save();
            return number;
        }
    }
    public bool AddTicketComment(int number, string text)
    {
        lock (_sync)
        {
            var index = _data.Tickets.FindIndex(x => x.Number == number);
            if (index < 0) return false;
            var comments = _data.Tickets[index].Comments.ToList();
            comments.Add(new TicketComment(text.Trim(), DateTime.UtcNow));
            _data.Tickets[index] = _data.Tickets[index] with { Comments = comments };
            Save();
            return true;
        }
    }
        public string AddSla(string name, int duration, string durationUnit, string? priority = null)
        {
            lock (_sync)
            {
                name = name.Trim();
                durationUnit = NormalizeDurationUnit(durationUnit);
                if (string.IsNullOrWhiteSpace(name)) return "SLA name is required.";
                if (duration < 1) return "SLA duration must be at least 1.";
                priority = string.IsNullOrWhiteSpace(priority) ? null : priority.Trim();
                if (_data.Slas.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))) return "That SLA already exists.";
                if (priority is not null && _data.Slas.Any(x => string.Equals(x.Priority, priority, StringComparison.OrdinalIgnoreCase))) return "That priority already has an SLA.";
                _data.Slas.Add(new(Guid.NewGuid(), name, duration, durationUnit, priority));
                Save();
                return "SLA added.";
            }
        }
        public string UpdateSla(Guid id, string name, int duration, string durationUnit, string? priority = null)
        {
            lock (_sync)
            {
                var index = _data.Slas.FindIndex(x => x.Id == id);
                if (index < 0) return "SLA was not found.";
                name = name.Trim();
                durationUnit = NormalizeDurationUnit(durationUnit);
                if (string.IsNullOrWhiteSpace(name)) return "SLA name is required.";
                if (duration < 1) return "SLA duration must be at least 1.";
                priority = string.IsNullOrWhiteSpace(priority) ? null : priority.Trim();
                if (_data.Slas.Any(x => x.Id != id && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))) return "That SLA already exists.";
                if (priority is not null && _data.Slas.Any(x => x.Id != id && string.Equals(x.Priority, priority, StringComparison.OrdinalIgnoreCase))) return "That priority already has an SLA.";
                _data.Slas[index] = new(id, name, duration, durationUnit, priority);
                Save();
                return "SLA updated.";
            }
        }
        public string DeleteSla(Guid id)
        {
            lock (_sync)
            {
                if (_data.Tickets.Any(x => x.SlaId == id)) return "This SLA is used by tickets and cannot be deleted.";
                var index = _data.Slas.FindIndex(x => x.Id == id);
                if (index < 0) return "SLA was not found.";
                _data.Slas.RemoveAt(index);
                Save();
                return "SLA deleted.";
            }
        }
        public DateTime? CalculateDueDate(Guid? slaId, DateTime createdAt) =>
            slaId is Guid id && _data.Slas.FirstOrDefault(x => x.Id == id) is { } sla
                ? createdAt.Add(sla.DurationUnit == "days" ? TimeSpan.FromDays(sla.Duration) : TimeSpan.FromHours(sla.Duration))
                : null;
    public Guid? SlaForPriority(string priority) => _data.Slas.FirstOrDefault(x => string.Equals(x.Priority, priority, StringComparison.OrdinalIgnoreCase))?.Id;
    public bool UpdateUser(UserRecord item) => Update(item, _data.Users, x => x.Id == item.Id);
    public bool UpdateUserAndTickets(UserRecord user, IEnumerable<int> selectedTicketNumbers)
    {
        lock (_sync)
        {
            var userIndex = _data.Users.FindIndex(x => x.Id == user.Id);
            if (userIndex < 0) return false;
            _data.Users[userIndex] = user;
            var selected = selectedTicketNumbers.ToHashSet();
            for (var i = 0; i < _data.Tickets.Count; i++)
            {
                var ticket = _data.Tickets[i];
                if (selected.Contains(ticket.Number))
                    _data.Tickets[i] = ticket with { RequesterId = user.Id };
                else if (ticket.RequesterId == user.Id)
                    continue;
            }
            Save();
            return true;
        }
    }
    public bool UpdateTechnician(TechnicianRecord item) => Update(item, _data.Technicians, x => x.Id == item.Id);
    public bool UpdateAsset(AssetRecord item)
    {
        lock (_sync)
        {
            var index = _data.Assets.FindIndex(x => x.Id == item.Id);
            if (index < 0) return false;

            var previous = _data.Assets[index];
            var history = previous.History.ToList();
            AddAssetActivities(history, previous, item);
            _data.Assets[index] = item with { History = history, Comments = previous.Comments };
            Save();
            return true;
        }
    }
    public bool AddAssetComment(Guid assetId, string text)
    {
        lock (_sync)
        {
            var index = _data.Assets.FindIndex(x => x.Id == assetId);
            if (index < 0) return false;
            var comments = _data.Assets[index].Comments.ToList();
            comments.Add(new AssetComment(text.Trim(), DateTime.UtcNow));
            _data.Assets[index] = _data.Assets[index] with { Comments = comments };
            Save();
            return true;
        }
    }
    public bool LinkAssetToTicket(Guid assetId, int ticketNumber)
    {
        lock (_sync)
        {
            var asset = _data.Assets.FirstOrDefault(x => x.Id == assetId);
            if (asset is null) return false;
            var index = _data.Tickets.FindIndex(x => x.Number == ticketNumber);
            if (index < 0) return false;
            var ticket = _data.Tickets[index];
            if (ticket.AssetIds.Contains(assetId)) return true;
            var history = ticket.History.ToList();
            history.Add(new("Asset changed", $"Asset {asset.AssetTag} was linked.", DateTime.UtcNow));
            _data.Tickets[index] = ticket with { AssetIds = ticket.AssetIds.Append(assetId).ToList(), History = history };
            var assetIndex = _data.Assets.FindIndex(x => x.Id == assetId);
            if (assetIndex >= 0)
            {
                var assetHistory = _data.Assets[assetIndex].History.ToList();
                assetHistory.Add(new("Ticket linked", $"Linked to ticket #{ticket.Number} - {ticket.Title}", DateTime.UtcNow));
                _data.Assets[assetIndex] = _data.Assets[assetIndex] with { History = assetHistory };
            }
            Save();
            return true;
        }
    }
    public bool UnlinkAssetFromTicket(Guid assetId, int ticketNumber)
    {
        lock (_sync)
        {
            var index = _data.Tickets.FindIndex(x => x.Number == ticketNumber);
            if (index < 0) return false;
            var ticket = _data.Tickets[index];
            if (!ticket.AssetIds.Contains(assetId)) return true;
            var asset = _data.Assets.FirstOrDefault(x => x.Id == assetId);
            var history = ticket.History.ToList();
            history.Add(new("Asset changed", $"Asset {asset?.AssetTag ?? "Unknown"} was unlinked.", DateTime.UtcNow));
            _data.Tickets[index] = ticket with { AssetIds = ticket.AssetIds.Where(x => x != assetId).ToList(), History = history };
            var assetIndex = _data.Assets.FindIndex(x => x.Id == assetId);
            if (assetIndex >= 0)
            {
                var assetHistory = _data.Assets[assetIndex].History.ToList();
                assetHistory.Add(new("Ticket unlinked", $"Unlinked from ticket #{ticket.Number} - {ticket.Title}", DateTime.UtcNow));
                _data.Assets[assetIndex] = _data.Assets[assetIndex] with { History = assetHistory };
            }
            Save();
            return true;
        }
    }
    public IReadOnlyList<AssetAttributeValue> GetAssetAttributeValues(Guid assetId)
    {
        lock (_sync) return _data.AssetAttributeValues.Where(x => x.AssetId == assetId).ToList();
    }
    public string AddAssetAttributeDefinition(string name, string? assetType, string fieldType, string? choices)
    {
        lock (_sync)
        {
            name = (name ?? string.Empty).Trim();
            assetType = string.IsNullOrWhiteSpace(assetType) ? null : assetType.Trim();
            fieldType = (fieldType ?? string.Empty).Trim().ToLowerInvariant();
            choices = NormalizeChoices(choices);
            if (string.IsNullOrWhiteSpace(name)) return "Attribute name is required.";
            if (assetType is not null && !_data.AssetTypes.Contains(assetType, StringComparer.OrdinalIgnoreCase)) return "Select a valid asset type.";
            if (!IsAttributeType(fieldType)) return "Select a valid field type.";
            if (fieldType == "dropdown" && string.IsNullOrWhiteSpace(choices)) return "Dropdown choices are required.";
            if (_data.AssetAttributeDefinitions.Any(x => string.Equals(x.AssetType, assetType, StringComparison.OrdinalIgnoreCase) && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return "That attribute already exists for this asset type.";
            _data.AssetAttributeDefinitions.Add(new(Guid.NewGuid(), name, assetType, fieldType, choices));
            Save();
            return "Custom attribute added.";
        }
    }
    public string AddAssetAttributeDefinition(string name, string? assetType) =>
        AddAssetAttributeDefinition(name, assetType, "single-line", null);
    public string UpdateAssetAttributeDefinition(Guid id, string name, string? assetType, string fieldType, string? choices)
    {
        lock (_sync)
        {
            name = (name ?? string.Empty).Trim();
            assetType = string.IsNullOrWhiteSpace(assetType) ? null : assetType.Trim();
            fieldType = (fieldType ?? string.Empty).Trim().ToLowerInvariant();
            choices = NormalizeChoices(choices);
            var index = _data.AssetAttributeDefinitions.FindIndex(x => x.Id == id);
            if (index < 0) return "Custom attribute was not found.";
            if (string.IsNullOrWhiteSpace(name)) return "Attribute name is required.";
            if (assetType is not null && !_data.AssetTypes.Contains(assetType, StringComparer.OrdinalIgnoreCase)) return "Select a valid asset type.";
            if (!IsAttributeType(fieldType)) return "Select a valid field type.";
            if (fieldType == "dropdown" && string.IsNullOrWhiteSpace(choices)) return "Dropdown choices are required.";
            if (_data.AssetAttributeDefinitions.Any(x => x.Id != id && string.Equals(x.AssetType, assetType, StringComparison.OrdinalIgnoreCase) && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return "That attribute already exists for this asset type.";
            _data.AssetAttributeDefinitions[index] = new(id, name, assetType, fieldType, choices);
            Save();
            return "Custom attribute updated.";
        }
    }
    public string UpdateAssetAttributeDefinition(Guid id, string name, string? assetType) =>
        UpdateAssetAttributeDefinition(id, name, assetType, "single-line", null);
    public string DeleteAssetAttributeDefinition(Guid id)
    {
        lock (_sync)
        {
            if (id == Guid.Empty) return "A valid custom attribute is required.";
            var index = _data.AssetAttributeDefinitions.FindIndex(x => x.Id == id);
            if (index < 0) return "Custom attribute was not found.";
            _data.AssetAttributeDefinitions.RemoveAt(index);
            _data.AssetAttributeValues.RemoveAll(x => x.AttributeDefinitionId == id);
            Save();
            return "Custom attribute deleted.";
        }
    }
    public bool UpdateAssetAttributeValues(Guid assetId, string assetType, IDictionary<Guid, string>? values)
    {
        lock (_sync)
        {
            if (!_data.Assets.Any(x => x.Id == assetId)) return false;
            _data.AssetAttributeValues.RemoveAll(x => x.AssetId == assetId);
            foreach (var definition in _data.AssetAttributeDefinitions.Where(x => x.AssetType is null || x.AssetType.Equals(assetType, StringComparison.OrdinalIgnoreCase)))
            {
                string? raw = null;
                values?.TryGetValue(definition.Id, out raw);
                var value = raw?.Trim() ?? string.Empty;
                if (definition.FieldType == "checkbox")
                    value = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ? "true" : "false";
                else if (definition.FieldType == "dropdown" && !string.IsNullOrEmpty(value) && !GetChoices(definition).Contains(value, StringComparer.Ordinal))
                    continue;
                if (!string.IsNullOrWhiteSpace(value))
                    _data.AssetAttributeValues.Add(new(assetId, definition.Id, value));
            }
            Save();
            return true;
        }
    }

            public string AddTicketAttributeDefinition(string name, string? category, string fieldType, string? choices)
            {
                lock (_sync)
                {
                    name = (name ?? "").Trim(); category = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
                    fieldType = NormalizeAttributeType(fieldType); choices = NormalizeChoices(choices);
                    if (string.IsNullOrWhiteSpace(name) || !IsAttributeType(fieldType)) return "Enter a name and valid field type.";
                    if (fieldType == "dropdown" && string.IsNullOrWhiteSpace(choices)) return "Dropdown choices are required.";
                    if (_data.TicketAttributeDefinitions.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Category, category, StringComparison.OrdinalIgnoreCase))) return "That ticket attribute already exists.";
                    _data.TicketAttributeDefinitions.Add(new(Guid.NewGuid(), name, category, fieldType, choices)); Save(); return "Ticket attribute added.";
                }
            }
            public string UpdateTicketAttributeDefinition(Guid id, string name, string? category, string fieldType, string? choices)
            {
                lock (_sync)
                {
                    var index = _data.TicketAttributeDefinitions.FindIndex(x => x.Id == id);
                    if (index < 0) return "Ticket attribute was not found.";
                    name = (name ?? "").Trim(); category = string.IsNullOrWhiteSpace(category) ? null : category.Trim(); fieldType = NormalizeAttributeType(fieldType); choices = NormalizeChoices(choices);
                    if (string.IsNullOrWhiteSpace(name) || !IsAttributeType(fieldType)) return "Enter a name and valid field type.";
                    if (fieldType == "dropdown" && string.IsNullOrWhiteSpace(choices)) return "Dropdown choices are required.";
                    if (_data.TicketAttributeDefinitions.Any(x => x.Id != id && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Category, category, StringComparison.OrdinalIgnoreCase))) return "That ticket attribute already exists.";
                    _data.TicketAttributeDefinitions[index] = new(id, name, category, fieldType, choices); Save(); return "Ticket attribute updated.";
                }
            }
            public string DeleteTicketAttributeDefinition(Guid id)
            {
                lock (_sync) { if (!_data.TicketAttributeDefinitions.RemoveAll(x => x.Id == id).Equals(1)) return "Ticket attribute was not found."; _data.TicketAttributeValues.RemoveAll(x => x.AttributeDefinitionId == id); Save(); return "Ticket attribute deleted."; }
            }
            public bool UpdateTicketAttributeValues(int number, string category, IDictionary<Guid, string>? values)
            {
                lock (_sync)
                {
                    var ticketIndex = _data.Tickets.FindIndex(x => x.Number == number);
                    if (ticketIndex < 0) return false;
                    var previousValues = _data.TicketAttributeValues.Where(x => x.TicketNumber == number).ToDictionary(x => x.AttributeDefinitionId, x => x.Value);
                    _data.TicketAttributeValues.RemoveAll(x => x.TicketNumber == number);
                    var changes = new List<string>();
                    foreach (var definition in GetTicketAttributes(category))
                    {
                        string? raw = null; values?.TryGetValue(definition.Id, out raw); var value = raw?.Trim() ?? "";
                        if (definition.FieldType == "checkbox") value = value.Equals("true", StringComparison.OrdinalIgnoreCase) ? "true" : "false";
                        if (definition.FieldType == "dropdown" && value.Length > 0 && !GetChoices(definition).Contains(value, StringComparer.Ordinal)) continue;
                        if (value.Length > 0) _data.TicketAttributeValues.Add(new(number, definition.Id, value));

                        var previousValue = previousValues.TryGetValue(definition.Id, out var existing) ? existing : "";
                        if (!string.Equals(previousValue, value, StringComparison.Ordinal))
                            changes.Add($"{definition.Name}: {(previousValue.Length > 0 ? previousValue : "(empty)")} -> {(value.Length > 0 ? value : "(empty)")}");
                    }
                    if (changes.Count > 0)
                    {
                        var history = _data.Tickets[ticketIndex].History.ToList();
                        history.Add(new TicketActivity("Custom attributes changed", string.Join("; ", changes), DateTime.UtcNow));
                        _data.Tickets[ticketIndex] = _data.Tickets[ticketIndex] with { History = history };
                    }
                    Save(); return true;
                }
            }
    public bool UpdateTicket(TicketRecord item)
    {
        lock (_sync)
        {
            var index = _data.Tickets.FindIndex(x => x.Number == item.Number);
            if (index < 0) return false;

            var previous = _data.Tickets[index];
            var history = previous.History.ToList();
            AddTicketActivities(history, previous, item);
            _data.Tickets[index] = item with { History = history };

            var previousAssetIds = previous.AssetIds.ToHashSet();
            var updatedAssetIds = item.AssetIds.ToHashSet();
            if (!previousAssetIds.SetEquals(updatedAssetIds))
            {
                var now = DateTime.UtcNow;
                foreach (var addedAssetId in updatedAssetIds.Except(previousAssetIds))
                {
                    var assetIndex = _data.Assets.FindIndex(x => x.Id == addedAssetId);
                    if (assetIndex < 0) continue;
                    var assetHistory = _data.Assets[assetIndex].History.ToList();
                    assetHistory.Add(new("Ticket linked", $"Linked to ticket #{item.Number} - {item.Title}", now));
                    _data.Assets[assetIndex] = _data.Assets[assetIndex] with { History = assetHistory };
                }
                foreach (var removedAssetId in previousAssetIds.Except(updatedAssetIds))
                {
                    var assetIndex = _data.Assets.FindIndex(x => x.Id == removedAssetId);
                    if (assetIndex < 0) continue;
                    var assetHistory = _data.Assets[assetIndex].History.ToList();
                    assetHistory.Add(new("Ticket unlinked", $"Unlinked from ticket #{item.Number} - {item.Title}", now));
                    _data.Assets[assetIndex] = _data.Assets[assetIndex] with { History = assetHistory };
                }
            }

            Save();
            return true;
        }
    }
    public void UpdateBranding(BrandingSettings item) { lock (_sync) { _data.Branding = item; Save(); } }
    public string ResetFactory(string confirmation)
    {
        lock (_sync)
        {
            if (!string.Equals(confirmation, "DELETE", StringComparison.Ordinal))
                return "Type DELETE exactly to reset the system.";

            _data = new StoreData();
            EnsureOptions(_data.Categories, ["Hardware", "Software", "Account", "Network", "Classroom AV", "Other"]);
            EnsureOptions(_data.Statuses, ["Open", "In Progress", "On Hold", "Closed"]);
            EnsureOptions(_data.Priorities, ["Normal", "Low", "High", "Urgent"]);
            Seed();
            if (File.Exists(_templatePath))
                File.Delete(_templatePath);
            if (File.Exists(_legacyPath))
                File.Delete(_legacyPath);
            return "System reset to factory settings.";
        }
    }
    public void SavePrintTemplate(Stream source)
    {
        using var destination = File.Create(_templatePath);
        source.CopyTo(destination);
    }

    public string RenderPrintTemplate(TicketRecord ticket, UserRecord? requester, TechnicianRecord? technician, IReadOnlyList<AssetRecord> assets)
    {
        if (!File.Exists(_templatePath)) return string.Empty;
        using var document = WordprocessingDocument.Open(_templatePath, false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null) return string.Empty;
        var values = new Dictionary<string, string?>
        {
            ["{{Job.Number}}"] = ticket.Number.ToString(),
            ["{{Job.Title}}"] = ticket.Title,
            ["{{Job.Description}}"] = ticket.Description,
            ["{{Job.Status}}"] = ticket.Status,
            ["{{Job.Priority}}"] = ticket.Priority,
            ["{{Job.Category}}"] = ticket.Category,
            ["{{Job.Created}}"] = ticket.CreatedAt.ToLocalTime().ToString("dd MMM yyyy, HH:mm"),
            ["{{Job.Closed}}"] = ticket.ClosedAt?.ToLocalTime().ToString("dd MMM yyyy, HH:mm") ?? "Not closed",
            ["{{Requester.Name}}"] = requester?.Name ?? "Unknown",
            ["{{Requester.Email}}"] = requester?.Email ?? "",
            ["{{Requester.Department}}"] = requester?.Department ?? "",
            ["{{Requester.Location}}"] = requester?.Location ?? "",
            ["{{Technician.Name}}"] = technician?.Name ?? "Unassigned",
            ["{{Technician.Email}}"] = technician?.Email ?? "",
            ["{{Technician.Team}}"] = technician?.Team ?? "",
            ["{{Asset.Tag}}"] = assets.Count > 0 ? string.Join(", ", assets.Select(x => x.AssetTag)) : "No asset linked",
            ["{{Asset.Type}}"] = assets.Count > 0 ? string.Join(", ", assets.Select(x => x.Type)) : "",
            ["{{Asset.Model}}"] = assets.Count > 0 ? string.Join(", ", assets.Select(x => x.Model)) : "",
            ["{{Asset.Serial}}"] = assets.Count > 0 ? string.Join(", ", assets.Select(x => x.SerialNumber)) : "",
            ["{{Asset.Location}}"] = assets.Count > 0 ? string.Join(", ", assets.Select(x => x.Location)) : ""
        };
        var html = new StringBuilder();
        foreach (var element in body.Elements())
        {
            var content = element.InnerText;
            foreach (var value in values) content = content.Replace(value.Key, value.Value ?? "", StringComparison.OrdinalIgnoreCase);
            var encoded = WebUtility.HtmlEncode(content);
            if (element is Table) html.Append($"<div class=\"template-table\">{encoded}</div>");
            else if (!string.IsNullOrWhiteSpace(encoded)) html.Append($"<p>{encoded}</p>");
        }
        return html.ToString();
    }

    public string? DeleteAsset(Guid id)
    {
        lock (_sync)
        {
            if (_data.Tickets.Any(x => x.AssetIds.Contains(id)))
                return "This asset is linked to a ticket and cannot be deleted.";
            var item = _data.Assets.FirstOrDefault(x => x.Id == id);
            if (item is null) return "Asset was not found.";
            _data.Assets.Remove(item);
            Save();
            return null;
        }
    }

    public string? DeleteUser(Guid id)
    {
        lock (_sync)
        {
            if (_data.Tickets.Any(x => x.RequesterId == id) || _data.Assets.Any(x => x.AssignedUserId == id))
                return "This user is linked to a ticket or asset and cannot be deleted.";
            var item = _data.Users.FirstOrDefault(x => x.Id == id);
            if (item is null) return "User was not found.";
            _data.Users.Remove(item);
            Save();
            return null;
        }
    }

    public string? DeleteTechnician(Guid id)
    {
        lock (_sync)
        {
            if (_data.Tickets.Any(x => x.TechnicianId == id))
                return "This technician is assigned to a ticket and cannot be deleted.";
            var item = _data.Technicians.FirstOrDefault(x => x.Id == id);
            if (item is null) return "Technician was not found.";
            _data.Technicians.Remove(item);
            Save();
            return null;
        }
    }

    public (int Imported, int Skipped) ImportUsers(IEnumerable<UserRecord> users)
    {
        lock (_sync)
        {
            var imported = 0;
            var skipped = 0;
            var existingEmails = _data.Users.Select(x => x.Email).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var user in users)
            {
                var name = (user.Name ?? string.Empty).Trim();
                var email = (user.Email ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email) || !existingEmails.Add(email))
                {
                    skipped++;
                    continue;
                }
                var department = (user.Department ?? string.Empty).Trim();
                var location = (user.Location ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(department) && !_data.Departments.Contains(department, StringComparer.OrdinalIgnoreCase))
                    _data.Departments.Add(department);
                if (!string.IsNullOrWhiteSpace(location) && !_data.Locations.Contains(location, StringComparer.OrdinalIgnoreCase))
                    _data.Locations.Add(location);
                _data.Users.Add(user with { Name = name, Email = email, Department = department, Location = location });
                imported++;
            }
            Save();
            return (imported, skipped);
        }
    }
    public (int Imported, int Skipped) ImportTechnicians(IEnumerable<TechnicianRecord> technicians)
    {
        lock (_sync)
        {
            var imported = 0;
            var skipped = 0;
            var existingEmails = _data.Technicians.Select(x => x.Email).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var technician in technicians)
            {
                var name = (technician.Name ?? string.Empty).Trim();
                var email = (technician.Email ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email) || !existingEmails.Add(email))
                {
                    skipped++;
                    continue;
                }
                var team = (technician.Team ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(team) && !_data.TechnicianTeams.Contains(team, StringComparer.OrdinalIgnoreCase))
                    _data.TechnicianTeams.Add(team);
                _data.Technicians.Add(technician with { Name = name, Email = email, Team = team });
                imported++;
            }
            Save();
            return (imported, skipped);
        }
    }

    private bool Update<T>(T item, List<T> items, Func<T, bool> match)
    {
        lock (_sync)
        {
            var index = items.FindIndex(x => match(x));
            if (index < 0) return false;
            items[index] = item;
            Save();
            return true;
        }
    }

    private static void AddTicketActivities(List<TicketActivity> history, TicketRecord previous, TicketRecord updated)
    {
        var now = DateTime.UtcNow;
        if (previous.Title != updated.Title) history.Add(new("Title changed", $"{previous.Title} -> {updated.Title}", now));
        if (previous.Description != updated.Description) history.Add(new("Description changed", "The ticket description was updated.", now));
        if (previous.Status != updated.Status) history.Add(new("Status changed", $"{previous.Status} -> {updated.Status}", now));
        if (previous.Priority != updated.Priority) history.Add(new("Priority changed", $"{previous.Priority} -> {updated.Priority}", now));
        if (previous.Category != updated.Category) history.Add(new("Category changed", $"{previous.Category} -> {updated.Category}", now));
        if (previous.RequesterId != updated.RequesterId) history.Add(new("Requester changed", "The ticket requester was updated.", now));
        if (previous.TechnicianId != updated.TechnicianId) history.Add(new("Technician changed", updated.TechnicianId.HasValue ? "A technician was assigned." : "The technician assignment was removed.", now));
        if (previous.TeamName != updated.TeamName) history.Add(new("Team changed", updated.TeamName is null ? "The team assignment was removed." : $"Assigned to team {updated.TeamName}.", now));
        if (!previous.AssetIds.ToHashSet().SetEquals(updated.AssetIds)) history.Add(new("Assets changed", updated.AssetIds.Count > 0 ? $"Linked assets updated ({updated.AssetIds.Count} linked)." : "All linked assets were removed.", now));
        if (previous.ClosedAt != updated.ClosedAt && previous.Status == updated.Status) history.Add(new("Closure changed", updated.ClosedAt.HasValue ? "The ticket was closed." : "The ticket was reopened.", now));
        if (previous.SlaId != updated.SlaId) history.Add(new("SLA changed", updated.SlaId.HasValue ? "An SLA was assigned." : "The SLA was removed.", now));
        if (previous.DueDate != updated.DueDate || previous.DueDateOverridden != updated.DueDateOverridden) history.Add(new("Due date changed", updated.DueDate.HasValue ? (updated.DueDateOverridden ? "The due date was manually overridden." : "The due date was recalculated from the SLA.") : "The due date was removed.", now));
    }

    private static void AddAssetActivities(List<AssetActivity> history, AssetRecord previous, AssetRecord updated)
    {
        var now = DateTime.UtcNow;
        if (previous.AssetTag != updated.AssetTag) history.Add(new("Asset tag changed", $"{previous.AssetTag} -> {updated.AssetTag}", now));
        if (previous.Make != updated.Make) history.Add(new("Make changed", $"{previous.Make} -> {updated.Make}", now));
        if (previous.Model != updated.Model) history.Add(new("Model changed", $"{previous.Model} -> {updated.Model}", now));
        if (previous.Type != updated.Type) history.Add(new("Type changed", $"{previous.Type} -> {updated.Type}", now));
        if (previous.SerialNumber != updated.SerialNumber) history.Add(new("Serial number changed", $"{previous.SerialNumber} -> {updated.SerialNumber}", now));
        if (previous.Location != updated.Location) history.Add(new("Location changed", string.IsNullOrWhiteSpace(updated.Location) ? "The location was removed." : $"Moved to {updated.Location}.", now));
        if (previous.AssignedUserId != updated.AssignedUserId) history.Add(new("Assigned user changed", updated.AssignedUserId.HasValue ? "The asset was assigned to a user." : "The user assignment was removed.", now));
        if (previous.SupplierId != updated.SupplierId) history.Add(new("Supplier changed", updated.SupplierId.HasValue ? "A supplier was linked." : "The supplier was removed.", now));
    }

    private StoreData Load()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        using var connection = new SqliteConnection($"Data Source={_path}");
        connection.Open();
        EnsureSchema(connection);

        var version = ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'SchemaVersion';");
        if (version is null)
        {
            StoreData? migrated = null;
            var payload = TableExists(connection, "Store") ? ExecuteScalar(connection, "SELECT Payload FROM Store WHERE Id = 1;") : null;
            if (payload is string json && !string.IsNullOrWhiteSpace(json))
            {
                try { migrated = JsonSerializer.Deserialize<StoreData>(json); } catch (JsonException) { }
            }
            if (migrated is null && File.Exists(_legacyPath))
            {
                try { migrated = JsonSerializer.Deserialize<StoreData>(File.ReadAllText(_legacyPath)); } catch (JsonException) { }
            }
            if (migrated is not null)
            {
                foreach (var team in migrated.Technicians.Select(x => x.Team).Where(x => !string.IsNullOrWhiteSpace(x)))
                    if (!migrated.TechnicianTeams.Contains(team, StringComparer.OrdinalIgnoreCase)) migrated.TechnicianTeams.Add(team);
                foreach (var department in migrated.Users.Select(x => x.Department).Where(x => !string.IsNullOrWhiteSpace(x)))
                    if (!migrated.Departments.Contains(department, StringComparer.OrdinalIgnoreCase)) migrated.Departments.Add(department);
                using var transaction = connection.BeginTransaction();
                WriteData(connection, transaction, migrated);
                SetMetadata(connection, transaction, "SchemaVersion", "8");
                DropLegacyStore(connection, transaction);
                transaction.Commit();
                return migrated;
            }
            using var marker = connection.CreateCommand();
            marker.CommandText = "INSERT OR REPLACE INTO Metadata (Key, Value) VALUES ('SchemaVersion', '5');";
            marker.ExecuteNonQuery();
            using var drop = connection.CreateCommand();
            drop.CommandText = "DROP TABLE IF EXISTS Store;";
            drop.ExecuteNonQuery();
        }
        else if (TableExists(connection, "Store"))
        {
            using var drop = connection.CreateCommand();
            drop.CommandText = "DROP TABLE IF EXISTS Store;";
            drop.ExecuteNonQuery();
        }
        return ReadData(connection);
    }

    private void Save()
    {
        using var connection = new SqliteConnection($"Data Source={_path}");
        connection.Open();
        using var transaction = connection.BeginTransaction();
        WriteData(connection, transaction, _data);
        SetMetadata(connection, transaction, "SchemaVersion", "5");
        transaction.Commit();
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;
            CREATE TABLE IF NOT EXISTS Metadata (Key TEXT PRIMARY KEY, Value TEXT NULL);
            CREATE TABLE IF NOT EXISTS Users (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, Email TEXT NOT NULL, Department TEXT NULL, Location TEXT NULL);
            CREATE TABLE IF NOT EXISTS TechnicianTeams (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS Technicians (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, Email TEXT NOT NULL, Team TEXT NULL,
                FOREIGN KEY (Team) REFERENCES TechnicianTeams(Name) ON UPDATE CASCADE);
            CREATE TABLE IF NOT EXISTS Departments (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS Locations (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS AssetTypes (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS AssetMakes (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS AssetModels (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS Categories (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS Statuses (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS StatusDescriptions (Status TEXT PRIMARY KEY, Description TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS RequireCloseMessagePriorities (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS RequireCloseMessageCategories (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS Priorities (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS Slas (Id TEXT PRIMARY KEY, Name TEXT NOT NULL UNIQUE, Duration INTEGER NOT NULL, DurationUnit TEXT NOT NULL, Priority TEXT NULL UNIQUE);
            CREATE TABLE IF NOT EXISTS Suppliers (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, ContactName TEXT NOT NULL DEFAULT '', Email TEXT NOT NULL DEFAULT '', Phone TEXT NOT NULL DEFAULT '', AddressLine1 TEXT NOT NULL DEFAULT '', AddressLine2 TEXT NOT NULL DEFAULT '', City TEXT NOT NULL DEFAULT '', StateRegion TEXT NOT NULL DEFAULT '', PostalCode TEXT NOT NULL DEFAULT '', Country TEXT NOT NULL DEFAULT '', Website TEXT NOT NULL DEFAULT '', Notes TEXT NOT NULL DEFAULT '', CreatedAt TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS Parts (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, Sku TEXT NOT NULL DEFAULT '', Category TEXT NOT NULL DEFAULT '', QuantityOnHand INTEGER NOT NULL DEFAULT 0, CreatedAt TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS TicketParts (TicketNumber INTEGER NOT NULL, PartId TEXT NOT NULL, Quantity INTEGER NOT NULL,
                PRIMARY KEY (TicketNumber, PartId),
                FOREIGN KEY (TicketNumber) REFERENCES Tickets(Number) ON DELETE CASCADE,
                FOREIGN KEY (PartId) REFERENCES Parts(Id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS Assets (Id TEXT PRIMARY KEY, AssetTag TEXT NOT NULL, Make TEXT NOT NULL DEFAULT '', Type TEXT NOT NULL, Model TEXT NOT NULL,
                SerialNumber TEXT NOT NULL, Location TEXT NOT NULL, AssignedUserId TEXT NULL, SupplierId TEXT NULL,
                FOREIGN KEY (AssignedUserId) REFERENCES Users(Id) ON DELETE SET NULL);
            CREATE TABLE IF NOT EXISTS Tickets (Number INTEGER PRIMARY KEY, Title TEXT NOT NULL, Description TEXT NOT NULL,
                RequesterId TEXT NOT NULL, AssetId TEXT NULL, TechnicianId TEXT NULL, Priority TEXT NOT NULL, Status TEXT NOT NULL,
                Category TEXT NOT NULL, CreatedAt TEXT NOT NULL, ClosedAt TEXT NULL, SlaId TEXT NULL, DueDate TEXT NULL, DueDateOverridden INTEGER NOT NULL DEFAULT 0, SlaOverridden INTEGER NOT NULL DEFAULT 0, TeamName TEXT NULL,
                FOREIGN KEY (RequesterId) REFERENCES Users(Id), FOREIGN KEY (AssetId) REFERENCES Assets(Id) ON DELETE SET NULL,
                FOREIGN KEY (TechnicianId) REFERENCES Technicians(Id) ON DELETE SET NULL);
            CREATE TABLE IF NOT EXISTS TicketComments (Id INTEGER PRIMARY KEY AUTOINCREMENT, TicketNumber INTEGER NOT NULL,
                Text TEXT NOT NULL, CreatedAt TEXT NOT NULL, FOREIGN KEY (TicketNumber) REFERENCES Tickets(Number) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS TicketActivities (Id INTEGER PRIMARY KEY AUTOINCREMENT, TicketNumber INTEGER NOT NULL,
                Action TEXT NOT NULL, Details TEXT NOT NULL, CreatedAt TEXT NOT NULL, FOREIGN KEY (TicketNumber) REFERENCES Tickets(Number) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS AssetComments (Id INTEGER PRIMARY KEY AUTOINCREMENT, AssetId TEXT NOT NULL,
                Text TEXT NOT NULL, CreatedAt TEXT NOT NULL, FOREIGN KEY (AssetId) REFERENCES Assets(Id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS AssetActivities (Id INTEGER PRIMARY KEY AUTOINCREMENT, AssetId TEXT NOT NULL,
                Action TEXT NOT NULL, Details TEXT NOT NULL, CreatedAt TEXT NOT NULL, FOREIGN KEY (AssetId) REFERENCES Assets(Id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS BrandingSettings (Id INTEGER PRIMARY KEY CHECK (Id = 1), BrandName TEXT NOT NULL,
                DashboardEyebrow TEXT NOT NULL, DashboardTitle TEXT NOT NULL, DashboardDescription TEXT NOT NULL,
                PrimaryColor TEXT NOT NULL, AccentColor TEXT NOT NULL, BackgroundColor TEXT NOT NULL, DarkMode INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS AssetAttributeDefinitions (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, AssetType TEXT NULL,
                FieldType TEXT NOT NULL DEFAULT 'single-line', Choices TEXT NOT NULL DEFAULT '');
            CREATE TABLE IF NOT EXISTS AssetAttributeValues (AssetId TEXT NOT NULL, AttributeDefinitionId TEXT NOT NULL, Value TEXT NOT NULL,
                PRIMARY KEY (AssetId, AttributeDefinitionId),
                FOREIGN KEY (AssetId) REFERENCES Assets(Id) ON DELETE CASCADE,
                FOREIGN KEY (AttributeDefinitionId) REFERENCES AssetAttributeDefinitions(Id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS TicketAttributeDefinitions (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, Category TEXT NULL, FieldType TEXT NOT NULL DEFAULT 'single-line', Choices TEXT NOT NULL DEFAULT '');
            CREATE TABLE IF NOT EXISTS TicketAttributeValues (TicketNumber INTEGER NOT NULL, AttributeDefinitionId TEXT NOT NULL, Value TEXT NOT NULL,
                PRIMARY KEY (TicketNumber, AttributeDefinitionId), FOREIGN KEY (TicketNumber) REFERENCES Tickets(Number) ON DELETE CASCADE,
                FOREIGN KEY (AttributeDefinitionId) REFERENCES TicketAttributeDefinitions(Id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS TicketAssets (TicketNumber INTEGER NOT NULL, AssetId TEXT NOT NULL,
                PRIMARY KEY (TicketNumber, AssetId),
                FOREIGN KEY (TicketNumber) REFERENCES Tickets(Number) ON DELETE CASCADE,
                FOREIGN KEY (AssetId) REFERENCES Assets(Id) ON DELETE CASCADE);
            """;
        command.ExecuteNonQuery();
        using var migration = connection.CreateCommand();
        migration.CommandText = "ALTER TABLE Assets ADD COLUMN Make TEXT NOT NULL DEFAULT '';";
        try { migration.ExecuteNonQuery(); } catch (SqliteException ex) when (ex.SqliteErrorCode == 1) { }
        using var supplierMigration = connection.CreateCommand();
        supplierMigration.CommandText = "ALTER TABLE Assets ADD COLUMN SupplierId TEXT NULL;";
        try { supplierMigration.ExecuteNonQuery(); } catch (SqliteException ex) when (ex.SqliteErrorCode == 1) { }
        using var attributeMigration = connection.CreateCommand();
        attributeMigration.CommandText = "ALTER TABLE AssetAttributeDefinitions ADD COLUMN FieldType TEXT NOT NULL DEFAULT 'single-line';";
        try { attributeMigration.ExecuteNonQuery(); } catch (SqliteException ex) when (ex.SqliteErrorCode == 1) { }
        using var choicesMigration = connection.CreateCommand();
        choicesMigration.CommandText = "ALTER TABLE AssetAttributeDefinitions ADD COLUMN Choices TEXT NOT NULL DEFAULT '';";
        try { choicesMigration.ExecuteNonQuery(); } catch (SqliteException ex) when (ex.SqliteErrorCode == 1) { }
        using var slaMigration = connection.CreateCommand();
        slaMigration.CommandText = "ALTER TABLE Tickets ADD COLUMN SlaId TEXT NULL; ALTER TABLE Tickets ADD COLUMN DueDate TEXT NULL; ALTER TABLE Tickets ADD COLUMN DueDateOverridden INTEGER NOT NULL DEFAULT 0; ALTER TABLE Tickets ADD COLUMN SlaOverridden INTEGER NOT NULL DEFAULT 0; ALTER TABLE Tickets ADD COLUMN TeamName TEXT NULL;";
        try { slaMigration.ExecuteNonQuery(); } catch (SqliteException ex) when (ex.SqliteErrorCode == 1) { }
        foreach (var sql in new[] { "ALTER TABLE Slas ADD COLUMN Priority TEXT NULL;", "ALTER TABLE TicketAttributeDefinitions ADD COLUMN FieldType TEXT NOT NULL DEFAULT 'single-line';", "ALTER TABLE TicketAttributeDefinitions ADD COLUMN Choices TEXT NOT NULL DEFAULT '';" })
        { using var m = connection.CreateCommand(); m.CommandText = sql; try { m.ExecuteNonQuery(); } catch (SqliteException ex) when (ex.SqliteErrorCode == 1) { } }
        MigrateAssetAttributeTypeToNullable(connection);
    }

    private static void MigrateAssetAttributeTypeToNullable(SqliteConnection connection)
    {
        var assetTypeIsNotNull = false;
        using (var info = connection.CreateCommand())
        {
            info.CommandText = "PRAGMA table_info(AssetAttributeDefinitions);";
            using var reader = info.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), "AssetType", StringComparison.OrdinalIgnoreCase))
                    assetTypeIsNotNull = reader.GetInt32(3) == 1;
            }
        }
        if (!assetTypeIsNotNull) return;
        using var migrate = connection.CreateCommand();
        migrate.CommandText = """
            ALTER TABLE AssetAttributeDefinitions RENAME TO AssetAttributeDefinitions_old;
            CREATE TABLE AssetAttributeDefinitions (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, AssetType TEXT NULL, FieldType TEXT NOT NULL DEFAULT 'single-line', Choices TEXT NOT NULL DEFAULT '');
            INSERT INTO AssetAttributeDefinitions (Id, Name, AssetType, FieldType, Choices) SELECT Id, Name, AssetType, FieldType, Choices FROM AssetAttributeDefinitions_old;
            DROP TABLE AssetAttributeDefinitions_old;
            """;
        migrate.ExecuteNonQuery();
    }

    private static object? ExecuteScalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static bool TableExists(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", table);
        return command.ExecuteScalar() is not null;
    }

    private static void DropLegacyStore(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DROP TABLE IF EXISTS Store;";
        command.ExecuteNonQuery();
    }

    private static void SetMetadata(SqliteConnection connection, SqliteTransaction transaction, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT OR REPLACE INTO Metadata (Key, Value) VALUES ($key, $value);";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private static string Iso(DateTime value) => value.ToUniversalTime().ToString("O");
    private static DateTime Date(SqliteDataReader reader, int index) => DateTime.Parse(reader.GetString(index), null, System.Globalization.DateTimeStyles.RoundtripKind);
    private static string? NullableString(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
    private static Guid? NullableGuid(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : Guid.Parse(reader.GetString(index));

    private static StoreData ReadData(SqliteConnection connection)
    {
        var data = new StoreData();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, Name, Email, Department, Location FROM Users;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.Users.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), NullableString(reader, 3) ?? "", NullableString(reader, 4) ?? ""));
        }
        ReadStrings(connection, "TechnicianTeams", data.TechnicianTeams);
        ReadStrings(connection, "Departments", data.Departments);
        ReadStrings(connection, "Locations", data.Locations);
        ReadStrings(connection, "AssetTypes", data.AssetTypes);
        ReadStrings(connection, "AssetMakes", data.AssetMakes);
        ReadStrings(connection, "AssetModels", data.AssetModels);
        ReadStrings(connection, "Categories", data.Categories);
        ReadStrings(connection, "Statuses", data.Statuses);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Status, Description FROM StatusDescriptions;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.StatusDescriptions[reader.GetString(0)] = reader.GetString(1);
        }
        ReadStrings(connection, "Priorities", data.Priorities);
        ReadStrings(connection, "RequireCloseMessagePriorities", data.RequireCloseMessagePriorities);
        ReadStrings(connection, "RequireCloseMessageCategories", data.RequireCloseMessageCategories);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, Name, Duration, DurationUnit, Priority FROM Slas ORDER BY rowid;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.Slas.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetInt32(2), NormalizeDurationUnit(reader.GetString(3)), NullableString(reader, 4)));
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, Name, AssetType, FieldType, Choices FROM AssetAttributeDefinitions ORDER BY rowid;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.AssetAttributeDefinitions.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), NullableString(reader, 2), reader.GetString(3), reader.GetString(4)));
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, Name, Category, FieldType, Choices FROM TicketAttributeDefinitions ORDER BY rowid;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.TicketAttributeDefinitions.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), NullableString(reader, 2), reader.GetString(3), reader.GetString(4)));
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT TicketNumber, AttributeDefinitionId, Value FROM TicketAttributeValues;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.TicketAttributeValues.Add(new(reader.GetInt32(0), Guid.Parse(reader.GetString(1)), reader.GetString(2)));
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, Name, Email, Team FROM Technicians;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.Technicians.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), NullableString(reader, 3) ?? ""));
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT AssetId, AttributeDefinitionId, Value FROM AssetAttributeValues;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.AssetAttributeValues.Add(new(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), reader.GetString(2)));
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, Name, ContactName, Email, Phone, AddressLine1, AddressLine2, City, StateRegion, PostalCode, Country, Website, Notes, CreatedAt FROM Suppliers;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.Suppliers.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.GetString(10), reader.GetString(11), reader.GetString(12), Date(reader, 13)));
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, Name, Sku, Category, QuantityOnHand, CreatedAt FROM Parts;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.Parts.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), Date(reader, 5)));
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT TicketNumber, PartId, Quantity FROM TicketParts;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.TicketParts.Add(new(reader.GetInt32(0), Guid.Parse(reader.GetString(1)), reader.GetInt32(2)));
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, AssetTag, Make, Type, Model, SerialNumber, Location, AssignedUserId FROM Assets;";
            command.CommandText = "SELECT Id, AssetTag, Make, Type, Model, SerialNumber, Location, AssignedUserId, SupplierId FROM Assets;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) data.Assets.Add(new(Guid.Parse(reader.GetString(0)), NullableString(reader, 1) ?? "", NullableString(reader, 2) ?? "", NullableString(reader, 3) ?? "", NullableString(reader, 4) ?? "", NullableString(reader, 5) ?? "", NullableString(reader, 6) ?? "", NullableGuid(reader, 7), NullableGuid(reader, 8)));
        }
        foreach (var asset in data.Assets) ReadAssetChildren(connection, asset);
        var ticketAssetMap = new Dictionary<int, List<Guid>>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT TicketNumber, AssetId FROM TicketAssets ORDER BY rowid;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var number = reader.GetInt32(0);
                var assetId = Guid.Parse(reader.GetString(1));
                if (!ticketAssetMap.TryGetValue(number, out var list)) ticketAssetMap[number] = list = [];
                list.Add(assetId);
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Number, Title, Description, RequesterId, AssetId, TechnicianId, Priority, Status, Category, CreatedAt, ClosedAt, SlaId, DueDate, DueDateOverridden, SlaOverridden, TeamName FROM Tickets;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var number = reader.GetInt32(0);
                var legacyAssetId = NullableGuid(reader, 4);
                List<Guid> assetIds;
                if (ticketAssetMap.TryGetValue(number, out var mapped)) assetIds = mapped;
                else if (legacyAssetId.HasValue) assetIds = new List<Guid> { legacyAssetId.Value };
                else assetIds = new List<Guid>();
                var ticket = new TicketRecord(number, reader.GetString(1), reader.GetString(2), Guid.Parse(reader.GetString(3)),
                    assetIds, NullableGuid(reader, 5), reader.GetString(6), reader.GetString(7), reader.GetString(8), Date(reader, 9),
                    reader.IsDBNull(10) ? null : Date(reader, 10), NullableGuid(reader, 11), reader.IsDBNull(12) ? null : Date(reader, 12), !reader.IsDBNull(13) && reader.GetInt32(13) != 0, !reader.IsDBNull(14) && reader.GetInt32(14) != 0, NullableString(reader, 15));
                data.Tickets.Add(ticket);
            }
        }
        foreach (var ticket in data.Tickets) ReadTicketChildren(connection, ticket);
        data.LastTicketNumber = Convert.ToInt32(ExecuteScalar(connection, "SELECT COALESCE(MAX(Number), 1000) FROM Tickets;"));
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT BrandName, DashboardEyebrow, DashboardTitle, DashboardDescription, PrimaryColor, AccentColor, BackgroundColor, DarkMode FROM BrandingSettings WHERE Id = 1;";
            using var reader = command.ExecuteReader();
            if (reader.Read()) data.Branding = new() { BrandName = reader.GetString(0), DashboardEyebrow = reader.GetString(1), DashboardTitle = reader.GetString(2), DashboardDescription = reader.GetString(3), PrimaryColor = reader.GetString(4), AccentColor = reader.GetString(5), BackgroundColor = reader.GetString(6), DarkMode = reader.GetInt32(7) != 0 };
        }
        return data;
    }

    private static void ReadStrings(SqliteConnection connection, string table, List<string> target)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT Name FROM {table} ORDER BY rowid;";
        using var reader = command.ExecuteReader();
        while (reader.Read()) target.Add(reader.GetString(0));
    }

    private static void ReadTicketChildren(SqliteConnection connection, TicketRecord ticket)
    {
        using var comments = connection.CreateCommand();
        comments.CommandText = "SELECT Text, CreatedAt FROM TicketComments WHERE TicketNumber = $number ORDER BY Id;";
        comments.Parameters.AddWithValue("$number", ticket.Number);
        using var commentReader = comments.ExecuteReader();
        while (commentReader.Read()) ticket.Comments.Add(new(commentReader.GetString(0), Date(commentReader, 1)));
        using var activities = connection.CreateCommand();
        activities.CommandText = "SELECT Action, Details, CreatedAt FROM TicketActivities WHERE TicketNumber = $number ORDER BY Id;";
        activities.Parameters.AddWithValue("$number", ticket.Number);
        using var activityReader = activities.ExecuteReader();
        while (activityReader.Read()) ticket.History.Add(new(activityReader.GetString(0), activityReader.GetString(1), Date(activityReader, 2)));
    }

    private static void ReadAssetChildren(SqliteConnection connection, AssetRecord asset)
    {
        using var comments = connection.CreateCommand();
        comments.CommandText = "SELECT Text, CreatedAt FROM AssetComments WHERE AssetId = $id ORDER BY Id;";
        comments.Parameters.AddWithValue("$id", asset.Id.ToString());
        using var commentReader = comments.ExecuteReader();
        while (commentReader.Read()) asset.Comments.Add(new(commentReader.GetString(0), Date(commentReader, 1)));
        using var activities = connection.CreateCommand();
        activities.CommandText = "SELECT Action, Details, CreatedAt FROM AssetActivities WHERE AssetId = $id ORDER BY Id;";
        activities.Parameters.AddWithValue("$id", asset.Id.ToString());
        using var activityReader = activities.ExecuteReader();
        while (activityReader.Read()) asset.History.Add(new(activityReader.GetString(0), activityReader.GetString(1), Date(activityReader, 2)));
    }

    private static void WriteData(SqliteConnection connection, SqliteTransaction transaction, StoreData data)
    {
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM TicketActivities; DELETE FROM TicketComments; DELETE FROM TicketAttributeValues; DELETE FROM TicketAssets; DELETE FROM TicketParts; DELETE FROM Parts; DELETE FROM Tickets; DELETE FROM TicketAttributeDefinitions; DELETE FROM AssetComments; DELETE FROM AssetActivities; DELETE FROM AssetAttributeValues; DELETE FROM Assets; DELETE FROM Suppliers; DELETE FROM Technicians; DELETE FROM Users; DELETE FROM AssetAttributeDefinitions; DELETE FROM Slas; DELETE FROM TechnicianTeams; DELETE FROM Departments; DELETE FROM Locations; DELETE FROM AssetTypes; DELETE FROM AssetMakes; DELETE FROM AssetModels; DELETE FROM Categories; DELETE FROM Statuses; DELETE FROM StatusDescriptions; DELETE FROM Priorities; DELETE FROM RequireCloseMessagePriorities; DELETE FROM RequireCloseMessageCategories; DELETE FROM BrandingSettings;";
            command.ExecuteNonQuery();
        }
        InsertStrings(connection, transaction, "TechnicianTeams", data.TechnicianTeams);
        InsertStrings(connection, transaction, "Departments", data.Departments);
        InsertStrings(connection, transaction, "Locations", data.Locations);
        InsertStrings(connection, transaction, "AssetTypes", data.AssetTypes);
        InsertStrings(connection, transaction, "AssetMakes", data.AssetMakes);
        InsertStrings(connection, transaction, "AssetModels", data.AssetModels);
        InsertStrings(connection, transaction, "Categories", data.Categories);
        InsertStrings(connection, transaction, "Statuses", data.Statuses);
        foreach (var pair in data.StatusDescriptions.Where(x => data.Statuses.Contains(x.Key, StringComparer.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(x.Value)))
            Execute(connection, transaction, "INSERT INTO StatusDescriptions (Status, Description) VALUES ($status,$description);", ("$status", pair.Key), ("$description", pair.Value));
        InsertStrings(connection, transaction, "Priorities", data.Priorities);
        InsertStrings(connection, transaction, "RequireCloseMessagePriorities", data.RequireCloseMessagePriorities);
        InsertStrings(connection, transaction, "RequireCloseMessageCategories", data.RequireCloseMessageCategories);
        foreach (var sla in data.Slas)
            Execute(connection, transaction, "INSERT INTO Slas (Id, Name, Duration, DurationUnit, Priority) VALUES ($id,$name,$duration,$unit,$priority);", ("$id", sla.Id.ToString()), ("$name", sla.Name), ("$duration", sla.Duration), ("$unit", NormalizeDurationUnit(sla.DurationUnit)), ("$priority", sla.Priority));
        foreach (var item in data.Users)
            Execute(connection, transaction, "INSERT INTO Users (Id, Name, Email, Department, Location) VALUES ($id,$name,$email,$department,$location);", ("$id", item.Id.ToString()), ("$name", item.Name), ("$email", item.Email), ("$department", item.Department), ("$location", item.Location));
        foreach (var item in data.Technicians)
            Execute(connection, transaction, "INSERT INTO Technicians (Id, Name, Email, Team) VALUES ($id,$name,$email,$team);", ("$id", item.Id.ToString()), ("$name", item.Name), ("$email", item.Email), ("$team", item.Team));
        foreach (var item in data.Suppliers)
            Execute(connection, transaction, "INSERT INTO Suppliers (Id, Name, ContactName, Email, Phone, AddressLine1, AddressLine2, City, StateRegion, PostalCode, Country, Website, Notes, CreatedAt) VALUES ($id,$name,$contact,$email,$phone,$a1,$a2,$city,$state,$postal,$country,$website,$notes,$created);", ("$id", item.Id.ToString()), ("$name", item.Name), ("$contact", item.ContactName), ("$email", item.Email), ("$phone", item.Phone), ("$a1", item.AddressLine1), ("$a2", item.AddressLine2), ("$city", item.City), ("$state", item.StateRegion), ("$postal", item.PostalCode), ("$country", item.Country), ("$website", item.Website), ("$notes", item.Notes), ("$created", Iso(item.CreatedAt)));
        foreach (var item in data.Parts)
            Execute(connection, transaction, "INSERT INTO Parts (Id, Name, Sku, Category, QuantityOnHand, CreatedAt) VALUES ($id,$name,$sku,$category,$quantity,$created);", ("$id", item.Id.ToString()), ("$name", item.Name), ("$sku", item.Sku), ("$category", item.Category), ("$quantity", item.QuantityOnHand), ("$created", Iso(item.CreatedAt)));
        foreach (var item in data.Assets)
        {
            Execute(connection, transaction, "INSERT INTO Assets (Id, AssetTag, Make, Type, Model, SerialNumber, Location, AssignedUserId, SupplierId) VALUES ($id,$tag,$make,$type,$model,$serial,$location,$user,$supplier);", ("$id", item.Id.ToString()), ("$tag", item.AssetTag), ("$make", item.Make), ("$type", item.Type), ("$model", item.Model), ("$serial", item.SerialNumber), ("$location", item.Location), ("$user", item.AssignedUserId?.ToString()), ("$supplier", item.SupplierId?.ToString()));
            foreach (var comment in item.Comments)
                Execute(connection, transaction, "INSERT INTO AssetComments (AssetId, Text, CreatedAt) VALUES ($id,$text,$created);", ("$id", item.Id.ToString()), ("$text", comment.Text), ("$created", Iso(comment.CreatedAt)));
            foreach (var activity in item.History)
                Execute(connection, transaction, "INSERT INTO AssetActivities (AssetId, Action, Details, CreatedAt) VALUES ($id,$action,$details,$created);", ("$id", item.Id.ToString()), ("$action", activity.Action), ("$details", activity.Details), ("$created", Iso(activity.CreatedAt)));
        }
        foreach (var item in data.AssetAttributeDefinitions)
            Execute(connection, transaction, "INSERT INTO AssetAttributeDefinitions (Id, Name, AssetType, FieldType, Choices) VALUES ($id,$name,$type,$fieldType,$choices);", ("$id", item.Id.ToString()), ("$name", item.Name), ("$type", item.AssetType), ("$fieldType", NormalizeAttributeType(item.FieldType)), ("$choices", NormalizeChoices(item.Choices)));
        foreach (var item in data.AssetAttributeValues)
            if (data.Assets.Any(x => x.Id == item.AssetId) && data.AssetAttributeDefinitions.Any(x => x.Id == item.AttributeDefinitionId))
                Execute(connection, transaction, "INSERT INTO AssetAttributeValues (AssetId, AttributeDefinitionId, Value) VALUES ($asset,$definition,$value);", ("$asset", item.AssetId.ToString()), ("$definition", item.AttributeDefinitionId.ToString()), ("$value", item.Value));
        foreach (var item in data.TicketAttributeDefinitions)
            Execute(connection, transaction, "INSERT INTO TicketAttributeDefinitions (Id, Name, Category, FieldType, Choices) VALUES ($id,$name,$category,$fieldType,$choices);", ("$id", item.Id.ToString()), ("$name", item.Name), ("$category", item.Category), ("$fieldType", NormalizeAttributeType(item.FieldType)), ("$choices", NormalizeChoices(item.Choices)));
        foreach (var item in data.Tickets)
        {
            Execute(connection, transaction, "INSERT INTO Tickets (Number, Title, Description, RequesterId, TechnicianId, Priority, Status, Category, CreatedAt, ClosedAt, SlaId, DueDate, DueDateOverridden, SlaOverridden, TeamName) VALUES ($number,$title,$description,$requester,$technician,$priority,$status,$category,$created,$closed,$sla,$due,$overridden,$slaoverridden,$team);",
                ("$number", item.Number), ("$title", item.Title), ("$description", item.Description), ("$requester", item.RequesterId.ToString()), ("$technician", item.TechnicianId?.ToString()), ("$priority", item.Priority), ("$status", item.Status), ("$category", item.Category), ("$created", Iso(item.CreatedAt)), ("$closed", item.ClosedAt.HasValue ? Iso(item.ClosedAt.Value) : null), ("$sla", item.SlaId?.ToString()), ("$due", item.DueDate.HasValue ? Iso(item.DueDate.Value) : null), ("$overridden", item.DueDateOverridden ? 1 : 0), ("$slaoverridden", item.SlaOverridden ? 1 : 0), ("$team", item.TeamName));
            foreach (var assetId in item.AssetIds.Distinct())
                if (data.Assets.Any(x => x.Id == assetId))
                    Execute(connection, transaction, "INSERT INTO TicketAssets (TicketNumber, AssetId) VALUES ($number,$asset);", ("$number", item.Number), ("$asset", assetId.ToString()));
            foreach (var part in data.TicketParts.Where(x => x.TicketNumber == item.Number))
                if (data.Parts.Any(x => x.Id == part.PartId))
                    Execute(connection, transaction, "INSERT INTO TicketParts (TicketNumber, PartId, Quantity) VALUES ($number,$part,$quantity);", ("$number", item.Number), ("$part", part.PartId.ToString()), ("$quantity", part.Quantity));
            foreach (var value in data.TicketAttributeValues.Where(x => x.TicketNumber == item.Number))
                if (data.TicketAttributeDefinitions.Any(x => x.Id == value.AttributeDefinitionId))
                    Execute(connection, transaction, "INSERT INTO TicketAttributeValues (TicketNumber, AttributeDefinitionId, Value) VALUES ($number,$definition,$value);", ("$number", value.TicketNumber), ("$definition", value.AttributeDefinitionId.ToString()), ("$value", value.Value));
            foreach (var comment in item.Comments)
                Execute(connection, transaction, "INSERT INTO TicketComments (TicketNumber, Text, CreatedAt) VALUES ($number,$text,$created);", ("$number", item.Number), ("$text", comment.Text), ("$created", Iso(comment.CreatedAt)));
            foreach (var activity in item.History)
                Execute(connection, transaction, "INSERT INTO TicketActivities (TicketNumber, Action, Details, CreatedAt) VALUES ($number,$action,$details,$created);", ("$number", item.Number), ("$action", activity.Action), ("$details", activity.Details), ("$created", Iso(activity.CreatedAt)));
        }
        var branding = data.Branding ?? new BrandingSettings();
        Execute(connection, transaction, "INSERT INTO BrandingSettings (Id, BrandName, DashboardEyebrow, DashboardTitle, DashboardDescription, PrimaryColor, AccentColor, BackgroundColor, DarkMode) VALUES (1,$name,$eyebrow,$title,$description,$primary,$accent,$background,$dark);",
            ("$name", branding.BrandName), ("$eyebrow", branding.DashboardEyebrow), ("$title", branding.DashboardTitle), ("$description", branding.DashboardDescription), ("$primary", branding.PrimaryColor), ("$accent", branding.AccentColor), ("$background", branding.BackgroundColor), ("$dark", branding.DarkMode ? 1 : 0));
    }

    private static void InsertStrings(SqliteConnection connection, SqliteTransaction transaction, string table, IEnumerable<string> values)
    {
        foreach (var value in values.Distinct(StringComparer.OrdinalIgnoreCase))
            Execute(connection, transaction, $"INSERT INTO {table} (Name) VALUES ($value);", ("$value", value));
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object? Value)[] values)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var value in values) command.Parameters.AddWithValue(value.Name, value.Value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private void Seed()
    {
        var technician = new TechnicianRecord(Guid.NewGuid(), "Alex Morgan", "alex.morgan@school.example", "IT Support");
        var user = new UserRecord(Guid.NewGuid(), "Jordan Lee", "jordan.lee@school.example", "Science", "Main Campus");
        _data.Technicians.Add(technician);
        _data.TechnicianTeams.Add(technician.Team);
        _data.Users.Add(user);
        _data.Assets.Add(new AssetRecord(Guid.NewGuid(), "LT-1001", "Dell", "Dell Latitude 5440", "Laptop", "SN-DEMO-001", "Main Campus", user.Id));
        _data.Locations.Add(user.Location);
        _data.AssetTypes.Add("Laptop");
        _data.AssetMakes.Add("Dell");
        _data.AssetModels.Add("Dell Latitude 5440");
        Save();
    }

    public sealed class StoreData
    {
        public List<UserRecord> Users { get; set; } = [];
        public List<TechnicianRecord> Technicians { get; set; } = [];
        public List<string> TechnicianTeams { get; set; } = [];
        public List<string> Departments { get; set; } = [];
        public List<string> Locations { get; set; } = [];
        public List<string> AssetTypes { get; set; } = [];
        public List<string> AssetMakes { get; set; } = [];
        public List<string> AssetModels { get; set; } = [];
        public List<string> Categories { get; set; } = [];
        public List<string> Statuses { get; set; } = [];
        public Dictionary<string, string> StatusDescriptions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> ClosureCommentPriorities { get; set; } = [];
        public List<string> ClosureCommentCategories { get; set; } = [];
        public List<string> Priorities { get; set; } = [];
        public List<string> RequireCloseMessagePriorities { get; set; } = [];
        public List<string> RequireCloseMessageCategories { get; set; } = [];
        public List<AssetRecord> Assets { get; set; } = [];
        public List<SupplierRecord> Suppliers { get; set; } = [];
        public List<PartRecord> Parts { get; set; } = [];
        public List<TicketPartAssignment> TicketParts { get; set; } = [];
        public List<AssetAttributeDefinition> AssetAttributeDefinitions { get; set; } = [];
        public List<AssetAttributeValue> AssetAttributeValues { get; set; } = [];
        public List<SlaDefinition> Slas { get; set; } = [];
        public List<TicketAttributeDefinition> TicketAttributeDefinitions { get; set; } = [];
        public List<TicketAttributeValue> TicketAttributeValues { get; set; } = [];
        public List<TicketRecord> Tickets { get; set; } = [];
        public int LastTicketNumber { get; set; } = 1000;
        public BrandingSettings Branding { get; set; } = new();
    }

    private List<string> GetOptions(string kind) => kind switch
    {
        "Category" => _data.Categories,
        "Status" => _data.Statuses,
        "Priority" => _data.Priorities,
        _ => throw new ArgumentException("Unknown ticket option.", nameof(kind))
    };
    private static string NormalizeManagedOptionKind(string kind) => (kind ?? string.Empty).Trim() switch
    {
        "Teams" => "Team",
        "Departments" => "Department",
        "Locations" => "Location",
        "AssetTypes" => "Asset type",
        "AssetMakes" => "Asset make",
        "AssetModels" => "Asset model",
        _ => (kind ?? string.Empty).Trim()
    };

    private List<string> GetManagedOptions(string kind) => NormalizeManagedOptionKind(kind) switch
    {
        "Team" => _data.TechnicianTeams,
        "Department" => _data.Departments,
        "Location" => _data.Locations,
        "Asset type" => _data.AssetTypes,
        "Asset make" => _data.AssetMakes,
        "Asset model" => _data.AssetModels,
        _ => []
    };
    private static bool IsManagedOptionKind(string kind) =>
        NormalizeManagedOptionKind(kind) is "Team" or "Department" or "Location" or "Asset type" or "Asset make" or "Asset model";
    private static bool IsTicketOptionKind(string kind) =>
        kind is "Category" or "Status" or "Priority";

    public static string NormalizeAttributeType(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "dropdown" => "dropdown",
        "checkbox" => "checkbox",
        "multi-line" or "multiline" => "multi-line",
        _ => "single-line"
    };

    public static bool IsAttributeType(string? value) =>
        value is "dropdown" or "checkbox" or "single-line" or "multi-line";

    public static string NormalizeChoices(string? value) =>
        string.Join(",", (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal));

    public static IReadOnlyList<string> GetChoices(AssetAttributeDefinition definition) =>
        NormalizeChoices(definition.Choices).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    public static IReadOnlyList<string> GetChoices(TicketAttributeDefinition definition) =>
        NormalizeChoices(definition.Choices).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string NormalizeDurationUnit(string? value) =>
        string.Equals(value?.Trim(), "days", StringComparison.OrdinalIgnoreCase) ? "days" : "hours";

    private static void EnsureOptions(List<string> options, IEnumerable<string> defaults)
    {
        foreach (var value in defaults)
        {
            if (!options.Contains(value, StringComparer.OrdinalIgnoreCase))
                options.Add(value);
        }
    }
}
