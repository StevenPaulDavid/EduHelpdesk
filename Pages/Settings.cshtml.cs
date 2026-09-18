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
    public IReadOnlyList<string> Categories => store.Categories;
    public IReadOnlyList<string> Statuses => store.Statuses;
    public IReadOnlyList<string> Priorities => store.Priorities;
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

    public IActionResult OnPostAddTechnicianTeam(string team, string? returnBlade)
    {
        Message = store.AddTechnicianTeam(team);
        return RedirectToPage(new { blade = returnBlade });
    }

    public IActionResult OnPostUpdateTechnicianTeam(string currentTeam, string team, string? returnBlade)
    {
        Message = store.UpdateTechnicianTeam(currentTeam, team);
        return RedirectToPage(new { blade = returnBlade });
    }

    public IActionResult OnPostAddDepartment(string department, string? returnBlade)
    {
        Message = store.AddDepartment(department);
        return RedirectToPage(new { blade = returnBlade });
    }

    public IActionResult OnPostUpdateDepartment(string currentDepartment, string department, string? returnBlade)
    {
        Message = store.UpdateDepartment(currentDepartment, department);
        return RedirectToPage(new { blade = returnBlade });
    }

    public IActionResult OnPostAddTicketOption(string kind, string value, string? returnBlade)
    {
        Message = store.AddTicketOption(kind, value);
        return RedirectToPage(new { blade = returnBlade });
    }

    public IActionResult OnPostUpdateTicketOption(string kind, string currentValue, string value, string? returnBlade)
    {
        Message = store.UpdateTicketOption(kind, currentValue, value);
        return RedirectToPage(new { blade = returnBlade });
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
