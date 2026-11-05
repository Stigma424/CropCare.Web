using System.Collections.Generic;

namespace CropCare.Web.Models;

public class PrototypeNpk
{
    public double? Nitrogen { get; set; }
    public double? Phosphorus { get; set; }
    public double? Potassium { get; set; }
}

public sealed class AdvisoryReading : PrototypeNpk
{
    public double? Ph { get; set; }
    public double? Moisture { get; set; }
    public double? Temperature { get; set; }
    public double? Ec { get; set; }
}

public sealed class ZoneAdvisory
{
    public string ZoneId { get; set; } = "";
    public string ZoneName { get; set; } = "";
    public bool IsHarvested { get; set; }
    public int? DaysAfterPlanting { get; set; }
    public string StageName { get; set; } = "";
    public string Status { get; set; } = "";
    public string Summary { get; set; } = "";
    public string? SensorId { get; set; }
    public string? SensorName { get; set; }
    public long RecordedAtMs { get; set; }
    public string ReadingStatus { get; set; } = "";
    public AdvisoryReading? Reading { get; set; }
    public PrototypeNpk? PrototypeTargets { get; set; }
    public List<string> ActionItems { get; set; } = new();
    public string Disclaimer { get; set; } = "";
}

public sealed class AdvisoriesPageModel
{
    public List<ZoneRecord> Zones { get; set; } = new();
    public string? SelectedZoneId { get; set; }
    public ZoneAdvisory? Advisory { get; set; }
    public string? Error { get; set; }
}

public sealed class CropCareNotification {
    public string ZoneId {get;set;}="";
    public string SensorId {get;set;}="";
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string Type { get; set; } = "general";
    public long TimestampMs { get; set; }
    public bool IsRead { get; set; }
    public string TimeText => SensorDates.Format(TimestampMs);
}

public sealed class NotificationsPageModel
{
    public List<CropCareNotification> Notifications { get; set; } = new();
    public string? Error { get; set; }
}
