using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CropCare.Web.Models;
using CropCare.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CropCare.Web.Controllers;

[Authorize]
public sealed class MonitoringController : Controller
{
    private readonly IHttpClientFactory _clients;
    private readonly FirebaseTokenStore _tokens;
    private readonly ILogger<MonitoringController> _logger;

    public MonitoringController(
        IHttpClientFactory clients,
        FirebaseTokenStore tokens,
        ILogger<MonitoringController> logger)
    {
        _clients = clients;
        _tokens = tokens;
        _logger = logger;
    }

    private string RequireToken()
    {
        string? sessionId = User.FindFirst("CropCareSession")?.Value;
        return _tokens.GetIdToken(sessionId) ??
            throw new UnauthorizedAccessException("Firebase session expired.");
    }

    private HttpRequestMessage GetRequest(string path, string? token = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", token ?? RequireToken());
        return request;
    }

    private async Task<IActionResult> LoginAgain()
    {
        string? sessionId = User.FindFirst("CropCareSession")?.Value;
        _tokens.RemoveSession(sessionId);
        await HttpContext.SignOutAsync(
            CookieAuthenticationDefaults.AuthenticationScheme);
        TempData["Error"] = "Your session expired. Please log in again.";
        return RedirectToAction("Login", "Account");
    }

    private static bool ValidId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= 256 &&
        id != "." && id != ".." && !id.Contains('/');

    private static string SensorPath(string id) =>
        "api/sensors/" + Uri.EscapeDataString(id);

    private static Task<string> ReadApiError(HttpResponseMessage response, string fallback, CancellationToken ct) => ApiResponseReader.ErrorAsync(response, fallback, ct);

    // Batch 5: dashboard of saved readings from existing sensors.
    // Does not ingest, simulate, or write measurements.
    [HttpGet]
    public async Task<IActionResult> Index(string? zoneId, CancellationToken ct)
    {
        var model = new MonitoringOverviewViewModel { ZoneId = zoneId };
        try
        {
            var api = _clients.CreateClient("CropCareApi");

            using (var zonesRequest = GetRequest("api/zones"))
            using (var zonesResponse = await api.SendAsync(zonesRequest, ct))
            {
                if (zonesResponse.StatusCode == HttpStatusCode.Unauthorized)
                    return await LoginAgain();
                if (zonesResponse.IsSuccessStatusCode)
                    model.Zones = await zonesResponse.Content
                        .ReadFromJsonAsync<List<ZoneRecord>>(cancellationToken: ct)
                        ?? new();
                else
                    model.Error = "Could not load farm-zone labels.";
            }

            List<SensorRecord> registrations;
            using (var sensorsRequest = GetRequest("api/sensors"))
            using (var sensorsResponse = await api.SendAsync(sensorsRequest, ct))
            {
                if (sensorsResponse.StatusCode == HttpStatusCode.Unauthorized)
                    return await LoginAgain();
                if (!sensorsResponse.IsSuccessStatusCode)
                {
                    model.Error = await ReadApiError(sensorsResponse,
                        "Cannot load registered sensors.", ct);
                    return View(model);
                }
                registrations = await sensorsResponse.Content
                    .ReadFromJsonAsync<List<SensorRecord>>(cancellationToken: ct)
                    ?? new();
            }

            if (!string.IsNullOrWhiteSpace(zoneId))
                registrations = registrations
                    .Where(s => s.ZoneId == zoneId).ToList();

            model.TotalSensorCount = registrations.Count;
            registrations = registrations
                .OrderBy(s => s.SensorName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Prevent an unbounded number of API/Firestore calls in one page load.
            // Remaining sensors remain available under My Sensors.
            const int maxOverviewSensors = 30;
            model.IsLimited = registrations.Count > maxOverviewSensors;
            registrations = registrations.Take(maxOverviewSensors).ToList();

            // Capture the Firebase token before running concurrent tasks.
            // Do not read HttpContext concurrently from those tasks.
            var tokenForMonitoring = RequireToken();
            using var limiter = new SemaphoreSlim(5);
            var tasks = registrations.Select(async sensor =>
            {
                var row = new MonitoredSensorItem
                {
                    Sensor = sensor,
                    ZoneName = model.Zones.FirstOrDefault(z => z.ZoneId == sensor.ZoneId)
                        ?.ZoneName ?? sensor.ZoneId
                };
                await limiter.WaitAsync(ct);
                try
                {
                    using var request = GetRequest(SensorPath(sensor.SensorId) + "/monitoring", tokenForMonitoring);
                    using var response = await api.SendAsync(request, ct);
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                        throw new UnauthorizedAccessException();
                    if (response.IsSuccessStatusCode)
                        row.Monitoring = await response.Content
                            .ReadFromJsonAsync<SensorMonitoring>(cancellationToken: ct);
                    else
                        row.Error = await ReadApiError(response,
                            "Monitoring information unavailable.", ct);
                }
                catch (HttpRequestException)
                {
                    row.Error = "API connection error.";
                }
                catch (TaskCanceledException) when (!ct.IsCancellationRequested)
                {
                    row.Error = "Monitoring request timed out.";
                }
                finally
                {
                    limiter.Release();
                }
                return row;
            });

            model.Sensors = (await Task.WhenAll(tasks)).ToList();
            return View(model);
        }
        catch (UnauthorizedAccessException) { return await LoginAgain(); }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Monitoring overview API error");
            model.Error = "Could not contact the CropCare API.";
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            model.Error = "CropCare API request timed out.";
        }
        return View(model);
    }

    // Batch 6: latest 50 historical readings per page (cursor from API).
    // Chart/date filtering operate on the currently loaded page only.
    [HttpGet]
    public IActionResult History(string id, string? metric, string? cursor, CancellationToken ct)
    {
        return RedirectToAction("History", "Sensors", new { id, metric = MonitoringMetrics.Normalize(metric), cursor });
    }
}
