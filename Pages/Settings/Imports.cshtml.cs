using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Settings;

// Settings → Imports: requesters, staff accounts and option lists from CSV. Needs Settings: Edit (Program.cs).
public class ImportsModel(HelpdeskStore store) : PageModel
{
    [BindProperty] public IFormFile? UserCsv { get; set; }
    [BindProperty] public IFormFile? TechnicianCsv { get; set; }
    [TempData] public string? Message { get; set; }

    public void OnGet() { }

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
}
