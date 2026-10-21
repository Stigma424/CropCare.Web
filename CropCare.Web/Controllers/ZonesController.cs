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
public sealed class ZonesController : Controller
{
    private readonly IHttpClientFactory _clients;
    private readonly FirebaseTokenStore _tokens;
    private readonly ZoneOverviewService _overview;

    public ZonesController(IHttpClientFactory clients, FirebaseTokenStore tokens, ZoneOverviewService overview)
    {
        _clients = clients;
        _tokens = tokens;
        _overview = overview;
    }

    private HttpRequestMessage ApiRequest(HttpMethod method, string path, object? body = null)
    {
        var sessionId = User.FindFirst("CropCareSession")?.Value;
        var idToken = _tokens.GetIdToken(sessionId);
        if (string.IsNullOrWhiteSpace(idToken))
            throw new UnauthorizedAccessException("Login session expired.");

        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
        if (body != null)
            request.Content = JsonContent.Create(body);
        return request;
    }

    private static string PathFor(string id) => "api/zones/" + Uri.EscapeDataString(id);

    private async Task<IActionResult> LoginAgain()
    {
        var sessionId = User.FindFirst("CropCareSession")?.Value;
        _tokens.RemoveSession(sessionId);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        TempData["Error"] = "Your session has expired. Please log in again.";
        return RedirectToAction("Login", "Account");
    }

    private static async Task<string> ReadApiError(HttpResponseMessage response, string fallback, CancellationToken ct)
    {
        try
        {
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            if (json.ValueKind == JsonValueKind.Object &&
                json.TryGetProperty("error", out var error) &&
                error.ValueKind == JsonValueKind.String)
            {
                var message = error.GetString();
                if (!string.IsNullOrWhiteSpace(message)) return message;
            }
        }
        catch (JsonException) { }
        catch (NotSupportedException) { }
        return fallback;
    }

    private IActionResult ApiUnavailable()
    {
        TempData["Error"] = "CropCare API is unavailable. Try again in a moment.";
        return RedirectToAction(nameof(Index));
    }

    private static bool TryReadDate(DateTime? date, out long milliseconds)
    {
        milliseconds = 0;
        if (!date.HasValue || date.Value.Year < 1970 || date.Value.Year > 2099)
            return false;
        milliseconds = ZoneDates.ToUnixMilliseconds(date.Value);
        return true;
    }

