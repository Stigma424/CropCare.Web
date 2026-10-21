using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace CropCare.Web.Models;

public sealed class SensorRecord
{
    public string SensorId { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string SensorName { get; set; } = "";
    public string ZoneId { get; set; } = "";
    public string UserId { get; set; } = "";
    public bool IsOnline { get; set; }
    public long LastScanTimestamp { get; set; }
    public string PairingStatus { get; set; } = "";
}

public sealed class SoilReading
{
    public string ReadingId { get; set; } = "";
    public long RecordedAtMs { get; set; }
    public double? Nitrogen { get; set; }
    public double? Phosphorus { get; set; }
    public double? Potassium { get; set; }
    public double? Moisture { get; set; }
    public double? Ph { get; set; }
    public double? Temperature { get; set; }
    public double? Ec { get; set; }
    public string RecordedText => SensorDates.Format(RecordedAtMs);
}

public static class SensorDates
{
    public static string Format(long unixMs)
    {
        if (unixMs <= 0) return "Not recorded";
        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(unixMs)
                .ToOffset(TimeSpan.FromHours(8))
                .ToString("MMM d, yyyy h:mm tt") + " (PH)";
        }
        catch (ArgumentOutOfRangeException) { return "Unknown time"; }
    }
}

public sealed class SensorMonitoring
{
    public string SensorId { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string SensorName { get; set; } = "";
    public string PairingStatus { get; set; } = "";
    public string Status { get; set; } = "";
    public long LastReadingAtMs { get; set; }
    public SoilReading? Reading { get; set; }
}

public sealed class SensorHistoryResponse
{
    public string SensorId { get; set; } = "";
    public string SensorName { get; set; } = "";
    public List<SoilReading> Readings { get; set; } = new();
    public string? NextCursor { get; set; }
}

public sealed class SensorIndexViewModel
{
    public List<SensorRecord> Sensors { get; set; } = new();
    public List<ZoneRecord> Zones { get; set; } = new();
    public string? ZoneId { get; set; }
    public string? Error { get; set; }
    public string ZoneLabel(string zoneId) =>
        Zones.FirstOrDefault(z => z.ZoneId == zoneId)?.ZoneName ?? "Unknown zone";
}

public sealed class SensorDetailsViewModel
{
    public SensorRecord Sensor { get; set; } = new();
    public SensorMonitoring? Monitoring { get; set; }
    public string? MonitoringError { get; set; }
}

public sealed class SensorHistoryViewModel
{
    public SensorHistoryResponse History { get; set; } = new();
    public string SensorId { get; set; } = "";
    public string? Error { get; set; }
}

public sealed class PairSensorViewModel
{
    [Required(ErrorMessage = "Please scan or upload a CropCare device QR code.")]
    public string QrPayload { get; set; } = "";

    [Required(ErrorMessage = "Enter a sensor name.")]
    [StringLength(80, MinimumLength = 1)]
    [Display(Name = "Sensor name")]
    public string SensorName { get; set; } = "";

    [Required(ErrorMessage = "Select a farm zone.")]
    [Display(Name = "Farm zone")]
    public string ZoneId { get; set; } = "";

    public List<ZoneRecord> Zones { get; set; } = new();
}

public sealed class RenameSensorViewModel
{
    public string SensorId { get; set; } = "";

    [Required]
    [StringLength(80, MinimumLength = 1)]
    [Display(Name = "Sensor name")]
    public string SensorName { get; set; } = "";
}

// QR syntax only: trust the API's device_registry + claim hash for authorization.
public static class CropCareQr
{
    private static readonly Regex DevicePattern = new(
        "^[A-Za-z0-9_-]{6,128}$", RegexOptions.CultureInvariant);
    private static readonly Regex ClaimPattern = new(
        "^[A-Fa-f0-9]{32}$", RegexOptions.CultureInvariant);

    public static bool TryParse(string? raw, out string deviceId, out string claimCode)
    {
        deviceId = "";
        claimCode = "";
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 200) return false;
        var parts = raw.Trim().Split('|');
        if (parts.Length != 3 || parts[0] != "CROPCARE1") return false;
        if (!DevicePattern.IsMatch(parts[1]) || !ClaimPattern.IsMatch(parts[2])) return false;
        deviceId = parts[1];
        claimCode = parts[2].ToUpperInvariant();
        return true;
    }
}