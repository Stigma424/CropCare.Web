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
        Assert.Contains("Fertilizer reference",html); Assert.Contains("Sources and limitations",html);
        Assert.Contains("cc-advisory-section",html); Assert.Contains("Back to zone",html);
        using var other=await client.GetAsync("/Advisories/Current?zoneId=other");
        Assert.Equal(HttpStatusCode.NotFound,other.StatusCode);
    }
}
