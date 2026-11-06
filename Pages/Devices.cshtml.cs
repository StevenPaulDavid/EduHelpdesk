using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages;

// The device agent (HelpdeskStore.Agent): turning it on and the install command, PCs waiting to be linked to an asset
// whose serial they share, and every PC reporting in. Opening it needs Device agent: Access (Program.cs); each button
// asks for its own tick here.
public class DevicesModel(HelpdeskStore store, IConfiguration configuration, IHostEnvironment environment) : PageModel
{
    public bool Enabled => store.AgentEnabled;
    public bool IsHttps => Request.IsHttps;
    public bool CanSetUp => store.UserCan(User, Modules.DeviceAgent, ModulePermission.Edit);
    public bool CanView => store.UserCan(User, Modules.DeviceAgent, ModulePermission.View);
    // Linking changes the asset; adding makes one. So each needs the asset tick as well as the agent's.
    public bool CanLink => CanSetUp && store.UserCan(User, Modules.Assets, ModulePermission.Edit);
    public bool CanAddNew => store.UserCan(User, Modules.DeviceAgent, ModulePermission.New) && store.UserCan(User, Modules.Assets, ModulePermission.New);
    public bool CanRemove => store.UserCan(User, Modules.DeviceAgent, ModulePermission.Delete);

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [TempData] public string? Message { get; set; }

    public IReadOnlyList<HelpdeskStore.AgentDevice> Waiting { get; private set; } = [];
    public IReadOnlyList<HelpdeskStore.AgentDevice> Linked { get; private set; } = [];
    public int LinkedTotal { get; private set; }
    private Dictionary<Guid, AssetRecord> _assets = [];

    public string Address => store.SiteAddress is { Length: > 0 } set ? set.TrimEnd('/') : People.QuickStartModel.GuessAddress(Request).TrimEnd('/');
    public string? Fingerprint => ServerCertificate.Fingerprint(configuration, environment);

    // What to run (as an administrator) on a PC, or push through Intune until the installer arrives.
    public string InstallCommand =>
        $"EduHelpdesk.Agent.exe install --server {Address} --key {store.AgentEnrolmentKey}" + (Fingerprint is { } fingerprint ? $" --fingerprint {fingerprint}" : "");

    public void OnGet()
    {
        _assets = store.Assets.ToDictionary(x => x.Id);
        var devices = store.AgentDevices;
        Waiting = devices.Where(x => x.Status == HelpdeskStore.AgentDeviceStatus.Waiting).OrderBy(x => x.EnrolledAt).ToList();
        var linked = devices.Where(x => x.Status == HelpdeskStore.AgentDeviceStatus.Linked).ToList();
        LinkedTotal = linked.Count;
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var q = Q.Trim();
            linked = linked.Where(x => x.ComputerName.Contains(q, StringComparison.OrdinalIgnoreCase)
                || x.SerialNumber.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (AssetFor(x.AssetId)?.AssetTag.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || AgentFacts.LastUser(x.Report).Contains(q, StringComparison.OrdinalIgnoreCase)
                || AgentFacts.Model(x.Report).Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        Linked = linked;
    }

    public AssetRecord? AssetFor(Guid? id) => id is { } value && _assets.TryGetValue(value, out var asset) ? asset : null;

    public IActionResult OnPostTurnOn() => SetUp(() => store.SetAgentEnabled(true));
    public IActionResult OnPostTurnOff() => SetUp(() => store.SetAgentEnabled(false));
    public IActionResult OnPostNewKey() => SetUp(store.NewAgentEnrolmentKey);

    public IActionResult OnPostLink(Guid id)
    {
        if (!CanLink) return Forbid();
        Message = store.LinkAgentDevice(id).Message;
        return RedirectToPage();
    }

    public IActionResult OnPostAddNew(Guid id)
    {
        if (!CanAddNew) return Forbid();
        Message = store.AddAgentDeviceAsNew(id).Message;
        return RedirectToPage();
    }

    public IActionResult OnPostRemove(Guid id)
    {
        if (!CanRemove) return Forbid();
        Message = store.ForgetAgentDevice(id).Message;
        return RedirectToPage(new { Q });
    }

    private IActionResult SetUp(Func<string> change)
    {
        if (!CanSetUp) return Forbid();
        Message = change();
        return RedirectToPage();
    }

    // "3 hours ago", "yesterday", or the date - what matters is whether it is still reporting.
    public static string Ago(DateTime utc)
    {
        var span = DateTime.UtcNow - utc;
        if (span < TimeSpan.FromMinutes(2)) return "just now";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes} minutes ago";
        if (span < TimeSpan.FromHours(24)) return (int)span.TotalHours == 1 ? "an hour ago" : $"{(int)span.TotalHours} hours ago";
        if (span < TimeSpan.FromDays(2)) return "yesterday";
        if (span < TimeSpan.FromDays(14)) return $"{(int)span.TotalDays} days ago";
        return utc.ToLocalTime().ToString("d MMM yyyy");
    }
}
