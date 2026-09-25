using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.People;

public class RoleModel(HelpdeskStore store) : PageModel
{
    public IReadOnlyList<Modules.Definition> AllModules => Modules.All;
    public IReadOnlyList<Modules.Flags.Definition> AllFlags => Modules.Flags.All;
    // Every column the grid draws, whether or not a given module offers it - a module that does not gets a blank cell,
    // so the columns stay lined up down the page.
    public static IReadOnlyList<ModulePermission> AllActions => ModulePermissions.Ordered;
    public static bool Offers(Modules.Definition module, ModulePermission action) => module.Supports.HasFlag(action);
    public static string ActionLabel(ModulePermission action) => ModulePermissions.Label(action);
    public static string ActionDescription(ModulePermission action) => ModulePermissions.Describe(action);

    public RoleRecord? Role { get; private set; }
    public string? CurrentName { get; private set; }

    public bool IsTicked(string module, ModulePermission action) => Role?.Allows(module, action) ?? false;
    public bool HasFlag(string flag) => Role?.Has(flag) ?? false;

    public IActionResult OnGet(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            // No name means adding, which is its own permission - the page opens for New or Edit, so being here with
            // only Edit is possible and has to be refused.
            if (!store.UserCan(User, Modules.Roles, ModulePermission.New)) return Forbid();
            return Page();
        }
        if (!store.UserCan(User, Modules.Roles, ModulePermission.Edit)) return Forbid();
        Role = store.Roles.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (Role is null) return NotFound();
        if (Role.IsProtected)
        {
            TempData["Message"] = "The Administrator role cannot be changed.";
            return RedirectToPage("/People", new { tab = "roles" });
        }
        CurrentName = Role.Name;
        return Page();
    }

    // Ticks arrive as grant=<Module>.<Action> and flags as a repeated flag=<key>. Anything unrecognised is dropped
    // rather than guessed at - a permission model should fail closed.
    public IActionResult OnPost(string? currentName, string name, string[]? grant, string[]? flag)
    {
        var adding = string.IsNullOrWhiteSpace(currentName);
        if (!store.UserCan(User, Modules.Roles, adding ? ModulePermission.New : ModulePermission.Edit)) return Forbid();

        CurrentName = currentName;
        var role = new RoleRecord((name ?? string.Empty).Trim());

        foreach (var ticked in grant ?? [])
        {
            var split = ticked.LastIndexOf('.');
            if (split < 0) continue;
            var moduleKey = ticked[..split];
            var action = ModulePermissions.Parse(ticked[(split + 1)..]);
            // Clamp rather than trust the post: a hand-crafted form must not grant Delete on a module that has no
            // records to delete.
            if (action == ModulePermission.None || !Modules.Supports(moduleKey, action)) continue;
            var module = Modules.Find(moduleKey)!;
            role.Grants[module.Key] = role.GrantsFor(module.Key) | action;
        }
        foreach (var key in flag ?? [])
            if (Modules.Flags.Find(key) is { } known) role.Flags.Add(known.Key);

        Role = role;

        var (ok, message) = adding ? store.AddRole(role) : store.UpdateRole(currentName!, role);
        if (!ok)
        {
            ModelState.AddModelError("", message);
            return Page();
        }
        TempData["Message"] = message;
        return RedirectToPage("/People", new { tab = "roles" });
    }
}
