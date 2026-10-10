using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Memory;

namespace EduHelpdesk.Pages;

// Upload, preview, apply - the contracts register import (/Contracts/Import) and the staff list import (/People/Import)
// work the same way (HelpdeskStore.RegisterImport). The upload is held on the server between the steps, and planned
// again when applied, so what is applied is worked out against the data as it is then, not as it was at preview.
public abstract class RegisterImportPage(HelpdeskStore store, IMemoryCache cache) : PageModel
{
    private const int MaxBytes = 10_000_000;
    private const int MaxRows = 20_000;

    protected HelpdeskStore Store { get; } = store;
    protected abstract string Kind { get; }
    protected abstract bool Allowed { get; }
    public abstract string Title { get; }
    public abstract string Intro { get; }
    public abstract string BackPage { get; }
    public abstract string BackLabel { get; }

    [BindProperty(SupportsGet = true, Name = "t")] public string? Token { get; set; }
    [TempData] public string? Message { get; set; }
    public string? FileName { get; private set; }
    public RegisterImportPreview? Preview { get; private set; }

    private sealed record Upload(string FileName, SheetTable Table);

    public IActionResult OnGet()
    {
        if (!Allowed) return Forbid();
        if (Load(Token) is { } upload)
        {
            FileName = upload.FileName;
            Preview = Store.PreviewRegisterImport(Kind, upload.Table);
        }
        else if (!string.IsNullOrWhiteSpace(Token)) Message = "That import has expired. Upload the file again.";
        return Page();
    }

    public async Task<IActionResult> OnPostUploadAsync(IFormFile? file)
    {
        if (!Allowed) return Forbid();
        if (file is null || file.Length == 0) { Message = "Choose a file first."; return RedirectToPage(); }
        if (file.Length > MaxBytes) { Message = "That file is larger than 10 MB."; return RedirectToPage(); }
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer);
        SheetTable table;
        try { table = SpreadsheetReader.Read(buffer.ToArray(), file.FileName, HelpdeskStore.ImportHeaders(Kind), HelpdeskStore.ImportSheetNames(Kind)); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or DocumentFormat.OpenXml.Packaging.OpenXmlPackageException or FormatException)
        {
            Message = "That file couldn't be read. Save it as .xlsx or .csv and try again.";
            return RedirectToPage();
        }
        if (table.Headers.Count == 0) { Message = "No recognisable column headings were found near the top of the file. Use the DfE template, or put headings like those listed below in the first row."; return RedirectToPage(); }
        if (table.Rows.Count == 0) { Message = "The file has headings but no rows to import."; return RedirectToPage(); }
        if (table.Rows.Count > MaxRows) { Message = $"That file has more than {MaxRows:N0} rows. Split it into smaller files."; return RedirectToPage(); }
        var token = Guid.NewGuid().ToString("N");
        cache.Set(Key(token), new Upload(Path.GetFileName(file.FileName), table), new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(45) });
        return RedirectToPage(new { t = token });
    }

    public IActionResult OnPostApply(string? t, int[]? rows)
    {
        if (!Allowed) return Forbid();
        if (Load(t) is not { } upload) { Message = "That import has expired. Upload the file again."; return RedirectToPage(); }
        var (ok, message) = Store.ApplyRegisterImport(Kind, upload.Table, (rows ?? []).ToHashSet());
        Message = message;
        if (!ok) return RedirectToPage(new { t });
        cache.Remove(Key(t!));
        return Redirect(Url.Page(BackPage) ?? "/");
    }

    private Upload? Load(string? token) => !string.IsNullOrWhiteSpace(token) && cache.TryGetValue(Key(token), out Upload? upload) ? upload : null;
    private string Key(string token) => $"register-import:{Kind}:{token}";
}
