using System;
using System.Collections.Generic;
using System.Linq;

namespace CropCare.Web.Models;

// One recent-or-historical reading per registered sensor, matching the Android zone flow.
public sealed class ZoneSensorSnapshot
{
    public SensorRecord Sensor { get; set; } = new();
    public SensorMonitoring? Monitoring { get; set; }
    public string? Error { get; set; }
    public SoilReading? Latest => Monitoring?.Reading;
    public string Freshness => Monitoring?.Status switch
    {
        "recent" => "Recent reading",
        "stale" => "Historical / stale",
        "awaiting_first_reading" => "No readings yet",
        "unknown_time" => "Unknown reading time",
        _ => "Unavailable"
    };
}

public sealed class ZoneAverageReading
{
    public double? Nitrogen { get; set; }
    public double? Phosphorus { get; set; }
    public double? Potassium { get; set; }
    public double? Moisture { get; set; }
    public double? Ph { get; set; }
    public double? Temperature { get; set; }
    public double? Ec { get; set; }
    public long LatestReadingAtMs { get; set; }
    public int ContributingSensors { get; set; }
    public int RegisteredSensors { get; set; }

    public bool HasData => ContributingSensors > 0;
    public string UpdatedText => SensorDates.Format(LatestReadingAtMs);
}

public sealed class ZoneOverview
{
    public ZoneRecord Zone { get; set; } = new();
    public List<ZoneSensorSnapshot> Sensors { get; set; } = new();
    public ZoneAverageReading Averages { get; set; } = new();
    public string? Error { get; set; }
    public bool Incomplete { get; set; }
    // Null means membership status could not be loaded (fail closed).
    public bool? IsSubscribed { get; set; }
    public string SubscriptionState { get; set; } = "unknown";

    // The exact five 20-point rules currently used by the Android prototype.
    // This is NOT a validated soil-health / agronomic score.
    public int? PrototypeSoilHealth
    {
        get
        {
            var a = Averages;
            if (Incomplete || !a.Nitrogen.HasValue || !a.Phosphorus.HasValue ||
                !a.Potassium.HasValue || !a.Moisture.HasValue || !a.Ph.HasValue)
                return null;
            var score = 0;
            if (a.Nitrogen >= 20) score += 20;
            if (a.Phosphorus >= 10) score += 20;
            if (a.Potassium >= 100) score += 20;
            if (a.Moisture >= 40 && a.Moisture <= 80) score += 20;
            if (a.Ph >= 5.5 && a.Ph <= 7.5) score += 20;
            return score;
        }
    }
    public string SoilHealthLabel => PrototypeSoilHealth switch
    {
        int n when n >= 80 => "Optimal (prototype)",
        int n when n >= 40 => "Fair (prototype)",
        int => "Poor (prototype)",
        _ => "Pending evaluation"
    };
    public string RecommendationText => Zone.IsHarvested
        ? "Post-harvest zone. Recommendations will be available after validation of the advice engine."
        : IsSubscribed == true
            ? "Pro membership active. Your customized fertilizer recommendation is being prepared."
            : SubscriptionState == "pending"
                ? "Your Pro request is pending verification. Recommendations will unlock after approval and engine validation."
                : IsSubscribed == false
                    ? "Subscribe to CropCare Pro to unlock future detailed fertilizer recommendations."
                    : "Subscription status unavailable. Fertilizer recommendations are temporarily hidden.";

}

public sealed class CropCareDashboardViewModel
{
    public CropCareProfile? Profile { get; set; }
    public List<ZoneOverview> Zones { get; set; } = new();
    public string? Error { get; set; }
}