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
public sealed class SensorsController : Controller
{
    private readonly IHttpClientFactory _clients;
    private readonly FirebaseTokenStore _tokens;
    private readonly ZoneOverviewService _overview;

    public SensorsController(IHttpClientFactory clients, FirebaseTokenStore tokens, ZoneOverviewService overview)
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
            throw new UnauthorizedAccessException("Your login session has expired.");

        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
        if (body != null) request.Content = JsonContent.Create(body);
        return request;
    }

    private static bool ValidId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= 256 &&
        id != "." && id != ".." && !id.Contains('/');

    private static string SensorPath(string id) =>
        "api/sensors/" + Uri.EscapeDataString(id);

    private async Task<IActionResult> LoginAgain()
    {
        var sessionId = User.FindFirst("CropCareSession")?.Value;
        _tokens.RemoveSession(sessionId);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        TempData["Error"] = "Your session expired. Please log in again.";
        return RedirectToAction("Login", "Account");
    }

    private async Task<List<ZoneRecord>> LoadZones(CancellationToken ct)
    {
        using var request = ApiRequest(HttpMethod.Get, "api/zones");
        using var response = await _clients.CreateClient("CropCareApi").SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new UnauthorizedAccessException("Your login session expired.");
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("Could not load your farm zones.");
        return await response.Content.ReadFromJsonAsync<List<ZoneRecord>>(
            cancellationToken: ct) ?? new();
    }

    private static async Task<string> ApiError(HttpResponseMessage response,
        string fallback, CancellationToken ct)
    {
        try
        {
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            if (json.ValueKind == JsonValueKind.Object &&
                json.TryGetProperty("error", out var error) &&
                error.ValueKind == JsonValueKind.String)
                return error.GetString() ?? fallback;
        }
        catch (JsonException) { }
        catch (NotSupportedException) { }
        return fallback;
    }

    private IActionResult Unavailable()
    {
        TempData["Error"] = "The CropCare API is unavailable. Please try again.";
        return RedirectToAction(nameof(Index));
    }

    // Zone-scoped sensor management, matching the Android flow.
    [HttpGet]
    public async Task<IActionResult> Index(string? zoneId, CancellationToken ct)
    {
        if (!ValidId(zoneId)) return RedirectToAction("Index", "Zones");
        try
        {
            using var zoneRequest = ApiRequest(HttpMethod.Get,
                "api/zones/" + Uri.EscapeDataString(zoneId!));
            using var zoneResponse = await _clients.CreateClient("CropCareApi").SendAsync(zoneRequest, ct);
            if (zoneResponse.StatusCode == HttpStatusCode.Unauthorized) return await LoginAgain();
            if (zoneResponse.StatusCode == HttpStatusCode.NotFound) return NotFound();
            if (!zoneResponse.IsSuccessStatusCode) return Unavailable();
            var zone = await zoneResponse.Content.ReadFromJsonAsync<ZoneRecord>(cancellationToken: ct);
            if (zone == null) return NotFound();
            var token = _tokens.GetIdToken(User.FindFirst("CropCareSession")?.Value)!;
            var vm = await _overview.GetZoneOverviewAsync(zone, token, ct);
            return View(vm);
        }
        catch (UnauthorizedAccessException) { return await LoginAgain(); }
        catch (HttpRequestException) { return Unavailable(); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return Unavailable(); }
    }

    // Removes registrations one by one through the EXISTING API.
    // Never bulk-delete with a GET or without explicit confirmation.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAll(string zoneId, string confirmation, CancellationToken ct)
    {
        if (!ValidId(zoneId)) return BadRequest("Invalid zone ID.");
        if (confirmation != "DELETE")
        {
            TempData["Error"] = "Type DELETE to confirm bulk removal.";
            return RedirectToAction(nameof(Index), new { zoneId });
        }
        try
        {
            var client = _clients.CreateClient("CropCareApi");
            using var zoneRequest = ApiRequest(HttpMethod.Get,
                "api/zones/" + Uri.EscapeDataString(zoneId));
            using var zoneResponse = await client.SendAsync(zoneRequest, ct);
            if (zoneResponse.StatusCode == HttpStatusCode.Unauthorized) return await LoginAgain();
            if (!zoneResponse.IsSuccessStatusCode) return NotFound();

            using var listRequest = ApiRequest(HttpMethod.Get,
                "api/sensors?zoneId=" + Uri.EscapeDataString(zoneId));
            using var listResponse = await client.SendAsync(listRequest, ct);
            if (listResponse.StatusCode == HttpStatusCode.Unauthorized) return await LoginAgain();
            if (!listResponse.IsSuccessStatusCode) return Unavailable();
            var sensors = await listResponse.Content.ReadFromJsonAsync<List<SensorRecord>>(cancellationToken: ct)
                          ?? new List<SensorRecord>();
            sensors = sensors.Where(s => s.ZoneId == zoneId).ToList();
            if (sensors.Count > 50)
            {
                TempData["Error"] = "More than 50 devices are registered. Remove sensors individually.";
                return RedirectToAction(nameof(Index), new { zoneId });
            }
            var removed = 0;
            foreach (var sensor in sensors)
            {
                using var deleteRequest = ApiRequest(HttpMethod.Delete, SensorPath(sensor.SensorId));
                using var response = await client.SendAsync(deleteRequest, ct);
                if (response.StatusCode == HttpStatusCode.Unauthorized) return await LoginAgain();
                if (!response.IsSuccessStatusCode) break;
                removed++;
            }
            if (removed == sensors.Count)
                TempData["Success"] = $"Removed {removed} sensor registration(s). The existing QR claims remain consumed.";
            else
                TempData["Error"] = $"Only {removed} of {sensors.Count} sensors were removed. Please review the remainder.";
            return RedirectToAction(nameof(Index), new { zoneId });
        }
        catch (UnauthorizedAccessException) { return await LoginAgain(); }
        catch (HttpRequestException) { return Unavailable(); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return Unavailable(); }
    }

    // GET /Sensors/Pair?zoneId=...
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Pair(string? zoneId, CancellationToken ct)
    {
        try
        {
            var vm = new PairSensorViewModel { ZoneId = zoneId ?? "" };
            vm.Zones = await LoadZones(ct);
            return View(vm);
        }
        catch (UnauthorizedAccessException) { return await LoginAgain(); }
        catch (HttpRequestException) { return Unavailable(); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return Unavailable(); }
    }

    // POST /Sensors/Pair
    [HttpPost]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Pair(PairSensorViewModel model, CancellationToken ct)
    {
        model.SensorName = model.SensorName?.Trim() ?? "";
        model.ZoneId = model.ZoneId?.Trim() ?? "";
        if (!CropCareQr.TryParse(model.QrPayload, out var deviceId, out var claimCode))
            ModelState.AddModelError(nameof(model.QrPayload),
                "This is not a valid CropCare device QR code.");

        try
        {
            model.Zones = await LoadZones(ct);
            if (!model.Zones.Any(z => z.ZoneId == model.ZoneId))
                ModelState.AddModelError(nameof(model.ZoneId), "Choose one of your own farm zones.");
            if (!ModelState.IsValid) return View(model);

            // Never send a raw QR image to the API; only the parsed credentials.
            using var request = ApiRequest(HttpMethod.Post, "api/sensors/pair", new
            {
                deviceId,
                claimCode,
                sensorName = model.SensorName,
                zoneId = model.ZoneId
            });
            using var response = await _clients.CreateClient("CropCareApi").SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized) return await LoginAgain();
            if (!response.IsSuccessStatusCode)
            {
                ModelState.AddModelError("", await ApiError(response,
                    "Pairing was rejected by the CropCare API.", ct));
                return View(model);
            }
            var created = await response.Content.ReadFromJsonAsync<SensorRecord>(
                cancellationToken: ct);
            TempData["Success"] = "QR pairing completed. Waiting for verified device readings.";
            return created != null && ValidId(created.SensorId)
                ? RedirectToAction(nameof(Details), new { id = created.SensorId })
                : RedirectToAction(nameof(Index));
        }
        catch (UnauthorizedAccessException) { return await LoginAgain(); }
        catch (HttpRequestException)
        { ModelState.AddModelError("", "Cannot reach CropCare API."); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        { ModelState.AddModelError("", "CropCare API request timed out."); }
        return View(model);
    }

    // GET /Sensors/Details/{id}
    [HttpGet]
    public async Task<IActionResult> Details(string id, CancellationToken ct)
    {
        if (!ValidId(id)) return BadRequest("Invalid sensor ID.");
        try
        {
            using var sensorRequest = ApiRequest(HttpMethod.Get, SensorPath(id));
            using var sensorResponse = await _clients.CreateClient("CropCareApi")
                .SendAsync(sensorRequest, ct);
            if (sensorResponse.StatusCode == HttpStatusCode.Unauthorized) return await LoginAgain();
            if (sensorResponse.StatusCode == HttpStatusCode.NotFound)
            {
                TempData["Error"] = "Sensor not found or not owned by this account.";
                return RedirectToAction(nameof(Index));
            }
            if (!sensorResponse.IsSuccessStatusCode)
            {
                TempData["Error"] = await ApiError(sensorResponse, "Cannot load sensor.", ct);
                return RedirectToAction(nameof(Index));
            }

            var sensor = await sensorResponse.Content.ReadFromJsonAsync<SensorRecord>(
                cancellationToken: ct);
            if (sensor == null) return RedirectToAction(nameof(Index));
            var vm = new SensorDetailsViewModel { Sensor = sensor };

            using var monitorRequest = ApiRequest(HttpMethod.Get,
                SensorPath(id) + "/monitoring");
            using var monitorResponse = await _clients.CreateClient("CropCareApi")
                .SendAsync(monitorRequest, ct);
            if (monitorResponse.StatusCode == HttpStatusCode.Unauthorized) return await LoginAgain();
            if (monitorResponse.IsSuccessStatusCode)
                vm.Monitoring = await monitorResponse.Content
                    .ReadFromJsonAsync<SensorMonitoring>(cancellationToken: ct);
            else
                vm.MonitoringError = await ApiError(monitorResponse,
                    "Latest reading is currently unavailable.", ct);
            return View(vm);
        }
        catch (UnauthorizedAccessException) { return await LoginAgain(); }
        catch (HttpRequestException) { return Unavailable(); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return Unavailable(); }
    }

    // GET /Sensors/History/{id}?cursor=...
    [HttpGet]
    public async Task<IActionResult> History(string id, string? cursor, CancellationToken ct)
    {
        if (!ValidId(id) || (!string.IsNullOrWhiteSpace(cursor) && !ValidId(cursor)))
            return BadRequest("Invalid sensor ID or cursor.");
        var vm = new SensorHistoryViewModel { SensorId = id };
        try
        {
            string path = SensorPath(id) + "/readings?limit=20";
            if (!string.IsNullOrWhiteSpace(cursor))
                path += "&cursor=" + Uri.EscapeDataString(cursor);
            using var request = ApiRequest(HttpMethod.Get, path);
            using var response = await _clients.CreateClient("CropCareApi").SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized) return await LoginAgain();
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                TempData["Error"] = "Sensor not found or not owned by this account.";
                return RedirectToAction(nameof(Index));
            }
            if (!response.IsSuccessStatusCode)
                vm.Error = await ApiError(response, "Could not load sensor history.", ct);
            else
                vm.History = await response.Content
                    .ReadFromJsonAsync<SensorHistoryResponse>(cancellationToken: ct) ?? new();
            return View(vm);
        }
        catch (UnauthorizedAccessException) { return await LoginAgain(); }
        catch (HttpRequestException) { vm.Error = "Cannot connect to CropCare API."; }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        { vm.Error = "CropCare API request timed out."; }
        return View(vm);
    }

    // GET /Sensors/Rename/{id}
    [HttpGet]
    public async Task<IActionResult> Rename(string id, CancellationToken ct)
    {
        if (!ValidId(id)) return BadRequest("Invalid sensor ID.");
        try
        {
            using var request = ApiRequest(HttpMethod.Get, SensorPath(id));
            using var response = await _clients.CreateClient("CropCareApi").SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized) return await LoginAgain();
            if (!response.IsSuccessStatusCode)
            {
                TempData["Error"] = "Cannot locate this sensor.";
                return RedirectToAction(nameof(Index));
            }
            var sensor = await response.Content.ReadFromJsonAsync<SensorRecord>(cancellationToken: ct);
            return sensor == null ? RedirectToAction(nameof(Index)) :
                View(new RenameSensorViewModel { SensorId = sensor.SensorId, SensorName = sensor.SensorName });
        }
        catch (UnauthorizedAccessException) { return await LoginAgain(); }
        catch (HttpRequestException) { return Unavailable(); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return Unavailable(); }
    }

    // POST /Sensors/Rename/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rename(string id, RenameSensorViewModel model, CancellationToken ct)
    {
        if (!ValidId(id)) return BadRequest("Invalid sensor ID.");
        model.SensorId = id;
        model.SensorName = model.SensorName?.Trim() ?? "";
        if (!ModelState.IsValid) return View(model);
        try
        {
            using var request = ApiRequest(HttpMethod.Patch, SensorPath(id),
                new { sensorName = model.SensorName });
            using var response = await _clients.CreateClient("CropCareApi").SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized) return await LoginAgain();
            if (!response.IsSuccessStatusCode)
            {
                ModelState.AddModelError("", await ApiError(response, "Cannot rename sensor.", ct));
                return View(model);
            }
            TempData["Success"] = "Sensor name updated.";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (UnauthorizedAccessException) { return await LoginAgain(); }
        catch (HttpRequestException) { ModelState.AddModelError("", "Cannot reach CropCare API."); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        { ModelState.AddModelError("", "API request timed out."); }
        return View(model);
    }

    // POST /Sensors/Delete/{id} — existing API preserves claim consumption.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        if (!ValidId(id)) return BadRequest("Invalid sensor ID.");
        try
        {
            var client = _clients.CreateClient("CropCareApi");
            using var getRequest = ApiRequest(HttpMethod.Get, SensorPath(id));
            using var getResponse = await client.SendAsync(getRequest, ct);
            if (getResponse.StatusCode == HttpStatusCode.Unauthorized) return await LoginAgain();
            if (!getResponse.IsSuccessStatusCode) return NotFound();
            var sensor = await getResponse.Content.ReadFromJsonAsync<SensorRecord>(cancellationToken: ct);
            if (sensor == null) return NotFound();
            using var deleteRequest = ApiRequest(HttpMethod.Delete, SensorPath(id));
            using var response = await client.SendAsync(deleteRequest, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized) return await LoginAgain();
            if (!response.IsSuccessStatusCode)
            {
                TempData["Error"] = await ApiError(response, "Cannot remove sensor.", ct);
                return RedirectToAction(nameof(Details), new { id });
            }
            TempData["Success"] = "Sensor registration removed. QR re-pairing remains on hold.";
            return RedirectToAction(nameof(Index), new { zoneId = sensor.ZoneId });
        }
        catch (UnauthorizedAccessException) { return await LoginAgain(); }
        catch (HttpRequestException) { return Unavailable(); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return Unavailable(); }
    }
}