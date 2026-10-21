
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CropCare.Web.Models;
using CropCare.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CropCare.Web.Controllers;

[Authorize]
public sealed class DashboardController : Controller
{
    private readonly IHttpClientFactory _httpClients;
    private readonly FirebaseTokenStore _tokenStore;

    public DashboardController(
        IHttpClientFactory httpClients,
        FirebaseTokenStore tokenStore)
    {
        _httpClients = httpClients;
        _tokenStore = tokenStore;
    }

    // GET: /Dashboard
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken)
    {
        var sessionId = User.FindFirst(
            "CropCareSession")?.Value;

        var idToken = _tokenStore.GetIdToken(sessionId);

        // Require a valid stored Firebase token
        if (idToken == null)
        {
            await HttpContext.SignOutAsync(
                CookieAuthenticationDefaults.AuthenticationScheme);

            return RedirectToAction(
                "Login",
                "Account");
        }

        try
        {
            var client = _httpClients.CreateClient(
                "CropCareApi");

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                "api/account/me");

            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    idToken);

            using var response = await client.SendAsync(
                request,
                cancellationToken);

            // Expired or rejected Firebase token
            if (response.StatusCode ==
                System.Net.HttpStatusCode.Unauthorized)
            {
                _tokenStore.RemoveSession(sessionId);

                await HttpContext.SignOutAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme);

                return RedirectToAction(
                    "Login",
                    "Account");
            }

            if (!response.IsSuccessStatusCode)
            {
                ViewBag.Status =
                    "Profile temporarily unavailable.";

                return View((CropCareProfile?)null);
            }

            var profile = await response.Content
                .ReadFromJsonAsync<CropCareProfile>(
                    cancellationToken: cancellationToken);

            return View(profile);
        }
        catch (HttpRequestException)
        {
            ViewBag.Status =
                "Could not connect to CropCare API.";

            return View((CropCareProfile?)null);
        }
        catch (TaskCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            ViewBag.Status =
                "CropCare API request timed out.";

            return View((CropCareProfile?)null);
        }
    }
}
