using System.ComponentModel.DataAnnotations;
using System.Net;
using CropCare.Web.Models;
using Xunit;
namespace CropCare.Web.Tests;
public sealed class AccountBoundaryTests
{
    [Theory]
    [InlineData(7,false)][InlineData(8,true)][InlineData(128,true)][InlineData(129,false)]
    public void NewPasswordBoundariesAreEnforced(int length,bool valid)
    {
        var password=new string('p',length);
        var model=new RegisterViewModel{FirstName="Test",LastName="Farmer",Address="Field",Username="farmer",Email="fixture@example.test",Password=password,ConfirmPassword=password};
        Assert.Equal(valid,Validator.TryValidateObject(model,new ValidationContext(model),new List<ValidationResult>(),true));
    }
    [Fact] public async Task ExpiredRecoveryCodesCannotReachReset()
    {
        await using var factory=new FixtureFactory{RecoveryExpired=true};using var client=factory.Client();
        await WebWorkflowTests.Post(client,"/Recovery/Request","/Recovery/Request",new(){{"Email","fixture@example.test"}});
        using var verify=await WebWorkflowTests.Post(client,"/Recovery/Verify","/Recovery/Verify",new(){{"Code","001234"}});
        Assert.Contains("Invalid or expired code",await verify.Content.ReadAsStringAsync());
        using var reset=await client.GetAsync("/Recovery/Reset");Assert.Equal(HttpStatusCode.Redirect,reset.StatusCode);
        Assert.False(factory.ResetConsumed);
    }
    [Fact] public async Task PartialClearReportsRemainingHistoryAndEmptyPagesRemainTruthful()
    {
        await using var factory=new FixtureFactory();using var client=factory.Client();await WebWorkflowTests.Login(client);
        await WebWorkflowTests.Post(client,"/Notifications","/Notifications/ClearAll",new());
        Assert.Contains("notifications remain",await client.GetStringAsync("/Notifications"));
        factory.Empty=true;
        var dashboard=await client.GetStringAsync("/Dashboard");Assert.DoesNotContain("0%",dashboard);
        Assert.Contains("No recorded readings",await client.GetStringAsync("/Sensors/History/s1"));
    }
    [Fact] public async Task UnknownMembershipAndPartialStaleReadingsAreExplicit()
    {
        await using var factory=new FixtureFactory{Membership="unknown"};using var client=factory.Client();await WebWorkflowTests.Login(client);
        var html=await client.GetStringAsync("/Zones/Details/z1");Assert.Contains("Subscription status is unavailable",html);
        Assert.Contains("Historical / stale",html);Assert.Contains("Partial measurements",html);
        Assert.DoesNotContain("View Recommendations</button>",html);
    }}
