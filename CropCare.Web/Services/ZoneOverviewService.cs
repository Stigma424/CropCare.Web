using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CropCare.Web.Models;

namespace CropCare.Web.Services;

// The API remains the only source of data for this MVC service.
// Sensor statistics use ONE latest stored reading per sensor (not all history rows).
public sealed class ZoneOverviewService
{
    private readonly IHttpClientFactory _httpClients;
    private const int MaxSensorRequests = 80;

    public ZoneOverviewService(IHttpClientFactory httpClients) => _httpClients = httpClients;

    private async Task<T> ReadAsync<T>(string url, string token, CancellationToken ct)
    {
        var client = _httpClients.CreateClient("CropCareApi");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new UnauthorizedAccessException("Firebase session expired.");
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"CropCare API returned HTTP {(int)response.StatusCode} for {url}.");
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct)
               ?? throw new HttpRequestException("CropCare API returned an empty response.");
    }

    public Task<List<ZoneRecord>> GetZonesAsync(string token, CancellationToken ct) =>
        ReadAsync<List<ZoneRecord>>("api/zones", token, ct);

    public Task<ZoneRecord> GetZoneAsync(string zoneId, string token, CancellationToken ct) =>
        ReadAsync<ZoneRecord>("api/zones/" + Uri.EscapeDataString(zoneId), token, ct);

    public async Task<List<ZoneOverview>> GetDashboardZonesAsync(string token, CancellationToken ct)
    {
        var zones = await GetZonesAsync(token, ct);
        var sensors = await ReadAsync<List<SensorRecord>>("api/sensors", token, ct);
        var items = zones.OrderBy(z => z.ZoneName, StringComparer.OrdinalIgnoreCase)
            .Select(z => new ZoneOverview { Zone = z }).ToList();
        foreach (var item in items)
            item.Sensors = sensors.Where(s => s.ZoneId == item.Zone.ZoneId)
                .Select(s => new ZoneSensorSnapshot { Sensor = s }).ToList();
        await PopulateAsync(items, token, ct);
        return items;
    }

    public async Task<ZoneOverview> GetZoneOverviewAsync(ZoneRecord zone, string token, CancellationToken ct)
    {
        var zoneId = Uri.EscapeDataString(zone.ZoneId);
        var sensors = await ReadAsync<List<SensorRecord>>("api/sensors?zoneId=" + zoneId, token, ct);
        var item = new ZoneOverview
        {
            Zone = zone,
            Sensors = sensors.Where(s => s.ZoneId == zone.ZoneId)
                .Select(s => new ZoneSensorSnapshot { Sensor = s }).ToList()
        };
        await PopulateAsync(new List<ZoneOverview> { item }, token, ct);
        return item;
    }

    private async Task PopulateAsync(List<ZoneOverview> zones, string token, CancellationToken ct)
    {
        var snapshots = zones.SelectMany(z => z.Sensors).ToList();
        if (snapshots.Count > MaxSensorRequests)
        {
            foreach (var zone in zones)
            {
                zone.Error = "Too many sensors for this web summary; use the sensor pages instead.";
                zone.Incomplete = true;
            }
            return;
        }

        // Bound API request concurrency; never access HttpContext inside these tasks.
        using var gate = new SemaphoreSlim(5);
        var tasks = snapshots.Select(async snapshot =>
        {
            await gate.WaitAsync(ct);
            try
            {
                snapshot.Monitoring = await ReadAsync<SensorMonitoring>(
                    "api/sensors/" + Uri.EscapeDataString(snapshot.Sensor.SensorId) + "/monitoring", token, ct);
            }
            catch (HttpRequestException)
            {
                snapshot.Error = "Monitoring currently unavailable";
            }
            catch (System.Text.Json.JsonException)
            {
                snapshot.Error = "Unrecognized monitoring response";
            }
            finally { gate.Release(); }
        });
        await Task.WhenAll(tasks);
        foreach (var item in zones)
        {
            item.Incomplete = item.Sensors.Any(s => s.Error != null);
            var readings = item.Sensors.Select(s => s.Latest).Where(r => r != null)
                .Select(r => r!).ToList();
            static double? Mean(List<SoilReading> r, Func<SoilReading, double?> read)
            {
                var values = r.Select(read).Where(v => v.HasValue && double.IsFinite(v.Value))
                    .Select(v => v!.Value).ToList();
                return values.Count == 0 ? null : values.Average();
            }
            item.Averages = new ZoneAverageReading
            {
                RegisteredSensors = item.Sensors.Count,
                ContributingSensors = readings.Count,
                LatestReadingAtMs = readings.Count == 0 ? 0 : readings.Max(r => r.RecordedAtMs),
                Nitrogen = Mean(readings, r => r.Nitrogen),
                Phosphorus = Mean(readings, r => r.Phosphorus),
                Potassium = Mean(readings, r => r.Potassium),
                Moisture = Mean(readings, r => r.Moisture),
                Ph = Mean(readings, r => r.Ph),
                Temperature = Mean(readings, r => r.Temperature),
                Ec = Mean(readings, r => r.Ec)
            };
        }
    }
}