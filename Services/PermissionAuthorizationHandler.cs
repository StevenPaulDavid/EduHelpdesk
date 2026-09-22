using Microsoft.AspNetCore.Authorization;

namespace EduHelpdesk.Services;

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

// Looks up the signed-in account's current role permissions from the store on every check, rather than baking them
// into the login cookie - so editing a role's permissions takes effect on that account's very next request.
public sealed class PermissionAuthorizationHandler(HelpdeskStore store) : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (store.UserHasPermission(context.User, requirement.Permission))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
