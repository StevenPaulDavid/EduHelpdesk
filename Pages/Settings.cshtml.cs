using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

public class SettingsModel(HelpdeskStore store) : PageModel
{
    [BindProperty] public BrandingSettings Branding { get; set; } = new();
    [BindProperty] public IFormFile? UserCsv { get; set; }
    [BindProperty] public IFormFile? TechnicianCsv { get; set; }
    [BindProperty] public IFormFile? PrintTemplate { get; set; }
    public bool HasPrintTemplate => store.HasPrintTemplate;
    public IReadOnlyList<string> TechnicianTeams => store.TechnicianTeams;
    public IReadOnlyList<string> Departments => store.Departments;
    public IReadOnlyList<string> Locations => store.Locations;
    public IReadOnlyList<string> AssetTypes => store.AssetTypes;
    public IReadOnlyList<string> AssetMakes => store.AssetMakes;
    public IReadOnlyList<string> AssetModels => store.AssetModels;
    public IReadOnlyList<string> Categories => store.Categories;
    public IReadOnlyList<string> Statuses => store.Statuses;
    public IReadOnlyList<string> Priorities => store.Priorities;
    public IReadOnlyList<AssetAttributeDefinition> AssetAttributeDefinitions => store.AssetAttributeDefinitions;
    public IReadOnlyList<SlaDefinition> Slas => store.Slas;
    public IReadOnlyList<TicketAttributeDefinition> TicketAttributeDefinitions => store.TicketAttributeDefinitions;
    [TempData] public string? Message { get; set; }

    public void OnGet() => Branding = store.Branding;

    public IActionResult OnPostSaveBranding()
    {
        if (!Hex(Branding.PrimaryColor) || !Hex(Branding.AccentColor) || !Hex(Branding.BackgroundColor))
        {
            Message = "Colours must be valid six-digit hex values, such as #067A78.";
            Branding = store.Branding;
            return Page();
        }

        store.UpdateBranding(Branding);
        Message = "Branding saved.";
        return RedirectToPage();
    }

    public IActionResult OnPostResetFactory(string confirmation)
    {
        Message = store.ResetFactory(confirmation);
        return RedirectToPage();
    }

    public IActionResult OnPostAddTechnicianTeam(string team)
    {
        Message = store.AddTechnicianTeam(team);
        return RedirectToPage(new { });
    }

    public IActionResult OnPostUpdateTechnicianTeam(string currentTeam, string team)
    {
        Message = store.UpdateTechnicianTeam(currentTeam, team);
        return RedirectToPage(new { });
    }
    public IActionResult OnPostDeleteTechnicianTeam(string team)
    {
        Message = store.DeleteTechnicianTeam(team);
        return RedirectToPage(new { });
    }

    public IActionResult OnPostAddDepartment(string department)
    {
        Message = store.AddDepartment(department);
        return RedirectToPage(new { });
    }

    public IActionResult OnPostUpdateDepartment(string currentDepartment, string department)
    {
        Message = store.UpdateDepartment(currentDepartment, department);
        return RedirectToPage(new { });
    }
    public IActionResult OnPostDeleteDepartment(string department)
    {
        Message = store.DeleteDepartment(department);
        return RedirectToPage(new { });
    }

    public IActionResult OnPostAddManagedOption(string kind, string value)
    {
        Message = store.AddManagedOption(kind, value);
        return RedirectToPage(new { });
    }

    public IActionResult OnPostUpdateManagedOption(string kind, string currentValue, string value)
    {
        Message = store.UpdateManagedOption(kind, currentValue, value);
        return RedirectToPage(new { });
    }
    public IActionResult OnPostDeleteManagedOption(string kind, string value)
    {
        Message = store.DeleteManagedOption(kind, value);
        return RedirectToPage(new { });
    }

    public IActionResult OnPostAddAssetAttribute(string name, string assetType, string fieldType, string? choices)
    {
        Message = store.AddAssetAttributeDefinition(name, assetType, fieldType, choices);
        return RedirectToPage(new { });
    }

    public IActionResult OnPostUpdateAssetAttribute(Guid id, string name, string assetType, string fieldType, string? choices)
    {
        Message = store.UpdateAssetAttributeDefinition(id, name, assetType, fieldType, choices);
        return RedirectToPage(new { });
    }
    public IActionResult OnPostDeleteAssetAttribute(Guid id)
    {
        Message = store.DeleteAssetAttributeDefinition(id);
        return RedirectToPage(new { });
    }

