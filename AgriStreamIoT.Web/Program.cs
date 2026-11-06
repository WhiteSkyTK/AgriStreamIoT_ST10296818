using System.Globalization;
using AgriStreamIoT.Web.Configuration;
using AgriStreamIoT.Web.Filters;
using AgriStreamIoT.Web.Middleware;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);

// Connection strings with passwords go in appsettings.Local.json (git-ignored, but published with
// the app). App Service "Environment variables" still win because they are added again afterwards.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();

builder.Services.AddControllersWithViews(options => options.Filters.Add<FarmExceptionFilter>());

// Telemetry to Azure Monitor (Application Insights). Inactive until a connection string is set.
builder.Services.AddApplicationInsightsTelemetry();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Cookie.Name = "AgriStream.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();

// Drone imagery is large: allow uploads of up to 512 MB per request.
const long maxUploadBytes = 512L * 1024 * 1024;
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = maxUploadBytes);
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = maxUploadBytes);

// Everything AgriStream: Azure SQL, Blob/Table/Queue storage, Functions client, seeding, simulator.
builder.Services.AddAgriStream(builder.Configuration);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// One culture for every visitor: dot decimals (HTML number inputs post "27.5") and day-month dates.
var farmCulture = new CultureInfo("en-GB");
app.UseRequestLocalization(options =>
{
    options.DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture(farmCulture);
    options.SupportedCultures = new[] { farmCulture };
    options.SupportedUICultures = new[] { farmCulture };
    options.RequestCultureProviders.Clear();
});

app.UseMiddleware<InstanceHeaderMiddleware>();   // X-Served-By header proves which instance answered
app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", utc = DateTimeOffset.UtcNow }));
app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
