using System.ComponentModel.DataAnnotations;

namespace CropCare.Web.Models;

// These names match GET /api/zones and GET /api/zones/{id}.
public sealed class ZoneRecord
{
    public string ZoneId { get; set; } = "";
    public string ZoneName { get; set; } = "";
    public double ZoneAreaSqm { get; set; }
    public long DateOfPlanting { get; set; }
    public bool IsHarvested { get; set; }

    public DateTime? PlantingDate => ZoneDates.FromUnixMilliseconds(DateOfPlanting);

    public string PlantingDateText => PlantingDate?.ToString("MMM d, yyyy") ?? "Not set";

    public string CropAgeText
    {
        get
        {
            if (IsHarvested) return "Harvested";
            var date = PlantingDate;
            if (date is null) return "Planting date unavailable";
            var days = (ZoneDates.Today - date.Value.Date).Days;
            if (days < 0) return "Planting scheduled";
            return days == 1 ? "1 day after planting" : $"{days} days after planting";
        }
    }
}

public sealed class ZoneFormViewModel
{
    public string? ZoneId { get; set; }

    [Required(ErrorMessage = "Enter a zone name.")]
    [StringLength(80, MinimumLength = 1,
        ErrorMessage = "Zone name must be 1 to 80 characters.")]
    [Display(Name = "Zone name")]
    public string ZoneName { get; set; } = "";

    [Range(0.000001, 100000000, ErrorMessage = "Enter a valid area greater than zero.")]
    [Display(Name = "Area (square meters)")]
    public double ZoneAreaSqm { get; set; }

    [Required(ErrorMessage = "Select a planting date.")]
    [DataType(DataType.Date)]
    [Display(Name = "Planting date")]
    public DateTime? PlantingDate { get; set; }
}

// Android writes local midnight as Unix milliseconds. For the Cebu pilot,
// convert the date using the Philippine UTC+08:00 offset in both directions.
public static class ZoneDates
{
    private static readonly TimeSpan PhilippineOffset = TimeSpan.FromHours(8);

    public static DateTime Today => DateTimeOffset.UtcNow
        .ToOffset(PhilippineOffset).Date;

    public static DateTime? FromUnixMilliseconds(long milliseconds)
    {
        if (milliseconds <= 0) return null;
        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
                .ToOffset(PhilippineOffset).Date;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    public static long ToUnixMilliseconds(DateTime date)
    {
        // Explicit offset avoids different dates when the MVC server runs in UTC.
        var philippineMidnight = new DateTimeOffset(
            date.Year, date.Month, date.Day, 0, 0, 0, PhilippineOffset);
        return philippineMidnight.ToUnixTimeMilliseconds();
    }
}