
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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

    public AdvisoriesController(
        IHttpClientFactory clients,
        FirebaseTokenStore tokens)
    {
        _clients = clients;
        _tokens = tokens;
    }

    // ==========================================
    // PRO-ONLY HISTORICAL DEBUGGING PAGE
    // ==========================================

    [HttpGet]
    public async Task<IActionResult> Index(
        string? zoneId,
        CancellationToken ct)
    {
        var model = new ZoneAssessmentPageModel
        {
            SelectedZoneId = zoneId,
            Mode = "history"
        };

        var (token, session) = CurrentToken();

        if (token == null)
            return await Expired(session);

        try
        {
            var api = _clients.CreateClient("CropCareApi");

            var (status, isPro) =
                await CheckProAsync(api, token, ct);

            if (status == HttpStatusCode.Unauthorized)
                return await Expired(session);

            if (status != HttpStatusCode.OK)
            {
                model.Error = "Unable to verify Pro membership.";
                return View(model);
            }

            if (!isPro)
            {
                TempData["Error"] =
                    "CropCare Pro is required for historical advisories.";

                return RedirectToAction(
                    "Index", "Subscription");
            }

            // Load the logged-in farmer's zones.
            using var zonesRequest = Authorized(
                HttpMethod.Get, "api/zones", token);

            using var zonesResponse =
                await api.SendAsync(zonesRequest, ct);

            if (zonesResponse.StatusCode ==
                HttpStatusCode.Unauthorized)
                return await Expired(session);

            if (!zonesResponse.IsSuccessStatusCode)
            {
                model.Error = "Unable to load your zones.";
                return View(model);
            }

            model.Zones = await zonesResponse.Content
                .ReadFromJsonAsync<List<ZoneRecord>>(
                    cancellationToken: ct) ?? new();

            if (model.Zones.Count == 0)
                return View(model);

            model.SelectedZoneId ??=
                model.Zones[0].ZoneId;

            if (!model.Zones.Any(z =>
                z.ZoneId == model.SelectedZoneId))
            {
                model.Error = "Select one of your zones.";
                return View(model);
            }

            string path =
                "api/zones/" +
                Uri.EscapeDataString(model.SelectedZoneId) +
                "/assessment/historical-preview";

            using var request = Authorized(
                HttpMethod.Get, path, token);

            using var response =
                await api.SendAsync(request, ct);

            if (response.StatusCode ==
                HttpStatusCode.Unauthorized)
                return await Expired(session);

            if (response.StatusCode ==
                HttpStatusCode.Forbidden)
            {
                TempData["Error"] =
                    "CropCare Pro is required.";

                return RedirectToAction(
                    "Index", "Subscription");
            }

            if (!response.IsSuccessStatusCode)
            {
                model.Error =
                    "Historical assessment is unavailable.";
                return View(model);
            }

            model.Preview = await response.Content
                .ReadFromJsonAsync<HistoricalPreviewDto>(
                    cancellationToken: ct);

            model.Assessment = model.Preview?.Assessment;
        }
        catch (HttpRequestException)
        {
            model.Error = "Cannot connect to CropCare.Api.";
        }
        catch (TaskCanceledException)
            when (!ct.IsCancellationRequested)
        {
            model.Error = "Assessment request timed out.";
        }
        catch (JsonException)
        {
            model.Error =
                "The API returned an unexpected assessment format.";
        }

        return View(model);
    }

    // ==========================================
    // LIVE RECOMMENDATION POPUP
    // ==========================================

    // GET /Advisories/Popup?zoneId=...
    [HttpGet]
    [ResponseCache(
        NoStore = true,
        Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Popup(
        string? zoneId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(zoneId) ||
            zoneId.Length > 256 ||
            zoneId.Contains('/'))
        {
            return BadRequest("Invalid zone ID.");
        }

        var (token, session) = CurrentToken();

        if (token == null)
            return Unauthorized("Login session expired.");

        try
        {
            var api = _clients.CreateClient("CropCareApi");

            // Enforce Pro on the server.
            var (status, isPro) =
                await CheckProAsync(api, token, ct);

            if (status == HttpStatusCode.Unauthorized)
                return Unauthorized("Login session expired.");

            if (status != HttpStatusCode.OK)
                return StatusCode(
                    503, "Cannot verify subscription.");

            if (!isPro)
                return StatusCode(
                    403, "CropCare Pro required.");

            string path =
                "api/zones/" +
                Uri.EscapeDataString(zoneId) +
                "/assessment?refresh=true";

            using var request = Authorized(
                HttpMethod.Get, path, token);

            using var response =
                await api.SendAsync(request, ct);

            if (response.StatusCode ==
                HttpStatusCode.Unauthorized)
                return Unauthorized("Login session expired.");

            if (response.StatusCode ==
                HttpStatusCode.Forbidden)
                return StatusCode(
                    403, "CropCare Pro required.");

            if (response.StatusCode ==
                HttpStatusCode.NotFound)
                return NotFound("Zone not found.");

            if (!response.IsSuccessStatusCode)
                return StatusCode(
                    503, "Recommendation unavailable.");

            var assessment = await response.Content
                .ReadFromJsonAsync<ZoneAssessmentDto>(
                    cancellationToken: ct);

            if (assessment == null)
                return StatusCode(
                    503, "Assessment is empty.");

            return PartialView(
                "_RecommendationPopup",
                assessment);
        }
        catch (HttpRequestException)
        {
            return StatusCode(
                503, "Cannot connect to CropCare.Api.");
        }
        catch (TaskCanceledException)
            when (!ct.IsCancellationRequested)
        {
            return StatusCode(
                504, "Recommendation request timed out.");
        }
        catch (JsonException)
        {
            return StatusCode(
                503, "Unexpected assessment response.");
        }
    }

    // ==========================================
    // MEMBERSHIP AND SESSION HELPERS
    // ==========================================

    private async Task<(HttpStatusCode Status, bool IsPro)>
        CheckProAsync(
            HttpClient api,
            string token,
            CancellationToken ct)
    {
        using var request = Authorized(
            HttpMethod.Get,
            "api/subscriptions/me",
            token);

        using var response =
            await api.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
            return (response.StatusCode, false);

        var membership = await response.Content
            .ReadFromJsonAsync<SubscriptionStatusDto>(
                cancellationToken: ct);

        return (
            HttpStatusCode.OK,
            membership?.IsSubscribed == true
        );
    }

    private (string? Token, string? Session)
        CurrentToken()
    {
        var session =
            User.FindFirst("CropCareSession")?.Value;

        return (_tokens.GetIdToken(session), session);
    }

    private static HttpRequestMessage Authorized(
        HttpMethod method,
        string path,
        string token)
    {
        var request =
            new HttpRequestMessage(method, path);

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer", token);

        return request;
    }

    private async Task<IActionResult> Expired(
        string? session)
    {
        _tokens.RemoveSession(session);

        await HttpContext.SignOutAsync(
            CookieAuthenticationDefaults.AuthenticationScheme);

        TempData["Error"] =
            "Your session expired. Please log in again.";

        return RedirectToAction("Login", "Account");
    }
}
