using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using EduHelpdesk.Models;
using System.Net;
using System.Text;

namespace EduHelpdesk.Services;

public sealed class HelpdeskStore
{
    private readonly string _path;
    private readonly object _sync = new();
    private StoreData _data;
    private readonly string _templatePath;

    public HelpdeskStore(IHostEnvironment environment)
    {
        _path = Path.Combine(environment.ContentRootPath, "App_Data", "helpdesk.json");
        _templatePath = Path.Combine(environment.ContentRootPath, "App_Data", "print-template.docx");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        _data = Load();
        _data.Branding ??= new BrandingSettings();
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
        EnsureOptions(_data.Categories, ["Hardware", "Software", "Account", "Network", "Classroom AV", "Other"]);
        EnsureOptions(_data.Statuses, ["Open", "In Progress", "On Hold", "Closed"]);
        EnsureOptions(_data.Priorities, ["Normal", "Low", "High", "Urgent"]);
        if (_data.Users.Count == 0 && _data.Technicians.Count == 0)
        {
            Seed();
        }
    }

    public IReadOnlyList<UserRecord> Users { get { lock (_sync) return _data.Users; } }
    public IReadOnlyList<TechnicianRecord> Technicians { get { lock (_sync) return _data.Technicians; } }
    public IReadOnlyList<string> TechnicianTeams { get { lock (_sync) return _data.TechnicianTeams.OrderBy(x => x).ToList(); } }
    public IReadOnlyList<string> Departments { get { lock (_sync) return _data.Departments.OrderBy(x => x).ToList(); } }
    public IReadOnlyList<string> Categories { get { lock (_sync) return _data.Categories.ToList(); } }
    public IReadOnlyList<string> Statuses { get { lock (_sync) return _data.Statuses.ToList(); } }
    public IReadOnlyList<string> Priorities { get { lock (_sync) return _data.Priorities.ToList(); } }
    public IReadOnlyList<AssetRecord> Assets { get { lock (_sync) return _data.Assets; } }
    public IReadOnlyList<TicketRecord> Tickets { get { lock (_sync) return _data.Tickets.OrderByDescending(x => x.Number).ToList(); } }
    public BrandingSettings Branding { get { lock (_sync) return _data.Branding; } }
    public bool HasPrintTemplate => File.Exists(_templatePath);

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
    public void AddAsset(AssetRecord item) { lock (_sync) { _data.Assets.Add(item); Save(); } }
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
    public bool UpdateAsset(AssetRecord item) => Update(item, _data.Assets, x => x.Id == item.Id);
    public bool UpdateAssetAndTickets(AssetRecord asset, IEnumerable<int> selectedTicketNumbers)
    {
        lock (_sync)
        {
            var assetIndex = _data.Assets.FindIndex(x => x.Id == asset.Id);
            if (assetIndex < 0) return false;

            _data.Assets[assetIndex] = asset;
            var selected = selectedTicketNumbers.ToHashSet();
            for (var i = 0; i < _data.Tickets.Count; i++)
            {
                var ticket = _data.Tickets[i];
                if (selected.Contains(ticket.Number))
                {
                    if (ticket.AssetId != asset.Id)
                    {
                        var history = ticket.History.ToList();
                        history.Add(new("Asset changed", "An asset was linked.", DateTime.UtcNow));
                        _data.Tickets[i] = ticket with { AssetId = asset.Id, History = history };
                    }
                }
                else if (ticket.AssetId == asset.Id)
                {
                    var history = ticket.History.ToList();
                    history.Add(new("Asset changed", "The linked asset was removed.", DateTime.UtcNow));
                    _data.Tickets[i] = ticket with { AssetId = null, History = history };
                }
            }

            Save();
            return true;
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
            Save();
            return true;
        }
    }
    public void UpdateBranding(BrandingSettings item) { lock (_sync) { _data.Branding = item; Save(); } }
    public void SavePrintTemplate(Stream source)
    {
        using var destination = File.Create(_templatePath);
        source.CopyTo(destination);
    }

    public string RenderPrintTemplate(TicketRecord ticket, UserRecord? requester, TechnicianRecord? technician, AssetRecord? asset)
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
            ["{{Asset.Tag}}"] = asset?.AssetTag ?? "No asset linked",
            ["{{Asset.Type}}"] = asset?.Type ?? "",
            ["{{Asset.Model}}"] = asset?.Model ?? "",
            ["{{Asset.Serial}}"] = asset?.SerialNumber ?? "",
            ["{{Asset.Location}}"] = asset?.Location ?? ""
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
            if (_data.Tickets.Any(x => x.AssetId == id))
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

    public int ImportUsers(IEnumerable<UserRecord> users) { lock (_sync) { _data.Users.AddRange(users); Save(); return users.Count(); } }
    public int ImportTechnicians(IEnumerable<TechnicianRecord> technicians) { lock (_sync) { _data.Technicians.AddRange(technicians); Save(); return technicians.Count(); } }

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
        if (previous.AssetId != updated.AssetId) history.Add(new("Asset changed", updated.AssetId.HasValue ? "An asset was linked." : "The linked asset was removed.", now));
        if (previous.ClosedAt != updated.ClosedAt && previous.Status == updated.Status) history.Add(new("Closure changed", updated.ClosedAt.HasValue ? "The ticket was closed." : "The ticket was reopened.", now));
    }

    private StoreData Load()
    {
        if (!File.Exists(_path)) return new();
        try { return JsonSerializer.Deserialize<StoreData>(File.ReadAllText(_path)) ?? new(); }
        catch (JsonException) { return new(); }
    }

    private void Save() => File.WriteAllText(_path, JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));

    private void Seed()
    {
        var technician = new TechnicianRecord(Guid.NewGuid(), "Alex Morgan", "alex.morgan@school.example", "IT Support");
        var user = new UserRecord(Guid.NewGuid(), "Jordan Lee", "jordan.lee@school.example", "Science", "Main Campus");
        _data.Technicians.Add(technician);
        _data.TechnicianTeams.Add(technician.Team);
        _data.Users.Add(user);
        _data.Assets.Add(new AssetRecord(Guid.NewGuid(), "LT-1001", "Laptop", "Dell Latitude 5440", "SN-DEMO-001", "Main Campus", user.Id));
        Save();
    }

    public sealed class StoreData
    {
        public List<UserRecord> Users { get; set; } = [];
        public List<TechnicianRecord> Technicians { get; set; } = [];
        public List<string> TechnicianTeams { get; set; } = [];
        public List<string> Departments { get; set; } = [];
        public List<string> Categories { get; set; } = [];
        public List<string> Statuses { get; set; } = [];
        public List<string> Priorities { get; set; } = [];
        public List<AssetRecord> Assets { get; set; } = [];
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

    private static void EnsureOptions(List<string> options, IEnumerable<string> defaults)
    {
        foreach (var value in defaults)
        {
            if (!options.Contains(value, StringComparer.OrdinalIgnoreCase))
                options.Add(value);
        }
    }
}
