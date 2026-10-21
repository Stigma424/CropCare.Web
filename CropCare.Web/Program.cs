
using CropCare.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// MVC controllers and Razor views
builder.Services.AddControllersWithViews();

// Temporary server-side token storage
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<FirebaseTokenStore>();

// Existing CropCare ASP.NET Core API
builder.Services.AddHttpClient("CropCareApi", client =>
{
    var apiUrl = builder.Configuration["CropCareApi:BaseUrl"]
        ?? throw new InvalidOperationException(
            "CropCare API BaseUrl is missing.");

    client.BaseAddress = new Uri(apiUrl);
    client.Timeout = TimeSpan.FromSeconds(15);
});

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

app.UseAuthentication();
app.UseAuthorization();

// Dashboard is the default route.
// Unauthenticated visitors are redirected to Login.
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

app.Run();
