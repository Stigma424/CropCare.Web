using System.Net;
using Xunit;
namespace CropCare.Web.Tests;
public sealed class RecommendationPresentationTests
{
    [Fact] public async Task FullPageAdviceKeepsOwnedDataAndSupportingDetailsAvailable()
    {
        await using var factory=new FixtureFactory { Membership="pro" }; using var client=factory.Client();
        await WebWorkflowTests.Login(client);
        using var response=await client.GetAsync("/Advisories/Current?zoneId=z1");
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var html=await response.Content.ReadAsStringAsync();
        Assert.Contains("YOUR NEXT STEP",html); Assert.Contains("Sensor readings and quality",html);
        Assert.Contains("What to apply",html); Assert.Contains("How to apply",html);
        Assert.Contains("When to apply",html); Assert.Contains("How much to apply",html);
        Assert.Contains("View more details",html); Assert.Contains("<h4>Sources</h4>",html);
        Assert.DoesNotContain("<details",html); Assert.DoesNotContain("provisional",html,StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cc-advisory-section",html); Assert.Contains("Back to zone",html);
        using var other=await client.GetAsync("/Advisories/Current?zoneId=other");
        Assert.Equal(HttpStatusCode.NotFound,other.StatusCode);
    }
    [Fact] public void ApplicationSummaryKeepsFieldChecksAheadOfProgramQuantities()
    {
        var assessment = new CropCare.Web.Models.ZoneAssessmentDto {
            PrimaryAction = new() { WhatToDo = "Restore moisture first" },
            Recommendations = [new() { Code = "NUTRIENT_REVIEW" }],
            ReferenceFertilizerPlan = new() { Entries = [new() { Product = "Urea", FertilizerGrade = "46-0-0", KgForPlot = 5.6, ApplicationTiming = "25 DAE" }] }
        };
        var summary = new CropCare.Web.Models.RecommendationSummary(assessment);
        Assert.True(summary.CanReviewApplication); Assert.Contains("5.60 kg", summary.Amount);
        assessment.Recommendations.Add(new() { Code = "HIGH_MOISTURE" });
        Assert.False(summary.CanReviewApplication); Assert.Contains("Before applying", summary.ApplicationNote);
        Assert.Contains("5.60", summary.Amount);
        assessment.Recommendations.Clear();
        Assert.False(summary.CanReviewApplication);
        Assert.Contains("5.60", summary.Amount);
        assessment.ReferenceFertilizerPlan.Entries.Clear();
        Assert.DoesNotContain("5.60", summary.Amount);
    }
}

