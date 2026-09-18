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
        if (_data.Users.Count == 0 && _data.Technicians.Count == 0)
        {
            Seed();
        }
    }

    public IReadOnlyList<UserRecord> Users { get { lock (_sync) return _data.Users; } }
    public IReadOnlyList<TechnicianRecord> Technicians { get { lock (_sync) return _data.Technicians; } }
    public IReadOnlyList<AssetRecord> Assets { get { lock (_sync) return _data.Assets; } }
    public IReadOnlyList<TicketRecord> Tickets { get { lock (_sync) return _data.Tickets.OrderByDescending(x => x.Number).ToList(); } }
    public BrandingSettings Branding { get { lock (_sync) return _data.Branding; } }
    public bool HasPrintTemplate => File.Exists(_templatePath);

    public void AddUser(UserRecord item) { lock (_sync) { _data.Users.Add(item); Save(); } }
    public void AddTechnician(TechnicianRecord item) { lock (_sync) { _data.Technicians.Add(item); Save(); } }
    public void AddAsset(AssetRecord item) { lock (_sync) { _data.Assets.Add(item); Save(); } }
    public int AddTicket(TicketRecord item) { lock (_sync) { var number = ++_data.LastTicketNumber; _data.Tickets.Add(item with { Number = number }); Save(); return number; } }
    public bool UpdateUser(UserRecord item) => Update(item, _data.Users, x => x.Id == item.Id);
    public bool UpdateTechnician(TechnicianRecord item) => Update(item, _data.Technicians, x => x.Id == item.Id);
    public bool UpdateAsset(AssetRecord item) => Update(item, _data.Assets, x => x.Id == item.Id);
    public bool UpdateTicket(TicketRecord item) => Update(item, _data.Tickets, x => x.Number == item.Number);
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
        var body = document.MainDocumentPart?.Document.Body;
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
        _data.Users.Add(user);
        _data.Assets.Add(new AssetRecord(Guid.NewGuid(), "LT-1001", "Laptop", "Dell Latitude 5440", "SN-DEMO-001", "Main Campus", user.Id));
        Save();
    }

    public sealed class StoreData
    {
        public List<UserRecord> Users { get; set; } = [];
        public List<TechnicianRecord> Technicians { get; set; } = [];
        public List<AssetRecord> Assets { get; set; } = [];
        public List<TicketRecord> Tickets { get; set; } = [];
        public int LastTicketNumber { get; set; } = 1000;
        public BrandingSettings Branding { get; set; } = new();
    }
}
