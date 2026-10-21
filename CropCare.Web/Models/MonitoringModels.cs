using System;
using System.Collections.Generic;
using System.Linq;

namespace CropCare.Web.Models;

// Reuse the DTOs created in Batch 4: SensorRecord, SoilReading,
// SensorMonitoring, SensorHistoryResponse, ZoneRecord and SensorDates.
public sealed class MonitoredSensorItem
{
    public SensorRecord Sensor { get; set; } = new();
    public string ZoneName { get; set; } = "";
    public SensorMonitoring? Monitoring { get; set; }
    public string? Error { get; set; }

    public SoilReading? Reading => Monitoring?.Reading;

    public string ReadingStatus => Monitoring?.Status switch
    {
        "recent" => "Recent reading",
        "stale" => "Historical / stale",
        "awaiting_first_reading" => "No readings yet",
        "unknown_time" => "Unknown timestamp",
        _ => "Unavailable"
    };
}

public sealed class MonitoringOverviewViewModel
{
    public List<ZoneRecord> Zones { get; set; } = new();
    public List<MonitoredSensorItem> Sensors { get; set; } = new();
    public string? ZoneId { get; set; }
    public string? Error { get; set; }
    public int TotalSensorCount { get; set; }
    public bool IsLimited { get; set; }

    public int WithReadings => Sensors.Count(s => s.Reading != null);
    public int RecentReadings => Sensors.Count(s => s.Monitoring?.Status == "recent");
    public int StaleReadings => Sensors.Count(s => s.Monitoring?.Status == "stale");
}

public sealed class MonitoringHistoryViewModel
{
    public SensorRecord Sensor { get; set; } = new();
    public SensorHistoryResponse History { get; set; } = new();
    public string Metric { get; set; } = "nitrogen";
    public string? Cursor { get; set; }
    public string? Error { get; set; }
}

public static class MonitoringMetrics
{
    // Units are from the source data model. EC has no guaranteed unit
    // in the current API, so show "as stored" rather than assume µS/cm.
    public static IReadOnlyDictionary<string, (string Label, string Unit)> All { get; } =
        new Dictionary<string, (string Label, string Unit)>
        {
            ["nitrogen"] = ("Nitrogen", "mg/kg"),
            ["phosphorus"] = ("Phosphorus", "mg/kg"),
            ["potassium"] = ("Potassium", "mg/kg"),
            ["ph"] = ("Soil pH", "pH"),
            ["ec"] = ("Electrical conductivity", "as stored"),
            ["moisture"] = ("Moisture", "%"),
            ["temperature"] = ("Soil temperature", "°C")
        };

    public static string Normalize(string? key) =>
        key != null && All.ContainsKey(key.ToLowerInvariant())
            ? key.ToLowerInvariant() : "nitrogen";

    public static double? Value(SoilReading reading, string key) => key switch
    {
        "nitrogen" => reading.Nitrogen,
        "phosphorus" => reading.Phosphorus,
        "potassium" => reading.Potassium,
        "ph" => reading.Ph,
        "ec" => reading.Ec,
        "moisture" => reading.Moisture,
        "temperature" => reading.Temperature,
        _ => null
    };
}