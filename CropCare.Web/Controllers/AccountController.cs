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

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(
        LoginViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);
        try
        {
            var signIn = await _firebase.SignInAsync(model.Email.Trim(), model.Password, ct);
            if (signIn is null || string.IsNullOrWhiteSpace(signIn.IdToken))
            {
                ModelState.AddModelError("", "Invalid email or password.");
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

            var sessionId = _tokens.CreateSession(signIn.IdToken);
            var displayName = !string.IsNullOrWhiteSpace(profile.FirstName)
                ? profile.FirstName : profile.Username ?? model.Email;

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, profile.UserId),
                new(ClaimTypes.Name, displayName),
                new(ClaimTypes.Email, profile.Email ?? model.Email),
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
                    ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(50)
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

            TempData["Notice"] = "Registration successful! Please log in with your email and password.";
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
    public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(
        ForgotPasswordViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);
        try
        {
            var accepted = await _firebase.SendPasswordResetAsync(model.Email.Trim(), ct);
            if (!accepted)
            {
                ModelState.AddModelError("", "Password recovery is temporarily unavailable. Try again later.");
                return View(model);
            }

            // Same response whether or not the email is registered.
            ViewBag.Message = "If this email has an account, Firebase will send a password reset link. Check your inbox and spam folder.";
            return View(new ForgotPasswordViewModel());
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "CropCare password recovery network failure");
            ModelState.AddModelError("", "Recovery is temporarily unavailable.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            ModelState.AddModelError("", "Recovery request timed out.");
        }
        return View(model);
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        _tokens.RemoveSession(User.FindFirst("CropCareSession")?.Value);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }
}
