namespace CropCare.Web.Models;

public sealed class RecommendationSummary(ZoneAssessmentDto assessment)
{
    private static readonly HashSet<string> BlockingCodes = new(StringComparer.Ordinal)
    {
        "CHECK_SENSOR", "CHECK_REGISTRATION", "AWAIT_READINGS", "LOW_MOISTURE", "HIGH_MOISTURE",
        "LOW_PH", "HIGH_PH", "EC_REVIEW", "TEMPERATURE_REVIEW", "PLANTING_DATE_REQUIRED", "POST_HARVEST"
    };
    public bool CanReviewApplication => assessment.ReferenceFertilizerPlan.Entries.Count > 0 &&
        assessment.Recommendations.Any(a => a.Code == "NUTRIENT_REVIEW") &&
        !assessment.Recommendations.Any(a => BlockingCodes.Contains(a.Code));
    public string What => CanReviewApplication
        ? string.Join("; ", assessment.ReferenceFertilizerPlan.Entries.Select(e => $"{e.Product} ({e.FertilizerGrade})"))
        : "Hold new fertilizer applications. " + assessment.PrimaryAction?.WhatToDo;
    public string How => CanReviewApplication
        ? "Follow each product label. Check root-zone moisture and account for fertilizer already applied this cycle."
        : "Complete the priority action above, then refresh the assessment before choosing fertilizer.";
    public string When => CanReviewApplication
        ? string.Join("; ", assessment.ReferenceFertilizerPlan.Entries.Select(e => $"{e.Product}: {e.ApplicationTiming}")) + ". Confirm emergence and actual crop stage first."
        : "Check the field now; reassess after the priority check and a fresh usable reading.";
    public string Amount => CanReviewApplication
        ? string.Join("; ", assessment.ReferenceFertilizerPlan.Entries.Select(e => $"{e.Product}: {e.KgForPlot.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} kg for this plot")) + ". Area-scaled program quantities; account for previous applications."
        : "No new fertilizer amount is advised for this assessment.";
}
