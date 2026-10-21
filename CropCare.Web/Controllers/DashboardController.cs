using System.Net;
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
    private readonly IHttpClientFactory _clients;
    private readonly FirebaseTokenStore _tokens;
    private readonly ZoneOverviewService _zones;

    public DashboardController(IHttpClientFactory clients, FirebaseTokenStore tokens, ZoneOverviewService zones)
    {
        _clients = clients;
        _tokens = tokens;
        _zones = zones;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var session = User.FindFirst("CropCareSession")?.Value;
        var token = _tokens.GetIdToken(session);
        if (string.IsNullOrWhiteSpace(token)) return await Expired(session);
        var model = new CropCareDashboardViewModel();
        try
        {
            var client = _clients.CreateClient("CropCareApi");
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/account/me");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized) return await Expired(session);
            if (response.IsSuccessStatusCode)
                model.Profile = await response.Content.ReadFromJsonAsync<CropCareProfile>(cancellationToken: ct);
            model.Zones = await _zones.GetDashboardZonesAsync(token, ct);
        }
        catch (UnauthorizedAccessException) { return await Expired(session); }
        catch (HttpRequestException) { model.Error = "Some farm information could not be loaded. Check the API connection."; }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        { model.Error = "The API request timed out. Please refresh."; }
        return View(model);
    }

    private async Task<IActionResult> Expired(string? session)
    {
        _tokens.RemoveSession(session);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login", "Account");
    }
}