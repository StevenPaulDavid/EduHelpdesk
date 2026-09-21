var builder = WebApplication.CreateBuilder(args);

// Room for a ticket upload of several attachments at once (each file is still limited to 10 MB in the store).
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 60_000_000);
builder.Services.AddRazorPages();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<EduHelpdesk.Services.HelpdeskStore>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
