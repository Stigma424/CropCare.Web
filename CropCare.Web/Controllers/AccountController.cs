
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
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
    private readonly IHttpClientFactory _httpClients;
    private readonly FirebaseTokenStore _tokenStore;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        FirebaseAuthRestClient firebase,
        IHttpClientFactory httpClients,
        FirebaseTokenStore tokenStore,
        ILogger<AccountController> logger)
    {
        _firebase = firebase;
        _httpClients = httpClients;
        _tokenStore = tokenStore;
        _logger = logger;
    }

    // GET: /Account/Login
    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Dashboard");
        }

        return View(new LoginViewModel
        {
            ReturnUrl = returnUrl
        });
    }

    // POST: /Account/Login
    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(
        LoginViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            // Step 1: Authenticate with Firebase
            var signIn = await _firebase.SignInAsync(
                model.Email.Trim(),
                model.Password,
                cancellationToken);

            if (signIn == null ||
                string.IsNullOrWhiteSpace(signIn.IdToken))
            {
                ModelState.AddModelError(
                    "",
                    "Invalid email or password.");

                return View(model);
            }

            // Step 2: Verify user through existing API
            var client = _httpClients.CreateClient("CropCareApi");

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                "api/account/me");

            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    signIn.IdToken);

            using var response = await client.SendAsync(
                request,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                ModelState.AddModelError(
                    "",
                    "Firebase login succeeded, but the " +
                    "CropCare profile could not be loaded. " +
                    "Check the API and Firestore user record.");

                return View(model);
            }

            // Step 3: Get Firestore profile
            var profile = await response.Content
                .ReadFromJsonAsync<CropCareProfile>(
                    cancellationToken: cancellationToken);

            if (profile == null ||
                string.IsNullOrWhiteSpace(profile.UserId) ||
                profile.UserId != signIn.LocalId)
            {
                ModelState.AddModelError(
                    "",
                    "CropCare account verification failed.");

                return View(model);
            }

            // Step 4: Store token on MVC server
            var sessionId = _tokenStore.CreateSession(
                signIn.IdToken);

            var displayName =
                !string.IsNullOrWhiteSpace(profile.FirstName)
                    ? profile.FirstName
                    : (profile.Username ?? model.Email);

            // Step 5: Create user claims
            var claims = new List<Claim>
            {
                new Claim(
                    ClaimTypes.NameIdentifier,
                    profile.UserId),

                new Claim(
                    ClaimTypes.Name,
                    displayName),

                new Claim(
                    ClaimTypes.Email,
                    profile.Email ?? model.Email),

                new Claim(
                    "CropCareSession",
                    sessionId)
            };

            var identity = new ClaimsIdentity(
                claims,
                CookieAuthenticationDefaults.AuthenticationScheme);

            var principal = new ClaimsPrincipal(identity);

            // Step 6: Create protected login cookie
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties
                {
                    IsPersistent = false,
                    AllowRefresh = false,
                    ExpiresUtc = DateTimeOffset.UtcNow
                        .AddMinutes(50)
                });

            // Step 7: Redirect to protected dashboard
            if (Url.IsLocalUrl(model.ReturnUrl))
            {
                return LocalRedirect(model.ReturnUrl!);
            }

            return RedirectToAction(
                "Index",
                "Dashboard");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                ex,
                "Firebase or CropCare API unavailable.");

            ModelState.AddModelError(
                "",
                "Cannot reach Firebase or CropCare API.");

            return View(model);
        }
        catch (TaskCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            ModelState.AddModelError(
                "",
                "The login request timed out.");

            return View(model);
        }
    }

    // POST: /Account/Logout
    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        var sessionId = User.FindFirst(
            "CropCareSession")?.Value;

        _tokenStore.RemoveSession(sessionId);

        await HttpContext.SignOutAsync(
            CookieAuthenticationDefaults.AuthenticationScheme);

        return RedirectToAction(nameof(Login));
    }
}
