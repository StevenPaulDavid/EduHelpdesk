using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.Devices;

// A PC waiting to be linked (HelpdeskStore.Agent) beside the asset whose serial it shares: each field the PC can fill in,
// with what the asset says and what the PC says, and a choice of which to keep. Opening it needs Device agent: Edit
// (Program.cs); linking also needs Assets: Edit, as it changes the asset.
public class ReviewModel(HelpdeskStore store) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? Asset { get; set; }
    // Field key (HelpdeskStore.AgentMergeFields) -> "pc" or "asset".
    [BindProperty] public Dictionary<string, string> Choice { get; set; } = [];

    public HelpdeskStore.AgentMergePreview Preview { get; private set; } = null!;
    public bool CanLink => store.UserCan(User, Modules.Assets, ModulePermission.Edit);
    public bool CanAddNew => store.UserCan(User, Modules.DeviceAgent, ModulePermission.New) && store.UserCan(User, Modules.Assets, ModulePermission.New);
    public bool CanRemove => store.UserCan(User, Modules.DeviceAgent, ModulePermission.Delete);

    public string? HolderName(AssetRecord asset) =>
        asset.AssignedUserId is { } id ? store.Users.FirstOrDefault(x => x.Id == id)?.Name : asset.StudentName.Length > 0 ? $"{asset.StudentName} (student)" : null;

    public IActionResult OnGet() => Load() ? Page() : Gone();

    public IActionResult OnPostLink()
    {
        if (!CanLink) return Forbid();
        if (!Load()) return Gone();
        var usePc = Preview.Rows.Where(x => Choice.TryGetValue(x.Key, out var pick) ? pick == "pc" : x.PcByDefault).Select(x => x.Key).ToList();
        var (ok, message) = store.LinkAgentDevice(Id, Preview.Asset.Id, usePc);
        TempData["Message"] = message;
        return ok ? RedirectToPage("/Devices") : RedirectToPage(new { Id, Asset });
    }

    public IActionResult OnPostAddNew()
    {
        if (!CanAddNew) return Forbid();
        TempData["Message"] = store.AddAgentDeviceAsNew(Id).Message;
        return RedirectToPage("/Devices");
    }

    public IActionResult OnPostRemove()
    {
        if (!CanRemove) return Forbid();
        TempData["Message"] = store.ForgetAgentDevice(Id).Message;
        return RedirectToPage("/Devices");
    }

    private bool Load()
    {
        if (store.AgentMergePreviewFor(Id, Asset) is not { } preview) return false;
        Preview = preview;
        return true;
    }

    // Linked already, removed, or its asset deleted: back to the list, which shows where it is now.
    private IActionResult Gone()
    {
        TempData["Message"] = "That PC isn't waiting to be linked any more.";
        return RedirectToPage("/Devices");
    }
}
