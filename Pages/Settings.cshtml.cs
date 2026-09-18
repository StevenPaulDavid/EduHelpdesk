using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

public class SettingsModel(HelpdeskStore store) : PageModel
{
    [BindProperty] public IFormFile? UserCsv { get; set; }
    [BindProperty] public IFormFile? TechnicianCsv { get; set; }
    [TempData] public string? Message { get; set; }

    public void OnGet() { }

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
}
