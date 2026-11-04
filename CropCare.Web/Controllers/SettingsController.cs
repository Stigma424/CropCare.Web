using CropCare.Web.Models;
using CropCare.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
namespace CropCare.Web.Controllers;
[Authorize]
public sealed class SettingsController(IHttpClientFactory clients, FirebaseTokenStore tokens) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var session = User.FindFirst("CropCareSession")?.Value;
        var token = tokens.GetIdToken(session);
        if (token is null) return await Expired(session);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/account/me");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await clients.CreateClient("CropCareApi").SendAsync(request, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized) return await Expired(session);
            if (response.IsSuccessStatusCode) return View(await response.Content.ReadFromJsonAsync<CropCareProfile>(cancellationToken: ct));
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { }
        ViewData["ProfileError"] = "Profile unavailable. Please refresh to try again.";
        return View(new CropCareProfile());
    }
    public IActionResult Help() => View();
    private async Task<IActionResult> Expired(string? session)
    {
        tokens.RemoveSession(session); await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login", "Account");
    }
}
