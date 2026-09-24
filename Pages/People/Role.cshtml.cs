using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EduHelpdesk.Pages.People;

public class RoleModel(HelpdeskStore store) : PageModel
{
    public IReadOnlyList<Modules.Definition> AllModules => Modules.All;
    public IReadOnlyList<Modules.Flags.Definition> AllFlags => Modules.Flags.All;
    public static IReadOnlyList<PermissionLevel> LevelsFor(Modules.Definition module) => Modules.LevelsFor(module);
    public static string LevelLabel(PermissionLevel level) => PermissionLevels.Label(level);
    public static string LevelDescription(PermissionLevel level) => PermissionLevels.Describe(level);

    public RoleRecord? Role { get; private set; }
    public string? CurrentName { get; private set; }

    public PermissionLevel LevelOf(string module) => Role?.LevelFor(module) ?? PermissionLevel.None;
    public bool HasFlag(string flag) => Role?.Has(flag) ?? false;

    public IActionResult OnGet(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Page();
        Role = store.Roles.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (Role is null) return NotFound();
        if (Role.IsProtected)
        {
            TempData["Message"] = "The Administrator role cannot be changed.";
            return RedirectToPage("/People");
        }
        CurrentName = Role.Name;
        return Page();
    }

    // Levels arrive as level.<Module>=<Level>, flags as a repeated flag=<key>. Anything unrecognised is dropped rather
    // than guessed at - a permission model should fail closed.
    public IActionResult OnPost(string? currentName, string name, string[]? flag)
    {
        CurrentName = currentName;
        var role = new RoleRecord((name ?? string.Empty).Trim());

        foreach (var module in Modules.All)
        {
            var level = PermissionLevels.Parse(Request.Form[$"level.{module.Key}"]);
            // Clamp rather than trust the post: a hand-crafted form must not grant Delete on a module that only goes
            // up to Access.
            if (level > module.Max) level = module.Max;
            if (level != PermissionLevel.None) role.Levels[module.Key] = level;
        }
        foreach (var key in flag ?? [])
            if (Modules.Flags.Find(key) is { } known) role.Flags.Add(known.Key);

        Role = role;

        var (ok, message) = string.IsNullOrWhiteSpace(currentName) ? store.AddRole(role) : store.UpdateRole(currentName, role);
        if (!ok)
        {
            ModelState.AddModelError("", message);
            return Page();
        }
        TempData["Message"] = message;
        return RedirectToPage("/People");
    }
}
