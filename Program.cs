using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using PulseBoardMigration.Services;
using PulseBoardMigration.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var keyDirectory = new DirectoryInfo(
    Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys"));
builder.Services.AddDataProtection()
    .SetApplicationName("PulseBoardMigration")
    .PersistKeysToFileSystem(keyDirectory);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<SupabaseClientFactory>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<BoardService>();
builder.Services.AddScoped<WorkspaceService>();
builder.Services.AddScoped<ReportingService>();
builder.Services.AddScoped<WorkManagementService>();
builder.Services.AddScoped<BillingService>();
builder.Services.AddScoped<EnterpriseService>();
builder.Services.AddScoped<BoardOperationsService>();

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Auth/Login";
        options.LogoutPath = "/Auth/Logout";
        options.AccessDeniedPath = "/Auth/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(PulsePolicies.AdminOnly, policy => policy.RequireRole("admin"));
    options.AddPolicy(PulsePolicies.ManagerOrAdmin, policy => policy.RequireRole("admin", "manager"));
    options.AddPolicy(PulsePolicies.FinanceAccess, policy => policy.RequireRole("admin", "manager"));
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

app.Run();
