using EduHelpdesk.Models;
using EduHelpdesk.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

// Run as a Windows service (deploy/Install-Service.ps1), the working folder is System32, so the app's own folder is
// named as the content root - where appsettings.json, wwwroot and the fonts are. From a console or `dotnet run` nothing
// changes. A service starts in Production unless told otherwise, so the developer error page never appears there.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : null
});
builder.Host.UseWindowsService(options => options.ServiceName = "EduHelpdesk");

// Room for a ticket upload of several attachments at once (each file is still limited to 10 MB in the store).
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 60_000_000);
builder.Services.AddRazorPages(options =>
{
    // Everyone must be signed in by default; individual pages/folders opt out or tighten further below.
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Login");
    // The second step of signing in, for accounts with two-step sign-in. Opens only with a right password behind it.
    options.Conventions.AllowAnonymousToPage("/LoginCode");
    // Setting a new password with a recovery key, for someone who can't sign in. Throttled like /Login.
    options.Conventions.AllowAnonymousToPage("/ForgotPassword");
    options.Conventions.AllowAnonymousToPage("/AccessDenied");
    // A portal visitor or a signed-out one can hit an error too, and shouldn't be sent to the technician sign-in for it.
    options.Conventions.AllowAnonymousToPage("/Error");
    // The root page allows anonymous access so it can redirect signed-out visitors to the portal itself (see
    // IndexModel.OnGet) instead of bouncing them straight to the technician login.
    options.Conventions.AllowAnonymousToPage("/Index");
    // The staff self-service portal - reachable with no login at all, by design (see the plan/README).
    options.Conventions.AllowAnonymousToFolder("/Portal");
    // The footer's light/dark switch, which the portal and the sign-in page show too. It only sets a cookie.
    options.Conventions.AllowAnonymousToPage("/Appearance");

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

    // Projects are raised from the portal, so the helpdesk side is the list and the detail page. Editing, assigning
    // (its own flag) and deleting are checked in the handlers, because the detail page serves all of them.
    options.Conventions.AuthorizePage("/Projects", Policy(Modules.Projects, ModulePermission.Access));
    options.Conventions.AuthorizePage("/Project", Policy(Modules.Projects, ModulePermission.View));

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
    // A requester's guide or a technician's: the page checks New or Edit on whichever module the account belongs to.
    options.Conventions.AuthorizePage("/People/QuickStart", PermissionRequirement.PeoplePolicy);
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
    // The projects report shows quote values and each technician's load, so it is its own tick like Finance.
    options.Conventions.AuthorizePage("/Reports/Projects", Modules.Flags.ReportProjects);
    options.Conventions.AuthorizePage("/Reports/ProjectsPrint", Modules.Flags.ReportProjects);
    options.Conventions.AuthorizePage("/Reports/ProjectsPrint", Modules.Flags.ReportExport);

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

    // The portal is open to anyone who can reach the site, so a post there is held to 1 MB rather than the helpdesk's
    // 60 MB - except the two forms that carry files (a new ticket and a reply), which get room for their few attachments
    // and no more. Its sessions are kept alive (or cleared) by PortalSessionFilter.
    options.Conventions.AddFolderApplicationModelConvention("/Portal", model =>
    {
        var takesFiles = model.ViewEnginePath is "/Portal/NewTicket" or "/Portal/Ticket";
        model.Filters.Add(new RequestSizeLimitAttribute(takesFiles ? HelpdeskStore.MaxPortalAttachments * HelpdeskStore.MaxAttachmentBytes + 1_000_000 : 1_000_000));
        model.Filters.Add(new ServiceFilterAttribute(typeof(PortalSessionFilter)));
    });
})
    .AddMvcOptions(options =>
    {
        // A save the database refuses becomes a message on the page rather than an error page (see SaveFailureFilter).
        options.Filters.Add<SaveFailureFilter>();
        // "Must change password" holds on every page, not just straight after sign-in (see PasswordChangeFilter).
        options.Filters.Add<PasswordChangeFilter>();
        // A handler the page doesn't have is "not found", not a crash (see UnknownHandlerFilter).
        options.Filters.Add<UnknownHandlerFilter>();
    });

