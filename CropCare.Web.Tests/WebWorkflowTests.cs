using System.Net;
using System.Text.RegularExpressions;
using CropCare.Web.Services;
using Xunit;
namespace CropCare.Web.Tests;

public sealed class WebWorkflowTests
{
    internal static async Task<string> Token(HttpClient client,string page)
    {
        var html=await client.GetStringAsync(page);
        return WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    }
    internal static async Task<HttpResponseMessage> Post(HttpClient client,string page,string action,Dictionary<string,string> values)
    {
        values["__RequestVerificationToken"]=await Token(client,page);
        return await client.PostAsync(action,new FormUrlEncodedContent(values));
    }
    internal static Task<HttpResponseMessage> Login(HttpClient client,string username="farmer",string? returnUrl=null) =>
        Post(client,"/Account/Login","/Account/Login",new(){{"Username",username},{"Password","test-password"},{"ReturnUrl",returnUrl??""}});
    [Fact] public async Task UsernameLoginUsesServerExchangeAndKeepsTokensOutOfHtml()
    {
        await using var factory=new FixtureFactory();using var client=factory.Client();
        using var login=await Login(client,"FaRmEr","https://attacker.example/");
        Assert.Equal(HttpStatusCode.Redirect,login.StatusCode);Assert.Contains(login.Headers.Location?.OriginalString, new[]{"/", "/Dashboard"});
        var html=await client.GetStringAsync("/Dashboard");Assert.Contains("Test Corn Zone",html);Assert.DoesNotContain("fixture-id-token",html);Assert.DoesNotContain("fixture-custom-token",html);
        Assert.Contains(factory.Calls,c=>c.Path=="/api/auth/login");Assert.All(factory.Calls.Where(c=>c.Path=="/api/zones"),c=>Assert.Equal("Bearer fixture-id-token",c.Bearer));
    }
    [Fact] public async Task InvalidLoginAndForgeryAreRejected()
    {
        await using var factory=new FixtureFactory();using var client=factory.Client();
        using var missing=await client.PostAsync("/Account/Login",new FormUrlEncodedContent(new Dictionary<string,string>{{"Username","farmer"},{"Password","test-password"}}));Assert.Equal(HttpStatusCode.BadRequest,missing.StatusCode);
        using var invalid=await Login(client,"unknown");Assert.Contains("Invalid username or password",await invalid.Content.ReadAsStringAsync());
        Assert.DoesNotContain(factory.Calls,c=>c.Path=="/api/account/me");
        using var protectedPage=await client.GetAsync("/Dashboard");Assert.Equal(HttpStatusCode.Redirect,protectedPage.StatusCode);
    }
    [Fact] public async Task SessionExpiryAndMalformedApiResponsesAreHandled()
    {
        await using var factory=new FixtureFactory();using var client=factory.Client();await Login(client);
        factory.Malformed=true;using var malformed=await client.GetAsync("/Notifications");Assert.Equal(HttpStatusCode.ServiceUnavailable,malformed.StatusCode);
        factory.Malformed=false;factory.Unauthorized=true;using var expired=await client.GetAsync("/Profile");Assert.Equal(HttpStatusCode.Redirect,expired.StatusCode);
        Assert.Contains("/Account/Login",expired.Headers.Location!.OriginalString);
        Assert.Equal(TimeSpan.FromMinutes(50),WebSessionPolicy.Lifetime(3600));Assert.True(WebSessionPolicy.Lifetime(30)<=TimeSpan.Zero);
    }
    [Theory][InlineData("free")][InlineData("pending")][InlineData("rejected")][InlineData("pro")]
    public async Task MembershipIsDisplayedWithoutClientActivation(string membership)
    {
        await using var factory=new FixtureFactory{Membership=membership};using var client=factory.Client();await Login(client);
        var html=await client.GetStringAsync("/Subscription");Assert.Contains(membership.ToUpperInvariant(),html);
        Assert.Equal(membership is "free" or "rejected",html.Contains("name=\"ReferenceNumber\""));
        Assert.DoesNotContain(factory.Calls,c=>c.Method=="PATCH"&&c.Path=="/api/account/me");
    }
    [Fact] public async Task HistoryPreservesSevenMetricsZeroNullAndPagination()
    {
        await using var factory=new FixtureFactory();using var client=factory.Client();await Login(client);
        var html=await client.GetStringAsync("/Sensors/History/s1?metric=ec");
        Assert.Contains("0 mg/kg",html);Assert.Contains("Phosphorus",html);Assert.Contains("Temperature",html);Assert.Contains("EC",html);Assert.Contains("—",WebUtility.HtmlDecode(html));Assert.Contains("Load older readings",html);Assert.Contains("this loaded page only",html);
        using var legacy=await client.GetAsync("/Monitoring/History/s1?metric=ph&cursor=r1");Assert.Equal(HttpStatusCode.Redirect,legacy.StatusCode);Assert.Contains("/Sensors/History/s1",legacy.Headers.Location!.OriginalString);
    }
    [Fact] public async Task MutationsUseAuthenticatedApiAndPreserveConflictResponses()
    {
        await using var factory=new FixtureFactory();using var client=factory.Client();await Login(client);
        using var denied=await client.GetAsync("/Sensors/Details/other");Assert.Equal(HttpStatusCode.Redirect,denied.StatusCode);
        using var conflict=await Post(client,"/Zones/Details/z1","/Zones/Delete/z1",new());Assert.Equal(HttpStatusCode.Redirect,conflict.StatusCode);
        Assert.Contains("Remove registered sensors",await client.GetStringAsync(conflict.Headers.Location!));
        using var read=await Post(client,"/Notifications","/Notifications/MarkRead",new(){{"id","n1"}});Assert.Equal(HttpStatusCode.Redirect,read.StatusCode);
        using var remove=await Post(client,"/Notifications","/Notifications/Remove",new(){{"id","n1"}});Assert.Equal(HttpStatusCode.Redirect,remove.StatusCode);
        using var clear=await Post(client,"/Notifications","/Notifications/ClearAll",new());Assert.Equal(HttpStatusCode.Redirect,clear.StatusCode);
        using var unpair=await Post(client,"/Sensors/Details/s1","/Sensors/Delete/s1",new());Assert.Equal(HttpStatusCode.Redirect,unpair.StatusCode);
        Assert.Contains("original QR can be used again",await client.GetStringAsync(unpair.Headers.Location!));
        Assert.Contains(factory.Calls,c=>c.Path=="/api/sensors/s1"&&c.Method=="DELETE");
    }
    [Fact] public async Task RegistrationRetryRecognizesCompletedProfileAndRecoveryUsesOneTimeTicket()
    {
        await using var factory=new FixtureFactory();using var client=factory.Client();
        using var registration=await Post(client,"/Account/Register","/Account/Register",new(){{"FirstName","Test"},{"LastName","Farmer"},{"Address","Field"},{"Username","farmer"},{"Email","fixture@example.test"},{"Password","test-password"},{"ConfirmPassword","test-password"}});
        Assert.Equal(HttpStatusCode.Redirect,registration.StatusCode);Assert.DoesNotContain(factory.Calls,c=>c.Path=="/api/onboarding/profile");
        using var request=await Post(client,"/Recovery/Request","/Recovery/Request",new(){{"Email","fixture@example.test"}});Assert.Equal(HttpStatusCode.Redirect,request.StatusCode);
        using var verify=await Post(client,"/Recovery/Verify","/Recovery/Verify",new(){{"Code","001234"}});Assert.Equal(HttpStatusCode.Redirect,verify.StatusCode);
        using var reset=await Post(client,"/Recovery/Reset","/Recovery/Reset",new(){{"NewPassword","new-password"},{"ConfirmPassword","new-password"}});Assert.Equal(HttpStatusCode.Redirect,reset.StatusCode);Assert.True(factory.ResetConsumed);
        using var again=await client.GetAsync("/Recovery/Reset");Assert.Equal(HttpStatusCode.Redirect,again.StatusCode);
    }
}
