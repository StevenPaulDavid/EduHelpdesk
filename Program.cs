using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;

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

    // A list page and its folder are two different things to ASP.NET: AuthorizeFolder("/Assets") matches /Assets/Add
    // but never /Assets itself. Assets, Parts and Suppliers were folder-only, which left their list pages - including
    // bulk edit and delete - open to any signed-in technician. Everything below names the page AND the folder.
    static string Policy(string module, ModulePermission action) => PermissionRequirement.PolicyName(module, action);
    // An add/edit page serves both, so it opens for either and the page model checks which one it is actually doing.
    const ModulePermission NewOrEdit = ModulePermission.New | ModulePermission.Edit;

    // Tickets: the queues and detail. Deleting and merging are checked in the handlers, because the same page serves
    // both reading and destroying.
    options.Conventions.AuthorizePage("/Jobs", Policy(Modules.Tickets, ModulePermission.Access));
    options.Conventions.AuthorizePage("/Job", Policy(Modules.Tickets, ModulePermission.View));
    options.Conventions.AuthorizePage("/NewTicket", Policy(Modules.Tickets, ModulePermission.New));
    options.Conventions.AuthorizePage("/PrintLabel", Policy(Modules.Tickets, ModulePermission.View));
    options.Conventions.AuthorizePage("/PrintJobSheet", Policy(Modules.Tickets, ModulePermission.View));

    // Assets. The list needs Access, the detail page View, and each page under /Assets/ asks for what it does rather
    // than sharing one folder rule - which is the whole point of separating New from Edit.
    options.Conventions.AuthorizePage("/Assets", Policy(Modules.Assets, ModulePermission.Access));
    options.Conventions.AuthorizePage("/Asset", Policy(Modules.Assets, ModulePermission.View));
    options.Conventions.AuthorizePage("/Assets/Add", Policy(Modules.Assets, ModulePermission.New));
    options.Conventions.AuthorizePage("/Assets/Import", Policy(Modules.Assets, ModulePermission.New));
    options.Conventions.AuthorizePage("/Assets/Delete", Policy(Modules.Assets, ModulePermission.Delete));

    options.Conventions.AuthorizePage("/Kits", Policy(Modules.Kits, ModulePermission.Access));
    options.Conventions.AuthorizePage("/Kits/Edit", Policy(Modules.Kits, NewOrEdit));

    // Issuing a device is New; booking it back in is Edit and happens from the list.
    options.Conventions.AuthorizePage("/Loans", Policy(Modules.Loans, ModulePermission.Access));
    options.Conventions.AuthorizePage("/Loans/Issue", Policy(Modules.Loans, ModulePermission.New));

    options.Conventions.AuthorizePage("/Parts", Policy(Modules.Parts, ModulePermission.Access));
    options.Conventions.AuthorizePage("/Parts/Edit", Policy(Modules.Parts, NewOrEdit));
    options.Conventions.AuthorizePage("/Parts/Delete", Policy(Modules.Parts, ModulePermission.Delete));

    options.Conventions.AuthorizePage("/Suppliers", Policy(Modules.Suppliers, ModulePermission.Access));
    options.Conventions.AuthorizePage("/Supplier", Policy(Modules.Suppliers, ModulePermission.View));
    options.Conventions.AuthorizePage("/Suppliers/Edit", Policy(Modules.Suppliers, NewOrEdit));
    options.Conventions.AuthorizePage("/Suppliers/Delete", Policy(Modules.Suppliers, ModulePermission.Delete));

    // People is three directories on one page, so the page itself only needs to reach one of them; each maintenance
    // page carries its own permission. /User is the requester detail page and was the one duplicating /People/User
    // without any check - it edits names, emails, portal passwords and ticket ownership.
    options.Conventions.AuthorizePage("/People", PermissionRequirement.PeoplePolicy);
    options.Conventions.AuthorizePage("/User", Policy(Modules.Requesters, ModulePermission.View));
    options.Conventions.AuthorizePage("/People/User", Policy(Modules.Requesters, NewOrEdit));
    options.Conventions.AuthorizePage("/People/DeleteUser", Policy(Modules.Requesters, ModulePermission.Delete));
    options.Conventions.AuthorizePage("/People/Technician", Policy(Modules.StaffAccounts, NewOrEdit));
    options.Conventions.AuthorizePage("/People/DeleteTechnician", Policy(Modules.StaffAccounts, ModulePermission.Delete));
    options.Conventions.AuthorizePage("/People/Role", Policy(Modules.Roles, NewOrEdit));
    options.Conventions.AuthorizePage("/People/DeleteRole", Policy(Modules.Roles, ModulePermission.Delete));

    // Reports: the area needs Access, and each report needs its own flag. A print view carries two AuthorizePage calls -
    // its report's flag and the export flag - and ASP.NET combines them, so taking export away closes the print route
    // without touching who can read the report on screen. The CSV handlers check the same flag themselves.
    options.Conventions.AuthorizeFolder("/Reports", Policy(Modules.Reports, ModulePermission.Access));
    options.Conventions.AuthorizePage("/Reports", Modules.Flags.ReportAssets);
    options.Conventions.AuthorizePage("/Reports/Print", Modules.Flags.ReportAssets);
    options.Conventions.AuthorizePage("/Reports/Print", Modules.Flags.ReportExport);
    options.Conventions.AuthorizePage("/Reports/Tickets", Modules.Flags.ReportTickets);
    options.Conventions.AuthorizePage("/Reports/TicketsPrint", Modules.Flags.ReportTickets);
    options.Conventions.AuthorizePage("/Reports/TicketsPrint", Modules.Flags.ReportExport);
    options.Conventions.AuthorizePage("/Reports/Parts", Modules.Flags.ReportParts);
    options.Conventions.AuthorizePage("/Reports/PartsPrint", Modules.Flags.ReportParts);
    options.Conventions.AuthorizePage("/Reports/PartsPrint", Modules.Flags.ReportExport);
    options.Conventions.AuthorizePage("/Reports/Loans", Modules.Flags.ReportLoans);
    options.Conventions.AuthorizePage("/Reports/LoansPrint", Modules.Flags.ReportLoans);
    options.Conventions.AuthorizePage("/Reports/LoansPrint", Modules.Flags.ReportExport);
    // The finance report shows purchase prices, order references and disposal proceeds together.
    options.Conventions.AuthorizePage("/Reports/Finance", Modules.Flags.ReportFinance);
    options.Conventions.AuthorizePage("/Reports/FinancePrint", Modules.Flags.ReportFinance);
    options.Conventions.AuthorizePage("/Reports/FinancePrint", Modules.Flags.ReportExport);

    // The Settings area (branding, option lists, CSV import, factory reset). The audit log lives under /Settings in the
    // tree but is its own module, so this cannot use AuthorizeFolder: a page rule does not replace a folder rule, it is
    // added to it, and a DPO given only the audit log would be stopped by the folder's Settings level. The option-list
    // pages are named individually instead.
    options.Conventions.AuthorizePage("/Settings", Policy(Modules.Settings, ModulePermission.Access));
    options.Conventions.AddFolderApplicationModelConvention("/Settings", model =>
    {
        if (model.ViewEnginePath is "/Settings/Audit" or "/Settings/AuditPrint") return;
        model.Filters.Add(new AuthorizeFilter(Policy(Modules.Settings, ModulePermission.Edit)));
    });
    options.Conventions.AuthorizePage("/Settings/Audit", Policy(Modules.AuditLog, ModulePermission.Access));
    options.Conventions.AuthorizePage("/Settings/AuditPrint", Policy(Modules.AuditLog, ModulePermission.Access));
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
// Generated from the module list rather than written out by hand, so a new module cannot be added without its policies
// existing - a missing policy name throws at startup, which is a far better failure than silently allowing everyone.
builder.Services.AddAuthorization(options =>
{
    foreach (var module in Modules.All)
    {
        foreach (var action in Modules.ActionsFor(module))
            options.AddPolicy(PermissionRequirement.PolicyName(module.Key, action),
                policy => policy.Requirements.Add(PermissionRequirement.For(module.Key, action)));
        // The combined policy the add/edit pages use, registered for any module that offers both boxes.
        var newOrEdit = ModulePermission.New | ModulePermission.Edit;
        if (module.Supports.HasFlag(newOrEdit))
            options.AddPolicy(PermissionRequirement.PolicyName(module.Key, newOrEdit),
                policy => policy.Requirements.Add(PermissionRequirement.For(module.Key, newOrEdit)));
    }
    foreach (var flag in Modules.Flags.All)
        options.AddPolicy(flag.Key, policy => policy.Requirements.Add(PermissionRequirement.ForFlag(flag.Key)));
    options.AddPolicy(PermissionRequirement.PeoplePolicy, policy => policy.Requirements.Add(
        PermissionRequirement.ForAny(Modules.Requesters, Modules.StaffAccounts, Modules.Roles)));
});

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