// HTTPS. Off by default, because a first install is usually reached by plain http:// on the school network, and
// forcing HTTPS without a certificate would lock everyone out. Once the site has a certificate - in Kestrel, IIS or a
// reverse proxy - set EduHelpdesk:RequireHttps to true: plain-HTTP requests are redirected, browsers are told to use
// HTTPS from then on (HSTS), and every cookie is marked Secure. Behind a proxy that ends HTTPS itself (Azure App
// Service, IIS ARR, nginx), also set the environment variable ASPNETCORE_FORWARDEDHEADERS_ENABLED=true so the app sees
// the visitor's real address and scheme - the sign-in lockout counts failures per address.
var requireHttps = builder.Configuration.GetValue<bool>("EduHelpdesk:RequireHttps");
var cookieSecurity = requireHttps ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
builder.Services.AddAntiforgery(options => options.Cookie.SecurePolicy = cookieSecurity);
builder.Services.Configure<CookieTempDataProviderOptions>(options => options.Cookie.SecurePolicy = cookieSecurity);
if (requireHttps) builder.Services.AddHsts(options => options.MaxAge = TimeSpan.FromDays(365));
builder.Services.AddMemoryCache();
// The store reads the signed-in account off the current request to attribute changes - see HelpdeskStore.CurrentActor.
builder.Services.AddHttpContextAccessor();
// Data Protection encrypts both the technician login cookie and the staff portal cookie (PortalIdentity). Its keys are
// kept with the rest of the data (in its keys folder), so sign-ins survive a restart and move with a restored backup, and
// named by application rather than by folder so a changed install path doesn't invalidate them. On Windows the key
// files are themselves encrypted to this machine: the data folder may sit in a synced folder, and a copied key must not be
// usable anywhere else. Machine rather than user scope, so running the app under a different account keeps working.
// Where all of that lives: App_Data by default, or EduHelpdesk:DataPath - see DataLocation. Resolved before anything
// else touches the data, because the first start in a new location copies the old data across.
var dataLocation = DataLocation.Resolve(builder.Configuration, builder.Environment);
if (dataLocation.CopiedFrom is { } copiedFrom) Console.WriteLine($"EduHelpdesk: data copied from {copiedFrom} to {dataLocation.Folder}.");
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("EduHelpdesk")
    .PersistKeysToFileSystem(new DirectoryInfo(dataLocation.KeysFolder));
if (OperatingSystem.IsWindows()) dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: true);
builder.Services.AddSingleton(dataLocation);
// Warnings and errors also go to a file a day in the data folder's logs folder (Settings → Error log), because a
// service has no console for them to scroll past in. See FileLogProvider.
var fileLog = new FileLogProvider(Path.Combine(dataLocation.Folder, "logs"));
builder.Logging.AddProvider(fileLog);
builder.Services.AddSingleton(fileLog);
builder.Services.AddSingleton<PortalIdentity>();
builder.Services.AddSingleton<TwoFactorPending>();
builder.Services.AddSingleton<HelpdeskStore>();
builder.Services.AddSingleton<SignInThrottle>();
builder.Services.AddSingleton<TemporaryPasswords>();
builder.Services.AddScoped<PortalSessionFilter>();
builder.Services.AddHostedService<BackupScheduler>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "EduHelpdeskAuth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = cookieSecurity;
        options.LoginPath = "/Login";
        options.AccessDeniedPath = "/AccessDenied";
        // Idle sliding expiration: stays signed in as long as the account is used at least once every 8 hours.
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        // Every request re-checks the account behind the cookie: gone, deactivated, password reset or past the 14-day
        // absolute cap ends the session; a changed role or name is picked up at once. See TechnicianSession.
        options.Events.OnValidatePrincipal = TechnicianSession.ValidateAsync;
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

// A new install's answers to the installer (school name, colours, logo, backups), applied once - see InstallSettings.
InstallSettings.Apply(app.Services.GetRequiredService<HelpdeskStore>(), dataLocation.Folder, app.Logger);

// Fonts for the project proposal PDF - see PdfFonts for where they come from.
PdfFonts.Install(app.Environment.ContentRootPath);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    if (requireHttps) app.UseHsts();
}
else
{
    // Development shows the whole error - stack trace, code and file paths. That is for the person debugging at this
    // machine, not for everyone reaching it over the network, so anyone else gets the ordinary error page.
    app.UseWhen(context => context.Connection.RemoteIpAddress is { } address && !System.Net.IPAddress.IsLoopback(address),
        branch => branch.UseExceptionHandler("/Error"));
}
if (requireHttps) app.UseHttpsRedirection();
app.UseSecurityHeaders();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

// The school's logo (Settings → Branding). Open to everyone, because the sign-in page and the staff portal show it
// too. Only a file that passed the PNG check in HelpdeskStore.SaveLogo is ever stored, and it is sent with headers
// that stop a browser treating it as anything but a picture. The address carries a version (?v=), so it can be
// cached hard and still change the moment a new logo is uploaded.
app.MapGet("/branding/logo", (HelpdeskStore store, HttpContext context) =>
{
    if (store.LogoFile is not { } path) return Results.NotFound();
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
    context.Response.Headers.CacheControl = context.Request.Query.ContainsKey("v") ? "public, max-age=31536000, immutable" : "no-cache";
    return Results.File(path, "image/png");
});
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