    // GET: /Zones
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        try
        {
            using var request = ApiRequest(HttpMethod.Get, "api/zones");
            using var response = await _clients.CreateClient("CropCareApi").SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized) return await LoginAgain();
            if (!response.IsSuccessStatusCode)
            {
                ViewBag.Error = await ReadApiError(response, "Unable to load zones.", ct);
                return View(new List<ZoneRecord>());
            }
            var zones = await response.Content.ReadFromJsonAsync<List<ZoneRecord>>(cancellationToken: ct)
                        ?? new List<ZoneRecord>();
            return View(zones.OrderBy(z => z.ZoneName, StringComparer.OrdinalIgnoreCase).ToList());
        }
        catch (UnauthorizedAccessException) { return await LoginAgain(); }
        catch (HttpRequestException) { ViewBag.Error = "Cannot connect to CropCare API."; }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        { ViewBag.Error = "CropCare API request timed out."; }
        return View(new List<ZoneRecord>());
    }

    // GET: /Zones/Details/{id}
    [HttpGet]
    public async Task<IActionResult> Details(string id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id)) return RedirectToAction(nameof(Index));
        try
        {
            using var request = ApiRequest(HttpMethod.Get, PathFor(id));
            using var response = await _clients.CreateClient("CropCareApi").SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized) return await LoginAgain();
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                TempData["Error"] = "Zone not found or does not belong to your account.";
                return RedirectToAction(nameof(Index));
            }
            if (!response.IsSuccessStatusCode)
            {
                TempData["Error"] = await ReadApiError(response, "Unable to load this zone.", ct);
                return RedirectToAction(nameof(Index));
            }
            var zone = await response.Content.ReadFromJsonAsync<ZoneRecord>(cancellationToken: ct);
            if (zone == null) return RedirectToAction(nameof(Index));
            try
            {
                var token = _tokens.GetIdToken(User.FindFirst("CropCareSession")?.Value);
                if (string.IsNullOrWhiteSpace(token)) return await LoginAgain();
                var details = await _overview.GetZoneOverviewAsync(zone, token, ct);
                return View(details);
            }
            catch (HttpRequestException)
            {
                return View(new ZoneOverview { Zone = zone, Error = "Sensor readings are temporarily unavailable." });
            }
        }
        catch (UnauthorizedAccessException) { return await LoginAgain(); }
        catch (HttpRequestException) { return ApiUnavailable(); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return ApiUnavailable(); }
    }

    // GET: /Zones/Create
    [HttpGet]
    public IActionResult Create() => View(new ZoneFormViewModel
    {
        PlantingDate = ZoneDates.Today
    });

    // POST: /Zones/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ZoneFormViewModel model, CancellationToken ct)
    {
        model.ZoneName = model.ZoneName?.Trim() ?? "";
        if (!TryReadDate(model.PlantingDate, out long dateMillis))
            ModelState.AddModelError(nameof(model.PlantingDate), "Select a date between 1970 and 2099.");
        if (!ModelState.IsValid) return View(model);

        try
        {
            using var request = ApiRequest(HttpMethod.Post, "api/zones", new
            {
                zoneName = model.ZoneName,
                zoneAreaSqm = model.ZoneAreaSqm,
                dateOfPlanting = dateMillis
            });
            using var response = await _clients.CreateClient("CropCareApi").SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized) return await LoginAgain();
            if (!response.IsSuccessStatusCode)
            {
                ModelState.AddModelError("", await ReadApiError(response, "Unable to create zone.", ct));
                return View(model);
            }
            var created = await response.Content.ReadFromJsonAsync<ZoneRecord>(cancellationToken: ct);
            TempData["Success"] = "Farm zone created successfully.";
            return created != null && !string.IsNullOrWhiteSpace(created.ZoneId)
                ? RedirectToAction(nameof(Details), new { id = created.ZoneId })
                : RedirectToAction(nameof(Index));
        }
        catch (UnauthorizedAccessException) { return await LoginAgain(); }
        catch (HttpRequestException) { ModelState.AddModelError("", "Cannot reach CropCare API."); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        { ModelState.AddModelError("", "API request timed out."); }
        return View(model);
    }

    // GET: /Zones/Edit/{id}
    [HttpGet]
    public async Task<IActionResult> Edit(string id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id)) return RedirectToAction(nameof(Index));
        try
        {
            using var request = ApiRequest(HttpMethod.Get, PathFor(id));
            using var response = await _clients.CreateClient("CropCareApi").SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized) return await LoginAgain();
            if (!response.IsSuccessStatusCode)
            {
                TempData["Error"] = await ReadApiError(response, "Zone not found.", ct);
                return RedirectToAction(nameof(Index));
            }
            var zone = await response.Content.ReadFromJsonAsync<ZoneRecord>(cancellationToken: ct);
            if (zone == null) return RedirectToAction(nameof(Index));
            return View(new ZoneFormViewModel
            {
                ZoneId = zone.ZoneId,
                ZoneName = zone.ZoneName,
                ZoneAreaSqm = zone.ZoneAreaSqm,
                PlantingDate = zone.PlantingDate
            });
        }
        catch (UnauthorizedAccessException) { return await LoginAgain(); }
        catch (HttpRequestException) { return ApiUnavailable(); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return ApiUnavailable(); }
    }

    // POST: /Zones/Edit/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, ZoneFormViewModel model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id)) return RedirectToAction(nameof(Index));
        model.ZoneId = id;
        model.ZoneName = model.ZoneName?.Trim() ?? "";
        if (!TryReadDate(model.PlantingDate, out long dateMillis))
            ModelState.AddModelError(nameof(model.PlantingDate), "Select a date between 1970 and 2099.");
        if (!ModelState.IsValid) return View(model);

        try
        {
            using var request = ApiRequest(HttpMethod.Patch, PathFor(id), new
            {
                zoneName = model.ZoneName,
                zoneAreaSqm = model.ZoneAreaSqm,
                dateOfPlanting = dateMillis
            });
            using var response = await _clients.CreateClient("CropCareApi").SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized) return await LoginAgain();
            if (!response.IsSuccessStatusCode)
            {
                ModelState.AddModelError("", await ReadApiError(response, "Unable to update zone.", ct));
                return View(model);
            }
            TempData["Success"] = "Zone details updated.";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (UnauthorizedAccessException) { return await LoginAgain(); }
        catch (HttpRequestException) { ModelState.AddModelError("", "Cannot reach CropCare API."); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        { ModelState.AddModelError("", "API request timed out."); }
        return View(model);
    }

    // POST: /Zones/Harvest/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Harvest(string id, CancellationToken ct)
    {
        return await PatchStatus(id, new { isHarvested = true },
            "Zone marked as harvested.", ct);
    }

    // POST: /Zones/Replant/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Replant(string id, DateTime? plantingDate, CancellationToken ct)
    {
        if (!TryReadDate(plantingDate, out long dateMillis))
        {
            TempData["Error"] = "Enter a valid new planting date (1970–2099).";
            return RedirectToAction(nameof(Details), new { id });
        }
        return await PatchStatus(id, new
        {
            dateOfPlanting = dateMillis,
            isHarvested = false
        }, "New planting cycle started.", ct);
    }

    private async Task<IActionResult> PatchStatus(string id, object payload,
        string successMessage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id)) return RedirectToAction(nameof(Index));
        try
        {
            using var request = ApiRequest(HttpMethod.Patch, PathFor(id), payload);
            using var response = await _clients.CreateClient("CropCareApi").SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized) return await LoginAgain();
            if (!response.IsSuccessStatusCode)
                TempData["Error"] = await ReadApiError(response, "Unable to update planting status.", ct);
            else
                TempData["Success"] = successMessage;
        }
        catch (UnauthorizedAccessException) { return await LoginAgain(); }
        catch (HttpRequestException) { TempData["Error"] = "Cannot reach CropCare API."; }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        { TempData["Error"] = "API request timed out."; }
        return RedirectToAction(nameof(Details), new { id });
    }

    // POST: /Zones/Delete/{id}. GET never deletes data.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id)) return RedirectToAction(nameof(Index));
        try
        {
            using var request = ApiRequest(HttpMethod.Delete, PathFor(id));
            using var response = await _clients.CreateClient("CropCareApi").SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized) return await LoginAgain();
            if (response.IsSuccessStatusCode)
            {
                TempData["Success"] = "Zone deleted.";
                return RedirectToAction(nameof(Index));
            }
            // API returns HTTP 409 if sensors are still registered to the zone.
            TempData["Error"] = await ReadApiError(response, "Unable to delete zone.", ct);
        }
        catch (UnauthorizedAccessException) { return await LoginAgain(); }
        catch (HttpRequestException) { TempData["Error"] = "Cannot reach CropCare API."; }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        { TempData["Error"] = "API request timed out."; }
        return RedirectToAction(nameof(Details), new { id });
    }
}