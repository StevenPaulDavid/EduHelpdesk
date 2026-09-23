using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

// Room for a ticket upload of several attachments at once (each file is still limited to 10 MB in the store).
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 60_000_000);
builder.Services.AddRazorPages(options =>
{
    // Everyone must be signed in by default; individual pages/folders opt out or tighten further below.
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Login");
    options.Conventions.AllowAnonymousToPage("/AccessDenied");
    // The root page allows anonymous access so it can redirect signed-out visitors to the portal itself (see
    // IndexModel.OnGet) instead of bouncing them straight to the technician login.
    options.Conventions.AllowAnonymousToPage("/Index");
    // The staff self-service portal - reachable with no login at all, by design (see the plan/README).
    options.Conventions.AllowAnonymousToFolder("/Portal");

    // Role definitions themselves - who can create/edit/delete roles.
    options.Conventions.AuthorizePage("/People/Role", "RequireManageRoles");
    options.Conventions.AuthorizePage("/People/DeleteRole", "RequireManageRoles");
    // Staff account/role management (assigning an existing role to a technician account).
    options.Conventions.AuthorizePage("/People/Technician", "RequireManageStaff");
    options.Conventions.AuthorizePage("/People/DeleteTechnician", "RequireManageStaff");
    // Requester directory maintenance, assets, suppliers and parts.
    options.Conventions.AuthorizePage("/People/User", "RequireManageRequesters");
    options.Conventions.AuthorizePage("/People/DeleteUser", "RequireManageRequesters");
    options.Conventions.AuthorizeFolder("/Assets", "RequireManageAssets");
    // Issuing and returning a kit is day-to-day desk work, so /Loans stays open to any signed-in technician.
    // Only creating and editing the kits themselves needs the asset permission.
    options.Conventions.AuthorizePage("/Loans/Kit", "RequireManageAssets");
    options.Conventions.AuthorizeFolder("/Suppliers", "RequireManageSuppliers");
    options.Conventions.AuthorizeFolder("/Parts", "RequireManageParts");
    // The Settings area (branding, option lists, CSV import, factory reset, audit log).
    options.Conventions.AuthorizePage("/Settings", "RequireSettings");
    options.Conventions.AuthorizeFolder("/Settings", "RequireSettings");
});
builder.Services.AddMemoryCache();
// The store reads the signed-in account off the current request to attribute changes - see HelpdeskStore.CurrentActor.
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<HelpdeskStore>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "EduHelpdeskAuth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.LoginPath = "/Login";
        options.AccessDeniedPath = "/AccessDenied";
        // Idle sliding expiration: stays signed in as long as the account is used at least once every 8 hours.
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnValidatePrincipal = context =>
        {
            // Absolute cap on top of the sliding window, so a browser left open can't stay signed in forever.
            if (context.Properties.IssuedUtc is { } issuedUtc && DateTimeOffset.UtcNow - issuedUtc > TimeSpan.FromDays(14))
                context.RejectPrincipal();
            return Task.CompletedTask;
        };
    });
// Each policy is a permission requirement, checked live against the signed-in account's role (see
// PermissionAuthorizationHandler and HelpdeskStore.RoleGrants) rather than a fixed set of role names - roles and
// their permissions are user-defined (see the Roles panel on the People page), except Administrator, which is
// hardcoded to always pass every check.
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("RequireSettings", policy => policy.Requirements.Add(new PermissionRequirement(Permissions.Settings)))
    .AddPolicy("RequireManageRoles", policy => policy.Requirements.Add(new PermissionRequirement(Permissions.ManageRoles)))
    .AddPolicy("RequireManageStaff", policy => policy.Requirements.Add(new PermissionRequirement(Permissions.ManageStaff)))
    .AddPolicy("RequireManageRequesters", policy => policy.Requirements.Add(new PermissionRequirement(Permissions.ManageRequesters)))
    .AddPolicy("RequireManageAssets", policy => policy.Requirements.Add(new PermissionRequirement(Permissions.ManageAssets)))
    .AddPolicy("RequireManageSuppliers", policy => policy.Requirements.Add(new PermissionRequirement(Permissions.ManageSuppliers)))
    .AddPolicy("RequireManageParts", policy => policy.Requirements.Add(new PermissionRequirement(Permissions.ManageParts)));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
