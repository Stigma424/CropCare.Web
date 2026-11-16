namespace CropCare.Web.Models;

public sealed class RecommendationSummary(ZoneAssessmentDto assessment)
{
    private static readonly HashSet<string> BlockingCodes = new(StringComparer.Ordinal)
    {
        "CHECK_SENSOR", "CHECK_REGISTRATION", "AWAIT_READINGS", "LOW_MOISTURE", "HIGH_MOISTURE",
        "LOW_PH", "HIGH_PH", "EC_REVIEW", "TEMPERATURE_REVIEW", "PLANTING_DATE_REQUIRED", "POST_HARVEST"
    };
    public bool HasPlan => assessment.DisplayPlan.Entries.Count > 0;
    public bool CanReviewApplication => HasPlan &&
        assessment.Recommendations.Any(a => a.Code == "NUTRIENT_REVIEW") &&
        !assessment.Recommendations.Any(a => BlockingCodes.Contains(a.Code));
    public string ApplicationNote => !HasPlan ? assessment.PrimaryAction?.WhatToDo ?? "Complete the zone setup to create a crop plan."
        : assessment.Recommendations.Any(a => BlockingCodes.Contains(a.Code))
            ? "Before applying: " + string.Join(" ", assessment.Recommendations.Where(a => BlockingCodes.Contains(a.Code)).Select(a => a.WhatToDo))
            : CanReviewApplication ? "Nutrient readings are low. Use this crop schedule after confirming the crop stage and earlier applications."
            : "Keep this crop schedule for the appropriate stage. Current readings do not establish a need for an extra fertilizer dose.";
    public string What => HasPlan
        ? string.Join("; ", assessment.DisplayPlan.Entries.Select(e => $"{e.Product} ({e.FertilizerGrade})"))
        : "A fertilizer schedule is not available for this crop cycle. " + assessment.PrimaryAction?.WhatToDo;
    public string How => HasPlan ? "Use 14-14-14 as the basal application and urea as the side-dress at the listed stage. Follow each product label and check root-zone moisture."
        : "Complete the priority action, then refresh this assessment.";
    public string When => HasPlan
        ? string.Join("; ", assessment.DisplayPlan.Entries.Select(e => $"{e.Product}: {e.ApplicationTiming}")) + ". DAE means days after emergence; confirm actual crop stage."
        : "Refresh after the crop-cycle details or priority issue are resolved.";
    public string Amount => HasPlan
        ? string.Join("; ", assessment.DisplayPlan.Entries.Select(e => $"{e.Product}: {e.KgForPlot.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} kg for this plot")) + ". Planned program totals; account for fertilizer already applied."
        : "Complete the current crop-cycle setup to display its calculated program quantities.";
}
