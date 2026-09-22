using EduHelpdesk.Models;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// Room for a ticket upload of several attachments at once (each file is still limited to 10 MB in the store).
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 60_000_000);
builder.Services.AddRazorPages(options =>
{
    // Everyone must be signed in by default; individual pages/folders opt out or tighten further below.
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Login");
    options.Conventions.AllowAnonymousToPage("/AccessDenied");

    // Staff account/role management (part of the technician roster, but sensitive): Administrator or Senior Technician.
    options.Conventions.AuthorizePage("/People/Technician", "RequireSeniorLevel");
    options.Conventions.AuthorizePage("/People/DeleteTechnician", "RequireSeniorLevel");
    // Requester directory maintenance, assets, suppliers and parts: anyone but Junior Technician.
    options.Conventions.AuthorizePage("/People/User", "RequireStaffLevel");
    options.Conventions.AuthorizePage("/People/DeleteUser", "RequireStaffLevel");
    options.Conventions.AuthorizeFolder("/Assets", "RequireStaffLevel");
    options.Conventions.AuthorizeFolder("/Suppliers", "RequireStaffLevel");
    options.Conventions.AuthorizeFolder("/Parts", "RequireStaffLevel");
    // The Settings area (branding, option lists, CSV import, factory reset, audit log) is Administrator-only.
    options.Conventions.AuthorizePage("/Settings", "RequireAdministrator");
    options.Conventions.AuthorizeFolder("/Settings", "RequireAdministrator");
});
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<EduHelpdesk.Services.HelpdeskStore>();

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
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("RequireAdministrator", policy => policy.RequireRole(StaffRoles.Administrator))
    // Senior Technician has every permission Administrator has except Settings access (see the Settings page/folder policies above).
    .AddPolicy("RequireSeniorLevel", policy => policy.RequireRole(StaffRoles.Administrator, StaffRoles.SeniorTechnician))
    .AddPolicy("RequireStaffLevel", policy => policy.RequireRole(StaffRoles.Administrator, StaffRoles.SeniorTechnician, StaffRoles.Technician));

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
