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
public sealed class AdvisoriesController : Controller
{
    private readonly IHttpClientFactory _clients;
    private readonly FirebaseTokenStore _tokens;

    public AdvisoriesController(IHttpClientFactory clients, FirebaseTokenStore tokens)
    {
        _clients = clients;
        _tokens = tokens;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? zoneId, CancellationToken ct)
    {
        var model = new AdvisoriesPageModel { SelectedZoneId = zoneId };
        var session = User.FindFirst("CropCareSession")?.Value;
        var token = _tokens.GetIdToken(session);
        if (string.IsNullOrEmpty(token)) return await Expired(session);

        try
        {
            var api = _clients.CreateClient("CropCareApi");
            using var zonesRequest = Authorized(HttpMethod.Get, "api/zones", token);
            using var zonesResponse = await api.SendAsync(zonesRequest, ct);
            if (zonesResponse.StatusCode == HttpStatusCode.Unauthorized)
                return await Expired(session);
            if (!zonesResponse.IsSuccessStatusCode)
            {
                model.Error = "Could not load your zones.";
                return View(model);
            }

            model.Zones = await zonesResponse.Content
                .ReadFromJsonAsync<List<ZoneRecord>>(cancellationToken: ct) ?? new();
            if (model.Zones.Count == 0) return View(model);

            if (string.IsNullOrWhiteSpace(zoneId))
                model.SelectedZoneId = model.Zones[0].ZoneId;

            // Prevent an unauthorized zone ID being sent to the API by mistake.
            if (!model.Zones.Any(z => z.ZoneId == model.SelectedZoneId))
            {
                model.Error = "Please select a zone belonging to your account.";
                return View(model);
            }

            var path = "api/zones/" + Uri.EscapeDataString(model.SelectedZoneId!) + "/advisory";
            using var request = Authorized(HttpMethod.Get, path, token);
            using var response = await api.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized) return await Expired(session);
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                TempData["Error"] = "CropCare Pro is required for the prototype advisory. Your zone readings remain free.";
                return RedirectToAction("Index", "Subscription");
            }
            if (!response.IsSuccessStatusCode)
            {
                model.Error = "Prototype advisory is temporarily unavailable.";
                return View(model);
            }
            model.Advisory = await response.Content
                .ReadFromJsonAsync<ZoneAdvisory>(cancellationToken: ct);
        }
        catch (HttpRequestException)
        {
            model.Error = "Could not connect to the CropCare API.";
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            model.Error = "The API took too long to respond.";
        }
        return View(model);
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async Task<IActionResult> Expired(string? session)
    {
        _tokens.RemoveSession(session);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        TempData["Error"] = "Your session expired. Please log in again.";
        return RedirectToAction("Login", "Account");
    }
}