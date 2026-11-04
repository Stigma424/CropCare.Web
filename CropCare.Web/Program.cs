using CropCare.Web.Services;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// MVC controllers and Razor views
builder.Services.AddControllersWithViews(options => { options.Filters.Add<ApiPageExceptionFilter>(); options.Filters.Add<ApiSessionResultFilter>(); });

// Per-request zone summaries using existing authenticated API endpoints.
builder.Services.AddScoped<ZoneOverviewService>();

// Temporary server-side token storage
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<FirebaseTokenStore>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<ApiSessionHandler>();
// Existing CropCare ASP.NET Core API
builder.Services.AddHttpClient("CropCareApi", client =>
{
    var apiUrl = builder.Configuration["CropCareApi:BaseUrl"]
        ?? throw new InvalidOperationException(
            "CropCare API BaseUrl is missing.");

    client.BaseAddress = new Uri(apiUrl);
    client.Timeout = TimeSpan.FromSeconds(15);
}).AddHttpMessageHandler<ApiSessionHandler>();

// Firebase Authentication REST API
builder.Services.AddHttpClient<FirebaseAuthRestClient>(client =>
{
    client.BaseAddress = new Uri(
        "https://identitytoolkit.googleapis.com/");

    client.Timeout = TimeSpan.FromSeconds(20);
});

// Website authentication cookie
builder.Services
    .AddAuthentication(
        CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "CropCare.Web.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;

        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Login";

        options.ExpireTimeSpan = TimeSpan.FromMinutes(50);
        options.SlidingExpiration = false;

        options.Events.OnRedirectToLogin = context => {
            if (context.Request.Headers["X-CropCare-Partial"] == "true") context.Response.StatusCode = 401;
            else context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        // Reject sessions that no longer have a stored token
        options.Events.OnValidatePrincipal = async context =>
        {
            var sessionId = context.Principal?
                .FindFirst("CropCareSession")?.Value;

            var tokenStore = context.HttpContext
                .RequestServices
                .GetRequiredService<FirebaseTokenStore>();

            if (string.IsNullOrWhiteSpace(sessionId) ||
                tokenStore.GetIdToken(sessionId) == null)
            {
                context.RejectPrincipal();

                await context.HttpContext.SignOutAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options => {
    options.RejectionStatusCode = 429;
    options.AddPolicy("web-login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions {
            PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
        }));
});

var app = builder.Build();

// Error handling
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

// Dashboard is the default route.
// Unauthenticated visitors are redirected to Login.
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

app.Run();
public partial class Program { }
