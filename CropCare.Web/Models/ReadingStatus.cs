namespace CropCare.Web.Models;
public static class ReadingStatus
{
    public static string Label(string key, double? value)
    {
        if (!value.HasValue || !double.IsFinite(value.Value)) return "NO DATA";
        var (min, max, low, high) = key.ToLowerInvariant() switch {
            "n" or "nitrogen" => (0d,1999d,20d,1999d),
            "p" or "phosphorus" => (0d,1999d,10d,1999d),
            "k" or "potassium" => (0d,1999d,100d,1999d),
            "ph" => (3d,9d,6d,7.2d), "moisture" => (0d,100d,40d,65d),
            "temperature" => (-40d,80d,18d,35d), "ec" => (0d,10000d,0d,1000d),
            _ => (double.NegativeInfinity,double.PositiveInfinity,double.NegativeInfinity,double.PositiveInfinity)
        };
        return value < min || value > max ? "CHECK SENSOR" : value < low ? "LOW" : value > high ? "HIGH" : "GOOD";
    }
}
