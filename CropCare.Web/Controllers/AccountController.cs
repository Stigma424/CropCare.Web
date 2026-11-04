using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using CropCare.Web.Models;
using CropCare.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CropCare.Web.Controllers;

public sealed class AccountController : Controller
{
    private readonly FirebaseAuthRestClient _firebase;
    private readonly IHttpClientFactory _clients;
    private readonly FirebaseTokenStore _tokens;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        FirebaseAuthRestClient firebase,
        IHttpClientFactory clients,
        FirebaseTokenStore tokens,
        ILogger<AccountController> logger)
    {
        _firebase = firebase;
        _clients = clients;
        _tokens = tokens;
        _logger = logger;
    }

    [AllowAnonymous, HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Dashboard");
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("web-login")]
    public async Task<IActionResult> Login(
        LoginViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);
        try
        {
            using var authentication = await _clients.CreateClient("CropCareApi").PostAsJsonAsync("api/auth/login",
                new { username = model.Username.Trim(), password = model.Password }, ct);
            if (!authentication.IsSuccessStatusCode)
            {
                ModelState.AddModelError("", authentication.StatusCode == System.Net.HttpStatusCode.Unauthorized
                    ? "Invalid username or password." : authentication.StatusCode == System.Net.HttpStatusCode.TooManyRequests
                    ? "Too many attempts. Please wait a minute and try again." : "Sign-in is temporarily unavailable. Please try again later.");
                return View(model);
            }
            var grant = await authentication.Content.ReadFromJsonAsync<UsernameLoginGrant>(cancellationToken: ct);
            var signIn = string.IsNullOrWhiteSpace(grant?.CustomToken) ? null : await _firebase.ExchangeCustomTokenAsync(grant.CustomToken, ct);
            if (signIn is not null && grant is not null) signIn.LocalId = grant.UserId;
            if (signIn is null || string.IsNullOrWhiteSpace(signIn.IdToken) || string.IsNullOrWhiteSpace(signIn.LocalId))
            {
                ModelState.AddModelError("", "Invalid username or password.");
                return View(model);
            }

            var client = _clients.CreateClient("CropCareApi");
            using var req = new HttpRequestMessage(HttpMethod.Get, "api/account/me");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", signIn.IdToken);
            using var res = await client.SendAsync(req, ct);

            if (!res.IsSuccessStatusCode)
            {
                ModelState.AddModelError("", "Account verified, but the CropCare profile is missing or unavailable.");
                return View(model);
            }

            var profile = await res.Content.ReadFromJsonAsync<CropCareProfile>(cancellationToken: ct);
            if (profile is null || profile.UserId != signIn.LocalId)
            {
                ModelState.AddModelError("", "Profile verification failed.");
                return View(model);
            }

            var lifetime = WebSessionPolicy.Lifetime(signIn.ExpiresInSeconds);
            if (lifetime <= TimeSpan.Zero) { ModelState.AddModelError("", "Sign-in expired. Please try again."); return View(model); }
            var sessionId = _tokens.CreateSession(signIn.IdToken, lifetime);
            var displayName = !string.IsNullOrWhiteSpace(profile.FirstName)
                ? profile.FirstName : profile.Username ?? model.Username;

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, profile.UserId),
                new(ClaimTypes.Name, displayName),
                new(ClaimTypes.Email, profile.Email ?? ""),
                new("CropCareSession", sessionId)
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(new ClaimsIdentity(
                    claims, CookieAuthenticationDefaults.AuthenticationScheme)),
                new AuthenticationProperties
                {
                    IsPersistent = false,
                    AllowRefresh = false,
                    ExpiresUtc = DateTimeOffset.UtcNow.Add(lifetime)
                });

            if (Url.IsLocalUrl(model.ReturnUrl)) return LocalRedirect(model.ReturnUrl!);
            return RedirectToAction("Index", "Dashboard");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "CropCare login network failure");
            ModelState.AddModelError("", "Cannot reach Firebase or CropCare API.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            ModelState.AddModelError("", "Login timed out. Please try again.");
        }
        return View(model);
    }

    [AllowAnonymous, HttpGet]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Dashboard");
        return View(new RegisterViewModel());
    }

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);
        try
        {
            // Create Firebase Auth account, or sign back into an unfinished one.
            var user = await _firebase.SignUpAsync(model.Email.Trim(), model.Password, ct);
            if (user is null || string.IsNullOrWhiteSpace(user.IdToken))
            {
                ModelState.AddModelError("", "Cannot create account. The email may already be registered, or the password may be invalid.");
                return View(model);
            }

            var client = _clients.CreateClient("CropCareApi");
            async Task<bool> ProfileExists()
            {
                using var check = new HttpRequestMessage(HttpMethod.Get, "api/account/me");
                check.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.IdToken);
                using var reply = await client.SendAsync(check, ct);
                if (reply.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
                reply.EnsureSuccessStatusCode();
                var existing = await reply.Content.ReadFromJsonAsync<CropCareProfile>(cancellationToken: ct);
                if (existing?.UserId != user.LocalId) throw new HttpRequestException("Profile ownership could not be verified.");
                return true;
            }
            if (await ProfileExists())
            {
                TempData["Notice"] = "Your profile is already complete. Sign in with your username and password.";
                return RedirectToAction(nameof(Login));
            }
            using var req = new HttpRequestMessage(HttpMethod.Post, "api/onboarding/profile");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.IdToken);
            req.Content = JsonContent.Create(new
            {
                firstName = model.FirstName.Trim(),
                lastName = model.LastName.Trim(),
                address = model.Address.Trim(),
                username = model.Username.Trim()
            });
            using var res = await client.SendAsync(req, ct);

            if (res.StatusCode == System.Net.HttpStatusCode.Conflict && await ProfileExists())
                return RedirectToAction(nameof(Login));
            if (!res.IsSuccessStatusCode)
            {
                var error = "Could not finish the CropCare profile. Retry registration with the same email/password.";
                try
                {
                    using var doc = await JsonDocument.ParseAsync(
                        await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                    if (doc.RootElement.TryGetProperty("error", out var apiError))
                        error = apiError.GetString() ?? error;
                }
                catch (JsonException) { }
                ModelState.AddModelError("", error);
                return View(model);
            }

            TempData["Notice"] = "Registration successful! Please log in with your username and password.";
            return RedirectToAction(nameof(Login));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "CropCare registration network failure");
            ModelState.AddModelError("", "Service unavailable. If Firebase created your account, retry with the same email and password.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            ModelState.AddModelError("", "Registration timed out. You may retry using the same email and password.");
        }
        return View(model);
    }

    [AllowAnonymous, HttpGet]
    public IActionResult ForgotPassword() => RedirectToAction("Request", "Recovery");
    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public IActionResult ForgotPassword(string? email) => RedirectToAction("Request", "Recovery");
    [Authorize, HttpGet]
    public IActionResult ConfirmLogout() => View();

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        _tokens.RemoveSession(User.FindFirst("CropCareSession")?.Value);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }
}
