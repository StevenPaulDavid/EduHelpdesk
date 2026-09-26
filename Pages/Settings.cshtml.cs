using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Authentication;
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
    public IReadOnlyList<string> RequireCloseMessagePriorities => store.RequireCloseMessagePriorities;
    public IReadOnlyList<string> RequireCloseMessageCategories => store.RequireCloseMessageCategories;
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

    [BindProperty] public IFormFile? Logo { get; set; }
    public string? LogoVersion => store.LogoVersion;

    public IActionResult OnPostUploadLogo()
    {
        if (Logo is null || Logo.Length == 0)
        {
            Message = "Choose a PNG file to upload.";
            return RedirectToPage(null, null, "logo");
        }
        using var stream = Logo.OpenReadStream();
        Message = store.SaveLogo(stream, Logo.Length).Message;
        return RedirectToPage(null, null, "logo");
    }

    public IActionResult OnPostRemoveLogo()
    {
        Message = store.RemoveLogo().Message;
        return RedirectToPage(null, null, "logo");
    }

    public int AssetReviewDays => store.AssetReviewDays;

    public IActionResult OnPostSaveAssetReview(int days)
    {
        Message = store.SetAssetReviewDays(days);
        return RedirectToPage();
    }

    public int TicketDueSoonHours => store.TicketDueSoonHours;

    public IActionResult OnPostSaveTicketDueSoon(int hours)
    {
        Message = store.SetTicketDueSoonHours(hours);
        return RedirectToPage();
    }

    public int PartsDefaultReorderThreshold => store.PartsDefaultReorderThreshold;

    public IActionResult OnPostSavePartsReorderThreshold(int threshold)
    {
        Message = store.SetPartsDefaultReorderThreshold(threshold);
        return RedirectToPage();
    }

    public int LoanRepeatCount => store.LoanRepeatCount;
    public int LoanRepeatDays => store.LoanRepeatDays;

    public IActionResult OnPostSaveLoanThreshold(int count, int days)
    {
        Message = store.SetLoanRepeatThreshold(count, days);
        return RedirectToPage();
    }

    // The "Go live" panel only appears while the seeded example is still there, so an established system never shows it.
    public bool HasDemoData => store.HasDemoData;
    public IReadOnlyList<(string Kind, string Name)> DemoRecords => store.DemoDataSummary();

    public IActionResult OnPostRemoveDemoData()
    {
        Message = store.RemoveDemoData().Message;
        return RedirectToPage();
    }

    public int AcademicYearStartMonth => store.AcademicYearStartMonth;
    public static IReadOnlyList<(int Month, string Name)> AcademicMonths => AcademicYear.Months;

    public IActionResult OnPostSaveAcademicYear(int month)
    {
        Message = store.SetAcademicYearStartMonth(month);
        return RedirectToPage();
    }

    public string BackupFolder => store.BackupFolder;
    public HelpdeskStore.BackupSettings Backups => store.Backups;
    public string? DataSyncedBy => store.Location?.SyncedBy;
    // Sign-in security: whether passwords reach this page encrypted, and lockouts in the last week.
    public bool IsHttps => Request.IsHttps;
    public int RecentLockouts => store.CountAuditEntries("Sign-in", DateTime.UtcNow.AddDays(-7));

    // A successful reset replaces every account with the bootstrap administrator, so the signed-in user no longer
    // exists and has to be signed out rather than left holding a cookie for a deleted account.
    public async Task<IActionResult> OnPostResetFactoryAsync(string? confirmation, bool keepBackup, bool eraseAudit)
    {
        var (ok, message) = store.ResetFactory(confirmation, keepBackup, eraseAudit);
        Message = message;
        if (!ok) return RedirectToPage();
        await HttpContext.SignOutAsync(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToPage("/Login");
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

    public IActionResult OnPostAddAssetAttribute(string name, string[]? assetTypes, string fieldType, string? choices)
    {
        Message = store.AddAssetAttributeDefinition(name, assetTypes, fieldType, choices);
        return RedirectToPage(new { });
    }

    public IActionResult OnPostUpdateAssetAttribute(Guid id, string name, string[]? assetTypes, string fieldType, string? choices)
    {
        Message = store.UpdateAssetAttributeDefinition(id, name, assetTypes, fieldType, choices);
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
    public IActionResult OnPostAddSla(string name, int duration, string durationUnit, string? description, string[]? priorities, string[]? categories)
    {
        Message = store.AddSla(name, duration, durationUnit, description, priorities, categories);
        return RedirectToPage(new { });
    }
    public IActionResult OnPostUpdateSla(Guid id, string name, int duration, string durationUnit, string? description, string[]? priorities, string[]? categories)
    {
        Message = store.UpdateSla(id, name, duration, durationUnit, description, priorities, categories);
        return RedirectToPage(new { });
    }
    public IActionResult OnPostAddTicketAttribute(string name, string[]? categories, string fieldType, string? choices) { Message = store.AddTicketAttributeDefinition(name, categories, fieldType, choices); return RedirectToPage(new { }); }
    public IActionResult OnPostUpdateTicketAttribute(Guid id, string name, string[]? categories, string fieldType, string? choices) { Message = store.UpdateTicketAttributeDefinition(id, name, categories, fieldType, choices); return RedirectToPage(new { }); }
    public IActionResult OnPostDeleteTicketAttribute(Guid id) { Message = store.DeleteTicketAttributeDefinition(id); return RedirectToPage(new { }); }
    public IActionResult OnPostDeleteSla(Guid id)
    {
        Message = store.DeleteSla(id);
        return RedirectToPage(new { });
    }

    public IActionResult OnPostSaveCloseRequirements(string[]? priorities, string[]? categories)
    {
        Message = store.SetCloseMessageRequirements(priorities, categories);
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

    public IActionResult OnGetUserImportTemplate()
    {
        const string csv = "Name,Email,Department,Location\r\nJane Doe,jane.doe@example.com,IT,Main Building\r\n";
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", "users-import-template.csv");
    }

    public IActionResult OnGetTechnicianImportTemplate()
    {
        const string csv = "Name,Email,Team,Role\r\nJohn Smith,john.smith@example.com,IT Support,Technician\r\n";
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", "technicians-import-template.csv");
    }

    public async Task<IActionResult> OnPostImportUsersAsync()
    {
        if (UserCsv is null || UserCsv.Length == 0) { Message = "Choose a users CSV file first."; return RedirectToPage(); }
        var records = new List<UserRecord>();
        var invalidRows = 0;
        using var reader = new StreamReader(UserCsv.OpenReadStream());
        await reader.ReadLineAsync();
        while (await reader.ReadLineAsync() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var cells = line.Split(',').Select(x => x.Trim().Trim('"')).ToArray();
            var name = cells.ElementAtOrDefault(0) ?? "";
            var email = cells.ElementAtOrDefault(1) ?? "";
            if (string.IsNullOrWhiteSpace(name) || !Mail(email)) { invalidRows++; continue; }
            var department = cells.ElementAtOrDefault(2) ?? "";
            var location = cells.ElementAtOrDefault(3) ?? "";
            records.Add(new(Guid.NewGuid(), name, email, department, location));
        }
        var (imported, duplicates) = store.ImportUsers(records);
        var skipped = invalidRows + duplicates;
        Message = skipped > 0 ? $"{imported} users imported, {skipped} row(s) skipped (missing name/email or email already in use)." : $"{imported} users imported.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostImportTechniciansAsync()
    {
        if (TechnicianCsv is null || TechnicianCsv.Length == 0) { Message = "Choose a technicians CSV file first."; return RedirectToPage(); }
        var records = new List<TechnicianRecord>();
        var invalidRows = 0;
        using var reader = new StreamReader(TechnicianCsv.OpenReadStream());
        await reader.ReadLineAsync();
        while (await reader.ReadLineAsync() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var cells = line.Split(',').Select(x => x.Trim().Trim('"')).ToArray();
            var name = cells.ElementAtOrDefault(0) ?? "";
            var email = cells.ElementAtOrDefault(1) ?? "";
            if (string.IsNullOrWhiteSpace(name) || !Mail(email)) { invalidRows++; continue; }
            var team = cells.ElementAtOrDefault(2) ?? "";
            var role = store.NormalizeRoleName(cells.ElementAtOrDefault(3));
            records.Add(new(Guid.NewGuid(), name, email, team, role));
        }
        var (imported, duplicates) = store.ImportTechnicians(records);
        var skipped = invalidRows + duplicates;
        Message = skipped > 0 ? $"{imported} technicians imported, {skipped} row(s) skipped (missing name/email or email already in use)." : $"{imported} technicians imported.";
        return RedirectToPage();
    }

    public sealed record ImportList(string Kind, string Label, string TemplateFile, string Header, string[] Examples);

    public static readonly IReadOnlyList<ImportList> ImportLists =
    [
        new("AssetTypes", "Asset types", "asset-types-import-template.csv", "Name", ["Laptop", "Tablet"]),
        new("AssetMakes", "Asset makes", "asset-makes-import-template.csv", "Name", ["Dell", "Apple"]),
        new("AssetModels", "Asset models", "asset-models-import-template.csv", "Name,Make", ["Latitude 5440,Dell", "iPad 10th Generation,Apple"]),
        new("Categories", "Categories", "categories-import-template.csv", "Name", ["Printing", "Wi-Fi"])
    ];

    public IActionResult OnGetOptionImportTemplate(string kind)
    {
        var list = ImportLists.FirstOrDefault(x => x.Kind == kind);
        if (list is null) return NotFound();
        var csv = list.Header + "\r\n" + string.Join("\r\n", list.Examples) + "\r\n";
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", list.TemplateFile);
    }

    public async Task<IActionResult> OnPostImportOptionsAsync(string kind, IFormFile? optionCsv)
    {
        var list = ImportLists.FirstOrDefault(x => x.Kind == kind);
        if (list is null) { Message = "Choose a list to import."; return RedirectToPage(); }
        var noun = list.Label.ToLowerInvariant();
        if (optionCsv is null || optionCsv.Length == 0) { Message = $"Choose a CSV file to import {noun} from first."; return RedirectToPage(); }
        var rows = new List<List<string>>();
        var checkedHeader = false;
        using var reader = new StreamReader(optionCsv.OpenReadStream());
        while (await reader.ReadLineAsync() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var fields = ReadCsvFields(line);
            if (!checkedHeader)
            {
                checkedHeader = true;
                if (fields[0].Equals("Name", StringComparison.OrdinalIgnoreCase)) continue;
            }
            rows.Add(fields);
        }

        if (kind == "AssetModels")
        {
            var (importedModels, linked, skippedModels) = store.ImportAssetModels(rows.Select(x => (x[0], x.ElementAtOrDefault(1))));
            var parts = new List<string>();
            if (linked > 0) parts.Add($"{linked} existing linked to a make");
            if (skippedModels > 0) parts.Add($"{skippedModels} row(s) skipped (blank, or already exist)");
            Message = importedModels == 0 && linked == 0 && skippedModels == 0 ? $"No {noun} found in the file."
                : $"{importedModels} {noun} imported" + (parts.Count > 0 ? ", " + string.Join(", ", parts) : "") + ".";
            return RedirectToPage();
        }

        var (imported, skipped) = store.ImportOptions(kind, rows.Select(x => x[0]));
        Message = imported == 0 && skipped == 0 ? $"No {noun} found in the file."
            : skipped > 0 ? $"{imported} {noun} imported, {skipped} row(s) skipped (blank or already exist)."
            : $"{imported} {noun} imported.";
        return RedirectToPage();
    }

    // Splits one CSV line into trimmed fields, honouring "quoted, values" and "" escapes. Always returns at least one field.
    private static List<string> ReadCsvFields(string line)
    {
        var fields = new List<string>();
        var value = new System.Text.StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c != '"') value.Append(c);
                else if (i + 1 < line.Length && line[i + 1] == '"') { value.Append('"'); i++; }
                else inQuotes = false;
            }
            else if (c == '"' && value.ToString().Trim().Length == 0) { inQuotes = true; value.Clear(); }
            else if (c == ',') { fields.Add(value.ToString().Trim()); value.Clear(); }
            else value.Append(c);
        }
        fields.Add(value.ToString().Trim());
        return fields;
    }

    private static bool Mail(string value) => value.Contains('@', StringComparison.Ordinal) && value.Contains('.', StringComparison.Ordinal);
    private static bool Hex(string value) => System.Text.RegularExpressions.Regex.IsMatch(value ?? "", "^#[0-9A-Fa-f]{6}$");
}
