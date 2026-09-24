using EduHelpdesk.Models;
using Microsoft.AspNetCore.Authorization;

namespace EduHelpdesk.Services;

// A requirement is either "this module at this level or higher" or "this flag". One type covers both so there is a
// single handler, and so policy names can be generated from the module list rather than hand-written.
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    private PermissionRequirement(string? module, PermissionLevel level, string? flag)
    {
        Module = module;
        Level = level;
        Flag = flag;
    }

    public string? Module { get; }
    public PermissionLevel Level { get; }
    public string? Flag { get; }

    public static PermissionRequirement For(string module, PermissionLevel level) => new(module, level, null);
    public static PermissionRequirement ForFlag(string flag) => new(null, PermissionLevel.None, flag);

    // The policy name for a module level, e.g. "Assets:View". Flags are their own key ("Reports.Finance") and are
    // registered under it directly, which is why they never contain a colon.
    public static string PolicyName(string module, PermissionLevel level) => $"{module}:{level}";
}

// Looks up the signed-in account's current role permissions from the store on every check, rather than baking them
// into the login cookie - so editing a role's permissions takes effect on that account's very next request.
public sealed class PermissionAuthorizationHandler(HelpdeskStore store) : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var allowed = requirement.Flag is { } flag
            ? store.UserHasFlag(context.User, flag)
            : store.UserCan(context.User, requirement.Module!, requirement.Level);
        if (allowed) context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
