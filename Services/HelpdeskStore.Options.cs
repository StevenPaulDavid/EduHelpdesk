using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// The option lists and small settings behind Settings: teams, departments, locations, categories, statuses, asset types and models, and the rules that keep them consistent.
public sealed partial class HelpdeskStore
{
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
            for (var i = 0; i < _data.Tickets.Count; i++)
            {
                if (string.Equals(_data.Tickets[i].TeamName, oldValue, StringComparison.OrdinalIgnoreCase))
                    _data.Tickets[i] = _data.Tickets[i] with { TeamName = newValue };
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
            if (IsProtectedContractStatus(kind, oldValue)) return ProtectedContractStatusMessage;
            options[index] = newValue;
            for (var i = 0; i < _data.Contracts.Count; i++)
            {
                var contract = _data.Contracts[i];
                _data.Contracts[i] = kind switch
                {
                    "Contract type" when string.Equals(contract.ContractType, oldValue, StringComparison.OrdinalIgnoreCase) => contract with { ContractType = newValue },
                    "Spend category" when string.Equals(contract.SpendCategory, oldValue, StringComparison.OrdinalIgnoreCase) => contract with { SpendCategory = newValue },
                    "Contract duration" when string.Equals(contract.Duration, oldValue, StringComparison.OrdinalIgnoreCase) => contract with { Duration = newValue },
                    "Contract status" when string.Equals(contract.Status, oldValue, StringComparison.OrdinalIgnoreCase) => contract with { Status = newValue },
                    _ => contract
                };
            }
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
                    "Asset status" when string.Equals(asset.Status, oldValue, StringComparison.OrdinalIgnoreCase) => asset with { Status = newValue },
                    "Building" when string.Equals(asset.Building, oldValue, StringComparison.OrdinalIgnoreCase) => asset with { Building = newValue },
                    "Asset condition" when string.Equals(asset.Condition, oldValue, StringComparison.OrdinalIgnoreCase) => asset with { Condition = newValue },
                    _ => asset
                };
            }
            if (kind == "Asset type")
            {
                for (var i = 0; i < _data.AssetAttributeDefinitions.Count; i++)
                    _data.AssetAttributeDefinitions[i] = _data.AssetAttributeDefinitions[i] with { AssetTypes = RenameInScope(_data.AssetAttributeDefinitions[i].AssetTypes, oldValue, newValue) };
                for (var i = 0; i < _data.Parts.Count; i++)
                    _data.Parts[i] = _data.Parts[i] with { AssetTypes = RenameInScope(_data.Parts[i].AssetTypes, oldValue, newValue) };
            }
            if (kind == "Asset model" && _data.AssetModelMakes.Remove(oldValue, out var linkedMake))
                _data.AssetModelMakes[newValue] = linkedMake;
            if (kind == "Asset type" && _data.AssetTypeLifespans.Remove(oldValue, out var lifespan))
                _data.AssetTypeLifespans[newValue] = lifespan;
            if (kind == "Asset make")
            {
                foreach (var model in _data.AssetModelMakes.Where(x => string.Equals(x.Value, oldValue, StringComparison.OrdinalIgnoreCase)).Select(x => x.Key).ToList())
                    _data.AssetModelMakes[model] = newValue;
            }
            for (var i = 0; i < _data.Parts.Count; i++)
            {
                var part = _data.Parts[i];
                _data.Parts[i] = kind switch
                {
                    "Part category" when string.Equals(part.Category, oldValue, StringComparison.OrdinalIgnoreCase) => part with { Category = newValue },
                    "Part location" when string.Equals(part.Location, oldValue, StringComparison.OrdinalIgnoreCase) => part with { Location = newValue },
                    _ => part
                };
            }
            if (kind == "Purchasing requirement")
            {
                for (var i = 0; i < _data.Projects.Count; i++)
                    if (_data.Projects[i].PurchasingRequirements.Contains(oldValue, StringComparer.OrdinalIgnoreCase))
                        _data.Projects[i] = _data.Projects[i] with { PurchasingRequirements = RenameInScope(_data.Projects[i].PurchasingRequirements, oldValue, newValue) };
            }
            if (kind == "Loan reason")
            {
                for (var i = 0; i < _data.KitLoans.Count; i++)
                    if (string.Equals(_data.KitLoans[i].Reason, oldValue, StringComparison.OrdinalIgnoreCase))
                        _data.KitLoans[i] = _data.KitLoans[i] with { Reason = newValue };
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
                "Asset make" => _data.Assets.Any(x => string.Equals(x.Make, item, StringComparison.OrdinalIgnoreCase)) || _data.AssetModelMakes.Values.Any(x => string.Equals(x, item, StringComparison.OrdinalIgnoreCase)),
                "Asset type" => _data.Assets.Any(x => string.Equals(x.Type, item, StringComparison.OrdinalIgnoreCase)) || _data.AssetAttributeDefinitions.Any(x => x.AssetTypes.Contains(item, StringComparer.OrdinalIgnoreCase)) || _data.Parts.Any(x => x.AssetTypes.Contains(item, StringComparer.OrdinalIgnoreCase)),
                "Asset model" => _data.Assets.Any(x => string.Equals(x.Model, item, StringComparison.OrdinalIgnoreCase)),
                "Asset status" => _data.Assets.Any(x => string.Equals(x.Status, item, StringComparison.OrdinalIgnoreCase)),
                "Building" => _data.Assets.Any(x => string.Equals(x.Building, item, StringComparison.OrdinalIgnoreCase)),
                "Asset condition" => _data.Assets.Any(x => string.Equals(x.Condition, item, StringComparison.OrdinalIgnoreCase)),
                "Contract type" => _data.Contracts.Any(x => string.Equals(x.ContractType, item, StringComparison.OrdinalIgnoreCase)),
                "Spend category" => _data.Contracts.Any(x => string.Equals(x.SpendCategory, item, StringComparison.OrdinalIgnoreCase)),
                "Contract duration" => _data.Contracts.Any(x => string.Equals(x.Duration, item, StringComparison.OrdinalIgnoreCase)),
                "Contract status" => _data.Contracts.Any(x => string.Equals(x.Status, item, StringComparison.OrdinalIgnoreCase)),
                "Part category" => _data.Parts.Any(x => string.Equals(x.Category, item, StringComparison.OrdinalIgnoreCase)),
                "Part location" => _data.Parts.Any(x => string.Equals(x.Location, item, StringComparison.OrdinalIgnoreCase)),
                "Loan reason" => _data.KitLoans.Any(x => string.Equals(x.Reason, item, StringComparison.OrdinalIgnoreCase)),
                _ => false
            };
            if (IsProtectedContractStatus(kind, item)) return ProtectedContractStatusMessage;
            if (inUse) return $"That {kind.ToLowerInvariant()} cannot be deleted because it is in use.";
            options.RemoveAt(index);
            if (kind == "Asset model") _data.AssetModelMakes.Remove(item);
            if (kind == "Asset type") _data.AssetTypeLifespans.Remove(item);
            Save();
            return $"{kind} deleted.";
        }
    }
    public string AddAssetType(string name, int? lifespanYears)
    {
        lock (_sync)
        {
            if (lifespanYears is < 1 or > 50) return "Enter a lifespan between 1 and 50 years, or leave it blank.";
            var message = AddManagedOption("Asset type", name);
            if (message != "Asset type added.") return message;
            if (!lifespanYears.HasValue) return message;
            var saved = SetAssetTypeLifespan(name, lifespanYears);
            return saved == "Asset type updated." ? message : saved;
        }
    }
    public string UpdateAssetType(string currentType, string name, int? lifespanYears)
    {
        lock (_sync)
        {
            if (lifespanYears is < 1 or > 50) return "Enter a lifespan between 1 and 50 years, or leave it blank.";
            var oldName = (currentType ?? string.Empty).Trim();
            var message = UpdateManagedOption("Asset type", oldName, name);
            if (message != "Asset type updated.") return message;
            var finalName = string.IsNullOrWhiteSpace(name) || string.Equals(oldName, name.Trim(), StringComparison.OrdinalIgnoreCase) ? oldName : name.Trim();
            return SetAssetTypeLifespan(finalName, lifespanYears);
        }
    }
    // A blank lifespan (null) removes it, so replacement dates for that type are only those typed on assets.
    public string SetAssetTypeLifespan(string assetType, int? years)
    {
        lock (_sync)
        {
            var name = _data.AssetTypes.FirstOrDefault(x => string.Equals(x, (assetType ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
            if (name is null) return "Asset type was not found.";
            if (years is < 1 or > 50) return "Enter a lifespan between 1 and 50 years, or leave it blank.";
            if (years is null) _data.AssetTypeLifespans.Remove(name);
            else _data.AssetTypeLifespans[name] = years.Value;
            Save();
            return "Asset type updated.";
        }
    }
    public string SetAssetReviewDays(int days)
    {
        lock (_sync)
        {
            if (days is < 0 or > 3650) return "Enter a number of days between 0 and 3650.";
            _data.AssetReviewDays = days;
            Save();
            return "Asset review window saved.";
        }
    }
    public string SetAssetCheckSettings(int dueSoonDays, int supportWarningDays, int intervalMonths)
    {
        lock (_sync)
        {
            if (dueSoonDays is < 0 or > 3650 || supportWarningDays is < 0 or > 3650) return "Enter a number of days between 0 and 3650.";
            if (intervalMonths is < 1 or > 120) return "Enter between 1 and 120 months between checks.";
            _data.CheckDueSoonDays = dueSoonDays;
            _data.SupportWarningDays = supportWarningDays;
            _data.CheckIntervalMonths = intervalMonths;
            Save();
            return "Check and support windows saved.";
        }
    }
    public string SetAcademicYearStartMonth(int month)
    {
        lock (_sync)
        {
            if (month is < 1 or > 12) return "Choose the month the academic year starts in.";
            _data.AcademicYearStartMonth = month;
            Save();
            return $"Academic year now starts in {AcademicYear.Months.First(x => x.Month == month).Name}.";
        }
    }
    public string SetTicketDueSoonHours(int hours)
    {
        lock (_sync)
        {
            if (hours is < 0 or > 720) return "Enter a number of hours between 0 and 720.";
            _data.TicketDueSoonHours = hours;
            Save();
            return "Due soon window saved.";
        }
    }
    public string SetPartsDefaultReorderThreshold(int threshold)
    {
        lock (_sync)
        {
            if (threshold < 0) return "Enter a reorder threshold of 0 or more.";
            _data.PartsDefaultReorderThreshold = threshold;
            Save();
            return "Parts reorder threshold saved.";
        }
    }
    public string AddAssetModel(string name, string? make)
    {
        lock (_sync)
        {
            var item = (name ?? string.Empty).Trim();
            if (item.Length == 0) return "Asset model name is required.";
            if (_data.AssetModels.Contains(item, StringComparer.OrdinalIgnoreCase)) return "That asset model already exists.";
            var linkedMake = FindMake(make, out var validMake);
            if (!validMake) return "Select a valid make.";
            _data.AssetModels.Add(item);
            if (linkedMake is not null) _data.AssetModelMakes[item] = linkedMake;
            Save();
            return "Asset model added.";
        }
    }
    // Renames a model and/or changes the make it belongs to. A blank make means the model can be used with any make.
    public string UpdateAssetModel(string currentModel, string name, string? make)
    {
        lock (_sync)
        {
            var oldName = (currentModel ?? string.Empty).Trim();
            FindMake(make, out var validMake);
            if (!validMake) return "Select a valid make.";
            var message = UpdateManagedOption("Asset model", oldName, name);
            if (message != "Asset model updated.") return message;
            var finalName = string.IsNullOrWhiteSpace(name) || string.Equals(oldName, name.Trim(), StringComparison.OrdinalIgnoreCase) ? oldName : name.Trim();
            return SetAssetModelMake(finalName, make);
        }
    }
    public string SetAssetModelMake(string model, string? make)
    {
        lock (_sync)
        {
            var name = _data.AssetModels.FirstOrDefault(x => string.Equals(x, (model ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
            if (name is null) return "Asset model was not found.";
            var linkedMake = FindMake(make, out var validMake);
            if (!validMake) return "Select a valid make.";
            if (linkedMake is null) _data.AssetModelMakes.Remove(name);
            else _data.AssetModelMakes[name] = linkedMake;
            Save();
            return "Asset model updated.";
        }
    }
    // Returns the configured make matching the text (null for blank). valid is false when a non-blank make is not configured.
    private string? FindMake(string? make, out bool valid)
    {
        var requested = (make ?? string.Empty).Trim();
        valid = true;
        if (requested.Length == 0) return null;
        var match = _data.AssetMakes.FirstOrDefault(x => string.Equals(x, requested, StringComparison.OrdinalIgnoreCase));
        valid = match is not null;
        return match;
    }
    public (int Imported, int Linked, int Skipped) ImportAssetModels(IEnumerable<(string Name, string? Make)> rows)
    {
        lock (_sync)
        {
            var imported = 0;
            var linked = 0;
            var skipped = 0;
            foreach (var (rawName, rawMake) in rows)
            {
                var name = (rawName ?? string.Empty).Trim();
                var make = (rawMake ?? string.Empty).Trim();
                if (name.Length == 0) { skipped++; continue; }
                var existing = _data.AssetModels.FirstOrDefault(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
                if (existing is not null && (make.Length == 0 || _data.AssetModelMakes.ContainsKey(existing))) { skipped++; continue; }
                string? linkedMake = null;
                if (make.Length > 0)
                {
                    linkedMake = _data.AssetMakes.FirstOrDefault(x => string.Equals(x, make, StringComparison.OrdinalIgnoreCase));
                    if (linkedMake is null) { _data.AssetMakes.Add(make); linkedMake = make; }
                }
                if (existing is null)
                {
                    _data.AssetModels.Add(name);
                    if (linkedMake is not null) _data.AssetModelMakes[name] = linkedMake;
                    imported++;
                }
                else
                {
                    _data.AssetModelMakes[existing] = linkedMake!;
                    linked++;
                }
            }
            if (imported > 0 || linked > 0) Save();
            return (imported, linked, skipped);
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
            if (!IsTicketOptionKind(kind)) return "Invalid ticket option.";
            var options = GetOptions(kind);
            var oldValue = currentValue.Trim();
            var newValue = value.Trim();
            if (string.IsNullOrWhiteSpace(newValue)) return $"{kind} name is required.";
            if (kind == "Status" && IsBuiltInStatus(oldValue)) return BuiltInStatusMessage;
            if (string.Equals(oldValue, newValue, StringComparison.OrdinalIgnoreCase)) return $"{kind} updated.";
            if (options.Contains(newValue, StringComparer.OrdinalIgnoreCase)) return $"That {kind.ToLowerInvariant()} already exists.";
            var index = options.FindIndex(x => string.Equals(x, oldValue, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return $"{kind} was not found.";
            options[index] = newValue;
            if (kind == "Status" && _data.StatusDescriptions.Remove(oldValue, out var existingDescription))
                _data.StatusDescriptions[newValue] = existingDescription;
            if (kind == "Status" && _data.SlaPauseStatuses.RemoveAll(x => string.Equals(x, oldValue, StringComparison.OrdinalIgnoreCase)) > 0)
                _data.SlaPauseStatuses.Add(newValue);
            if (kind == "Priority")
            {
                var priorityIndex = _data.RequireCloseMessagePriorities.FindIndex(x => string.Equals(x, oldValue, StringComparison.OrdinalIgnoreCase));
                if (priorityIndex >= 0) _data.RequireCloseMessagePriorities[priorityIndex] = newValue;
                for (var i = 0; i < _data.ServiceItems.Count; i++)
                    if (string.Equals(_data.ServiceItems[i].DefaultPriority, oldValue, StringComparison.OrdinalIgnoreCase))
                        _data.ServiceItems[i] = _data.ServiceItems[i] with { DefaultPriority = newValue };
            }
            if (kind == "Category")
            {
                var categoryIndex = _data.RequireCloseMessageCategories.FindIndex(x => string.Equals(x, oldValue, StringComparison.OrdinalIgnoreCase));
                if (categoryIndex >= 0) _data.RequireCloseMessageCategories[categoryIndex] = newValue;
                for (var i = 0; i < _data.ServiceItems.Count; i++)
                    if (string.Equals(_data.ServiceItems[i].Category, oldValue, StringComparison.OrdinalIgnoreCase))
                        _data.ServiceItems[i] = _data.ServiceItems[i] with { Category = newValue };
                if (_data.CategoryStyles.Remove(oldValue, out var tileStyle)) _data.CategoryStyles[newValue] = tileStyle;
                // Templates too: one ticked for the portal would otherwise lose its category and drop off the buttons.
                for (var i = 0; i < _data.TicketTemplates.Count; i++)
                    if (string.Equals(_data.TicketTemplates[i].Category, oldValue, StringComparison.OrdinalIgnoreCase))
                        _data.TicketTemplates[i] = _data.TicketTemplates[i] with { Category = newValue };
                for (var i = 0; i < _data.TicketAttributeDefinitions.Count; i++)
                    _data.TicketAttributeDefinitions[i] = _data.TicketAttributeDefinitions[i] with { Categories = RenameInScope(_data.TicketAttributeDefinitions[i].Categories, oldValue, newValue) };
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
            if (kind == "Status" && IsBuiltInStatus(item)) return BuiltInStatusMessage;
            var options = GetOptions(kind);
            var index = options.FindIndex(x => string.Equals(x, item, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return $"{kind} was not found.";
            // New tickets need somewhere to start: the last open status, priority or category stays.
            if (options.Count(x => kind != "Status" || !IsBuiltInStatus(x)) <= 1)
                return $"Keep at least one {(kind == "Status" ? "open status" : kind.ToLowerInvariant())} - new tickets need one.";
            if (_data.Tickets.Any(x => kind switch
            {
                "Category" => string.Equals(x.Category, item, StringComparison.OrdinalIgnoreCase),
                "Status" => string.Equals(x.Status, item, StringComparison.OrdinalIgnoreCase),
                "Priority" => string.Equals(x.Priority, item, StringComparison.OrdinalIgnoreCase),
                _ => false
            })) return $"That {kind.ToLowerInvariant()} cannot be deleted because tickets use it.";
            if (kind == "Category" && _data.TicketAttributeDefinitions.Any(x => x.Categories.Contains(item, StringComparer.OrdinalIgnoreCase)))
                return "That category cannot be deleted because a ticket custom attribute uses it.";
            if (kind == "Category" && _data.ServiceItems.Any(x => string.Equals(x.Category, item, StringComparison.OrdinalIgnoreCase)))
                return "That category cannot be deleted because the service catalogue has items under it.";
            options.RemoveAt(index);
            if (kind == "Status") _data.StatusDescriptions.Remove(item);
            if (kind == "Status") _data.SlaPauseStatuses.RemoveAll(x => string.Equals(x, item, StringComparison.OrdinalIgnoreCase));
            if (kind == "Priority")
                for (var i = 0; i < _data.ServiceItems.Count; i++)
                    if (string.Equals(_data.ServiceItems[i].DefaultPriority, item, StringComparison.OrdinalIgnoreCase))
                        _data.ServiceItems[i] = _data.ServiceItems[i] with { DefaultPriority = "" };
            if (kind == "Priority") _data.RequireCloseMessagePriorities.RemoveAll(x => string.Equals(x, item, StringComparison.OrdinalIgnoreCase));
            if (kind == "Category") _data.RequireCloseMessageCategories.RemoveAll(x => string.Equals(x, item, StringComparison.OrdinalIgnoreCase));
            if (kind == "Category") _data.CategoryStyles.Remove(item);
            Save();
            return $"{kind} deleted.";
        }
    }
    // "Closed" is what the whole system means by finished: SLA timers stop on it, queues and reports leave it out, the
    // Close button sets it and a portal reply reopens from it. Renaming it once turned every closed ticket back into an
    // open, overdue one, so it can't be renamed or deleted. Its description can still be edited.
    public static bool IsBuiltInStatus(string? status) => string.Equals(status?.Trim(), TicketInsights.ClosedStatus, StringComparison.OrdinalIgnoreCase);
    private const string BuiltInStatusMessage = "\"Closed\" is built in - closing tickets, SLA timers and reports all depend on it - so it can't be renamed or deleted. You can still change its description.";

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
    // Run on every start. Only values the code itself sets or depends on come back if they have been removed - putting
    // back every starting option meant a school that deleted, say, "Classroom AV" found it again after each restart.
    // A new built-in value added in a later version needs its own line here, since EnsureFactoryOptions no longer runs
    // against existing databases.
    private void EnsureSystemOptions()
    {
        // Set by IssueKit and DisposeAsset.
        EnsureOptions(_data.AssetStatuses, ["On loan", "Disposed"]);
        // What "ended" means on the contracts register (ContractRules.IsLive). Only once the register's lists exist, so
        // an upgrade still gets the full starting list from EnsureComplianceDefaults rather than this one value.
        if (_data.ComplianceVersion >= 1) EnsureOptions(_data.ContractStatuses, [ContractStatusDefaults.Expired]);
        // What "finished" means everywhere (see IsBuiltInStatus), plus at least one open status for new tickets to start in.
        EnsureOptions(_data.Statuses, [TicketInsights.ClosedStatus]);
        if (!_data.Statuses.Any(x => !IsBuiltInStatus(x))) _data.Statuses.Insert(0, "Open");
        // A list emptied completely would leave forms with nothing to pick; it gets its starting values back.
        if (_data.Priorities.Count == 0) EnsureOptions(_data.Priorities, ["Normal", "Low", "High", "Urgent"]);
        if (_data.Categories.Count == 0) EnsureOptions(_data.Categories, ["Hardware", "Software", "Account", "Network", "Classroom AV", "Other"]);
        if (_data.LoanReasons.Count == 0) EnsureOptions(_data.LoanReasons, ["Forgot own device", "Supply or visitor", "Own device in repair", "Other"]);
        // The DfE asset register's condition column. Existing databases get these on first start after the update too.
        if (_data.AssetConditions.Count == 0) EnsureOptions(_data.AssetConditions, AssetConditionDefaults);
    }
    private static readonly string[] AssetConditionDefaults = ["New", "Used", "Refurbished", "Donated"];

    // The lists a brand new install (or a factory reset) starts with.
    private void EnsureFactoryOptions()
    {
        // "On loan" and "Disposed" are set by the system itself (see IssueKit and DisposeAsset), so unlike the others
        // they have to exist in every database rather than only in newly seeded ones.
        // "Disposed" goes LAST on purpose: the add-asset form and the CSV importer both fall back to
        // AssetStatuses.FirstOrDefault() for a default, and defaulting new kit to disposed would be absurd.
        EnsureOptions(_data.AssetStatuses, ["In use", "On loan", "In stock or spare", "In repair", "Lost or stolen", "Disposed"]);
        EnsureOptions(_data.Categories, ["Hardware", "Software", "Account", "Network", "Classroom AV", "Other"]);
        EnsureOptions(_data.Statuses, ["Open", "In Progress", "On Hold", "Closed"]);
        if (_data.SlaPauseStatuses.Count == 0) _data.SlaPauseStatuses.Add("On Hold");
        EnsureOptions(_data.Priorities, ["Normal", "Low", "High", "Urgent"]);
        EnsureOptions(_data.LoanReasons, ["Forgot own device", "Supply or visitor", "Own device in repair", "Other"]);
        EnsureOptions(_data.AssetConditions, AssetConditionDefaults);
    }

    // Kinds use the same names as the Settings option pages: AssetTypes, AssetMakes, AssetModels and Categories.
    public (int Imported, int Skipped) ImportOptions(string kind, IEnumerable<string> values)
    {
        lock (_sync)
        {
            var options = kind switch
            {
                "AssetTypes" => _data.AssetTypes,
                "AssetMakes" => _data.AssetMakes,
                "AssetModels" => _data.AssetModels,
                "Categories" => _data.Categories,
                _ => throw new ArgumentException("Unknown option list.", nameof(kind))
            };
            var imported = 0;
            var skipped = 0;
            foreach (var value in values)
            {
                var item = (value ?? string.Empty).Trim();
                if (item.Length == 0 || options.Contains(item, StringComparer.OrdinalIgnoreCase))
                {
                    skipped++;
                    continue;
                }
                options.Add(item);
                imported++;
            }
            if (imported > 0) Save();
            return (imported, skipped);
        }
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
        "AssetStatuses" => "Asset status",
        "PartCategories" => "Part category",
        "PartLocations" => "Part location",
        "LoanReasons" => "Loan reason",
        "PurchasingRequirements" => "Purchasing requirement",
        "Buildings" => "Building",
        "AssetConditions" => "Asset condition",
        "ContractTypes" => "Contract type",
        "SpendCategories" => "Spend category",
        "ContractDurations" => "Contract duration",
        "ContractStatuses" => "Contract status",
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
        "Asset status" => _data.AssetStatuses,
        "Part category" => _data.PartCategories,
        "Part location" => _data.PartLocations,
        "Loan reason" => _data.LoanReasons,
        // Deleting one is always allowed: a project keeps the text of what it asked for, so nothing points at the list.
        "Purchasing requirement" => _data.PurchasingRequirements,
        "Building" => _data.Buildings,
        "Asset condition" => _data.AssetConditions,
        "Contract type" => _data.ContractTypes,
        "Spend category" => _data.SpendCategories,
        "Contract duration" => _data.ContractDurations,
        "Contract status" => _data.ContractStatuses,
        _ => []
    };
    private static bool IsManagedOptionKind(string kind) =>
        NormalizeManagedOptionKind(kind) is "Team" or "Department" or "Location" or "Asset type" or "Asset make" or "Asset model" or "Asset status" or "Part category" or "Part location" or "Loan reason" or "Purchasing requirement" or "Building" or "Asset condition"
            or "Contract type" or "Spend category" or "Contract duration" or "Contract status";
    // Expired is how the register knows a contract has ended (ContractRules.IsLive).
    private static bool IsProtectedContractStatus(string kind, string value) =>
        kind == "Contract status" && string.Equals(value.Trim(), ContractStatusDefaults.Expired, StringComparison.OrdinalIgnoreCase);
    private const string ProtectedContractStatusMessage = "Expired is how the register knows a contract has ended, so it can't be renamed or removed.";
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

    public static string NormalizeDurationUnit(string? value) => SlaUnits.Normalize(value);

    private static List<string> NormalizeScope(IEnumerable<string>? values) =>
        (values ?? []).Select(x => x?.Trim() ?? string.Empty).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    // Returns the requested values using the casing of the configured options, or null if any value is not a configured option.
    private static List<string>? ResolveScope(IEnumerable<string>? requested, List<string> options)
    {
        var resolved = new List<string>();
        foreach (var value in NormalizeScope(requested))
        {
            var match = options.FirstOrDefault(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));
            if (match is null) return null;
            resolved.Add(match);
        }
        return resolved;
    }

    // An empty scope means "applies to everything", so it overlaps with any other scope.
    private static bool ScopesOverlap(List<string> first, List<string> second) =>
        first.Count == 0 || second.Count == 0 || first.Intersect(second, StringComparer.OrdinalIgnoreCase).Any();

    private static List<string> RenameInScope(List<string> scope, string oldValue, string newValue) =>
        scope.Select(x => string.Equals(x, oldValue, StringComparison.OrdinalIgnoreCase) ? newValue : x).ToList();

    private static string DuplicateAttributeMessage(List<string> scope, string noun) =>
        scope.Count == 0 ? "That attribute name is already in use." : $"That attribute already exists for one of the selected {noun}.";

    private static void EnsureOptions(List<string> options, IEnumerable<string> defaults)
    {
        foreach (var value in defaults)
        {
            if (!options.Contains(value, StringComparer.OrdinalIgnoreCase))
                options.Add(value);
        }
    }
}