    public IActionResult OnPostAddTicketOption(string kind, string value)
    {
        Message = store.AddTicketOption(kind, value);
        return RedirectToPage(new { });
    }

    public IActionResult OnPostUpdateTicketOption(string kind, string currentValue, string value)
    {
        Message = store.UpdateTicketOption(kind, currentValue, value);
        return RedirectToPage(new { });
    }
    public IActionResult OnPostDeleteTicketOption(string kind, string value)
    {
        Message = store.DeleteTicketOption(kind, value);
        return RedirectToPage(new { });
    }
    public IActionResult OnPostAddSla(string name, int duration, string durationUnit, string? priority)
    {
        Message = store.AddSla(name, duration, durationUnit, priority);
        return RedirectToPage(new { });
    }
    public IActionResult OnPostUpdateSla(Guid id, string name, int duration, string durationUnit, string? priority)
    {
        Message = store.UpdateSla(id, name, duration, durationUnit, priority);
        return RedirectToPage(new { });
    }
    public IActionResult OnPostAddTicketAttribute(string name, string? category, string fieldType, string? choices) { Message = store.AddTicketAttributeDefinition(name, category, fieldType, choices); return RedirectToPage(new { }); }
    public IActionResult OnPostUpdateTicketAttribute(Guid id, string name, string? category, string fieldType, string? choices) { Message = store.UpdateTicketAttributeDefinition(id, name, category, fieldType, choices); return RedirectToPage(new { }); }
    public IActionResult OnPostDeleteTicketAttribute(Guid id) { Message = store.DeleteTicketAttributeDefinition(id); return RedirectToPage(new { }); }
    public IActionResult OnPostDeleteSla(Guid id)
    {
        Message = store.DeleteSla(id);
        return RedirectToPage(new { });
    }

    public IActionResult OnPostUploadPrintTemplate()
    {
        if (PrintTemplate is null || PrintTemplate.Length == 0 || !Path.GetExtension(PrintTemplate.FileName).Equals(".docx", StringComparison.OrdinalIgnoreCase))
        {
            Message = "Choose a .docx Word document.";
            return RedirectToPage();
        }
        using var stream = PrintTemplate.OpenReadStream();
        store.SavePrintTemplate(stream);
        Message = "Print template uploaded.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostImportUsersAsync()
    {
        if (UserCsv is null || UserCsv.Length == 0) { Message = "Choose a users CSV file first."; return RedirectToPage(); }
        var records = new List<UserRecord>();
        using var reader = new StreamReader(UserCsv.OpenReadStream());
        await reader.ReadLineAsync();
        while (await reader.ReadLineAsync() is { } line)
        {
            var cells = line.Split(',').Select(x => x.Trim().Trim('"')).ToArray();
            if (cells.Length >= 4 && !string.IsNullOrWhiteSpace(cells[0]) && Mail(cells[1])) records.Add(new(Guid.NewGuid(), cells[0], cells[1], cells[2], cells[3]));
        }
        Message = $"{store.ImportUsers(records)} users imported.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostImportTechniciansAsync()
    {
        if (TechnicianCsv is null || TechnicianCsv.Length == 0) { Message = "Choose a technicians CSV file first."; return RedirectToPage(); }
        var records = new List<TechnicianRecord>();
        using var reader = new StreamReader(TechnicianCsv.OpenReadStream());
        await reader.ReadLineAsync();
        while (await reader.ReadLineAsync() is { } line)
        {
            var cells = line.Split(',').Select(x => x.Trim().Trim('"')).ToArray();
            if (cells.Length >= 3 && !string.IsNullOrWhiteSpace(cells[0]) && Mail(cells[1])) records.Add(new(Guid.NewGuid(), cells[0], cells[1], cells[2]));
        }
        Message = $"{store.ImportTechnicians(records)} technicians imported.";
        return RedirectToPage();
    }

    private static bool Mail(string value) => value.Contains('@', StringComparison.Ordinal) && value.Contains('.', StringComparison.Ordinal);
    private static bool Hex(string value) => System.Text.RegularExpressions.Regex.IsMatch(value ?? "", "^#[0-9A-Fa-f]{6}$");
}
