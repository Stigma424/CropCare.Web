using System.Net;
using Xunit;

namespace CropCare.Web.Tests;

public sealed class RecoveryLinkTests
{
    [Fact] public async Task MissingLocalFirebaseKeyShowsLoginErrorInsteadOfUnhandledException()
    {
        await using var factory=new FixtureFactory { MissingFirebaseKey=true };
        using var client=factory.Client();
        using var response=await WebWorkflowTests.Post(client,"/Account/Login","/Account/Login",new(){{"Username","farmer"},{"Password","test-password"}});
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        Assert.Contains("Cannot reach Firebase or CropCare API",await response.Content.ReadAsStringAsync());
    }
    [Fact] public async Task LinkRecoveryDoesNotEnterCodeOrResetTicketFlow()
    {
        await using var factory=new FixtureFactory { RecoveryLink=true };
        using var client=factory.Client();
        using var response=await WebWorkflowTests.Post(client,"/Recovery/Request","/Recovery/Request",new(){{"Email","fixture@example.test"}});
        Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);
        Assert.EndsWith("/Recovery/LinkSent",response.Headers.Location?.ToString());
        Assert.Contains("Open the link",await client.GetStringAsync("/Recovery/LinkSent"));
        using var verify=await client.GetAsync("/Recovery/Verify");
        Assert.Equal(HttpStatusCode.Redirect,verify.StatusCode);
        Assert.False(factory.ResetConsumed);
    }
}
