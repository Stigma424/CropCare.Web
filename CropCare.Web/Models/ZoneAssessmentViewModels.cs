
namespace CropCare.Web.Models;

// ==========================================
// ADVISORY PAGE MODEL
// ==========================================

public sealed class ZoneAssessmentPageModel
{
    public List<ZoneRecord> Zones { get; set; } = new();

    public string? SelectedZoneId { get; set; }

    public string Mode { get; set; } = "live";

    public string? Error { get; set; }

    public ZoneAssessmentDto? Assessment { get; set; }

    public HistoricalPreviewDto? Preview { get; set; }

    public bool IsHistorical => Mode == "history";
}

// ==========================================
// COMPLETE ZONE ASSESSMENT
// ==========================================

public sealed class ZoneAssessmentDto
{
    public string ZoneId { get; set; } = "";

    public string ZoneName { get; set; } = "";

    public int? DaysAfterPlanting { get; set; }

    public string CropStage { get; set; } = "";

    public long EvaluatedAtMs { get; set; }

    public string RulesetVersion { get; set; } = "";

    public SoilHealthDto SoilHealth { get; set; } = new();

    public SensorAllocationDto SensorAllocation
    {
        get;
        set;
    } = new();

    public List<RecommendationActionDto> Recommendations
    {
        get;
        set;
    } = new();

    public FertilizerReferenceDto ReferenceFertilizerPlan
    {
        get;
        set;
    } = new();

    public List<string> SourceIds { get; set; } = new();

    public List<string> Limitations { get; set; } = new();
}

// ==========================================
// SOIL HEALTH CARD
// ==========================================

public sealed class SoilHealthDto
{
    public double? Score { get; set; }

    public string Label { get; set; } =
        "Insufficient data";

    public string Quality { get; set; } =
        "insufficient_data";

    public int ValidSensors { get; set; }

    public List<ParameterScoreDto> Parameters
    {
        get;
        set;
    } = new();
}

public sealed class ParameterScoreDto
{
    public string Parameter { get; set; } = "";

    public double Value { get; set; }

    public string Unit { get; set; } = "";

    public int Score { get; set; }

    public string Status { get; set; } = "";

    public string RuleId { get; set; } = "";
}

// ==========================================
// SENSOR ALLOCATION
// ==========================================

public sealed class SensorAllocationDto
{
    public int Minimum { get; set; }

    public int Suggested { get; set; }

    public int Maximum { get; set; }

    public int Installed { get; set; }

    public int ReportingRecently { get; set; }

    public int AdditionalSuggested { get; set; }

    public double DeploymentCompletenessPercent
    {
        get;
        set;
    }

    public string PlacementGuidance { get; set; } = "";
}

// ==========================================
// RECOMMENDATION ACTIONS
// ==========================================

public sealed class RecommendationActionDto
{
    public string Code { get; set; } = "";

    public string Priority { get; set; } = "info";

    public string Title { get; set; } = "";

    public string WhatToDo { get; set; } = "";

    public string Why { get; set; } = "";

    public string SourceId { get; set; } = "";

    public bool PushCandidate { get; set; }
}

// ==========================================
// PUBLISHED FERTILIZER REFERENCE
// ==========================================

public sealed class FertilizerReferenceDto
{
    public string Crop { get; set; } = "";

    public string Applicability { get; set; } = "";

    public string SourceId { get; set; } = "";

    public bool IsSiteSpecific { get; set; }

    public string Warning { get; set; } = "";

    public List<ReferenceFertilizerDto> Entries
    {
        get;
        set;
    } = new();
}

public sealed class ReferenceFertilizerDto
{
    public string Product { get; set; } = "";

    public string FertilizerGrade { get; set; } = "";

    public string ApplicationTiming { get; set; } = "";

    public double KgPerHectare { get; set; }

    public double KgForPlot { get; set; }
}

// ==========================================
// HISTORICAL PREVIEW RESPONSE
// ==========================================

public sealed class HistoricalPreviewDto
{
    public bool Historical { get; set; }

    public long? SnapshotAtMs { get; set; }

    public int SensorsFound { get; set; }

    public string Warning { get; set; } = "";

    public ZoneAssessmentDto? Assessment { get; set; }
}
