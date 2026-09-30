using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Settings;

// Settings → Database: every table as it is stored, for looking into a problem. Needs "View raw database", not Settings
// (Program.cs), like the audit log. Read-only throughout - see Services/RawDatabase.cs.
public class DatabaseModel(RawDatabase raw, HelpdeskStore store) : PageModel
{
    public IReadOnlyList<RawDatabase.TableSummary> Tables { get; private set; } = [];
    // The way back: Settings for those who have it, otherwise the Overview.
    public bool CanSeeSettings => store.UserCan(User, Modules.Settings, EduHelpdesk.Models.ModulePermission.Access);
    public bool SeesSecrets => RawDatabase.IsAdministrator(User);

    public HelpdeskStore.HealthReport Health { get; private set; } = null!;
    // The warning limits are a setting like any other, so changing them needs Settings: Edit as well.
    public bool CanEditLimits => store.UserCan(User, Modules.Settings, EduHelpdesk.Models.ModulePermission.Edit);
    [TempData] public string? Message { get; set; }

    public void OnGet()
    {
        Tables = raw.Tables();
        Health = store.DatabaseHealth(DateTime.UtcNow);
    }

    public IActionResult OnPostLimits(int slowSaveMs, int lowDiskGb)
    {
        if (!CanEditLimits) return Forbid();
        Message = store.SetHealthLimits(slowSaveMs, lowDiskGb).Message;
        return RedirectToPage(null, null, null, "health");
    }

    public IReadOnlyList<RawDatabase.TableSummary> Biggest => Tables.OrderByDescending(x => x.Rows).Take(6).ToList();

    // "+1.2 MB since 3 Sep", or "-" before a day's reading has been kept.
    public string Growth(long now, long? then) => then is not { } before || Health.GrowthFrom is null
        ? "Growth shows once a day's reading has been kept"
        : $"{(now >= before ? "+" : "−")}{HelpdeskStore.FormatSize(Math.Abs(now - before))} since {Health.GrowthFrom.Day:d MMM}";

    public static string Time(double ms) => ms < 1000 ? $"{ms:0} ms" : $"{ms / 1000:0.0} s";

    // "Look up a record": a ticket or project number, an asset tag, or a person's email - or the id itself.
    public IActionResult OnGetFind(string? kind, string? key)
    {
        if (RawDatabase.FindKind(kind) is not { } found || string.IsNullOrWhiteSpace(key)) return RedirectToPage();
        var value = key.Trim().TrimStart('#');
        if (!Guid.TryParse(value, out _))
        {
            var match = found.Key switch
            {
                "asset" => store.Assets.FirstOrDefault(x => string.Equals(x.AssetTag, value, StringComparison.OrdinalIgnoreCase))?.Id,
                "user" => store.Users.FirstOrDefault(x => string.Equals(x.Email, value, StringComparison.OrdinalIgnoreCase))?.Id,
                "technician" => store.Technicians.FirstOrDefault(x => string.Equals(x.Email, value, StringComparison.OrdinalIgnoreCase))?.Id,
                _ => null
            };
            if (match is { } id) value = id.ToString();
            else if (found.Key is "project" && value.StartsWith("PRJ-", StringComparison.OrdinalIgnoreCase) && int.TryParse(value[4..], out var number)) value = number.ToString();
        }
        return RedirectToPage("/Settings/DatabaseRecord", new { kind = found.Key, key = value });
    }
}
