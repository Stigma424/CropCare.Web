using Microsoft.Playwright;
using Xunit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
namespace CropCare.Web.Tests;

public sealed class BrowserTests
{
    [Fact]
    public async Task FarmerPagesRenderAndNavigateAcrossDesktopAndPhoneWidths()
    {
        await using var factory=new FixtureFactory(); using var backend=factory.Client(cookies:false);
        using var playwright=await Playwright.CreateAsync();
        await using var browser=await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions {
            Headless=true, Channel=Environment.GetEnvironmentVariable("CROPCARE_TEST_BROWSER")
        });
        var host=WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName="Testing" });
        host.Logging.ClearProviders();
        host.WebHost.ConfigureKestrel(k=>k.Listen(System.Net.IPAddress.Loopback,0));
        await using var bridge=host.Build();
        bridge.Run(async incoming=>{
            using var request=new HttpRequestMessage(new HttpMethod(incoming.Request.Method),
                $"https://{incoming.Request.Host}{incoming.Request.Path}{incoming.Request.QueryString}");
            if(incoming.Request.ContentLength>0 || incoming.Request.Headers.ContainsKey("Transfer-Encoding")) {
                using var buffer=new MemoryStream();await incoming.Request.Body.CopyToAsync(buffer);request.Content=new ByteArrayContent(buffer.ToArray());
            }
            foreach(var header in incoming.Request.Headers) {
                if(header.Key.Equals("Content-Length",StringComparison.OrdinalIgnoreCase))continue;
                if(!request.Headers.TryAddWithoutValidation(header.Key,header.Value.ToArray()))request.Content?.Headers.TryAddWithoutValidation(header.Key,header.Value.ToArray());
            }
            using var response=await backend.SendAsync(request);
            incoming.Response.StatusCode=(int)response.StatusCode;
            foreach(var header in response.Headers.Concat(response.Content.Headers)) {
                if(header.Key.Equals("Transfer-Encoding",StringComparison.OrdinalIgnoreCase)||header.Key.Equals("Content-Length",StringComparison.OrdinalIgnoreCase))continue;
                incoming.Response.Headers[header.Key]=header.Value.ToArray();
            }
            var bytes=await response.Content.ReadAsByteArrayAsync();incoming.Response.ContentLength=bytes.Length;
            await incoming.Response.Body.WriteAsync(bytes);
        });
        await bridge.StartAsync();
        var origin=bridge.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var context=await browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize=new(){Width=1440,Height=950},IgnoreHTTPSErrors=true });
        var page=await context.NewPageAsync();
        var errors=new List<string>();page.PageError+=(_,error)=>errors.Add(error);
        await page.RouteAsync("**/*",async route=> { if(route.Request.Url.StartsWith(origin+"/",StringComparison.Ordinal))await route.ContinueAsync();else await route.AbortAsync(); });
        var artifacts=Environment.GetEnvironmentVariable("CROPCARE_ARTIFACT_DIR") ?? Path.Combine(AppContext.BaseDirectory,"TestResults","screenshots");Directory.CreateDirectory(artifacts);
        async Task Check(string path,string screenshot)
        {
            var response=await page.GotoAsync(origin+path);Assert.True(response?.Status<400,$"Unexpected status at {path}: {response?.Status}");
            await page.EvaluateAsync("() => document.fonts.ready.then(() => true)");
            Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth + 1"),$"Horizontal overflow at {path}");
            await page.ScreenshotAsync(new PageScreenshotOptions { Path=Path.Combine(artifacts,screenshot+".png"),FullPage=true });
        }
        await Check("/Account/Login","login-desktop");
        await page.Locator("#Password").FillAsync("test-password");await page.Locator("[data-password-toggle]").ClickAsync();Assert.Equal("text",await page.Locator("#Password").GetAttributeAsync("type"));
        await Check("/Account/Register","register-desktop");
        await Check("/Recovery/Request","recovery-request");await page.Locator("#Email").FillAsync("fixture@example.test");await page.GetByRole(AriaRole.Button,new(){Name="Request Password Reset"}).ClickAsync();await page.WaitForURLAsync("**/Recovery/Verify");
        await Check("/Recovery/Verify","recovery-verify");await page.Locator("#Code").FillAsync("001234");await page.GetByRole(AriaRole.Button,new(){Name="Submit",Exact=true}).ClickAsync();await page.WaitForURLAsync("**/Recovery/Reset");
        await Check("/Recovery/Reset","recovery-reset");
        await page.GotoAsync(origin+"/Account/Login");await page.Locator("#Username").FillAsync("FaRmEr");await page.Locator("#Password").FillAsync("test-password");await page.GetByRole(AriaRole.Button,new(){Name="LOGIN",Exact=true}).ClickAsync();await page.WaitForURLAsync(url => url == origin+"/" || url == origin+"/Dashboard");
        await Check("/Dashboard","dashboard-desktop");Assert.Equal(2,await page.Locator("[data-zone-card]").CountAsync());await page.Locator("[data-zone-filter='z1']").ClickAsync();Assert.Equal(1,await page.Locator("[data-zone-card]:visible").CountAsync());
        await Check("/Zones/Create","add-zone");await page.Locator("[data-cal-step='1']").ClickAsync();
        foreach(var target in new[]{("/Zones/Details/z1","zone"),("/Zones/Details/z2","harvested"),("/Zones/Edit/z1","edit-zone"),("/Sensors?zoneId=z1","sensor-status"),("/Sensors/Details/s1","sensor-details"),("/Sensors/History/s1?metric=nitrogen","history"),("/Sensors/Pair?zoneId=z1","pair"),("/Sensors/Rename/s1","rename"),("/Notifications","notifications"),("/Settings","settings"),("/Settings/Help","help"),("/Profile","account-settings"),("/Profile/Edit","profile-edit"),("/Profile/ChangePassword","change-password"),("/Account/ConfirmLogout","logout"),("/Subscription","subscription"),("/Home/Privacy","privacy")}) await Check(target.Item1,target.Item2);
        await page.GotoAsync(origin+"/Sensors/History/s1"); await page.Locator("#historyDate").FillAsync("2020-01-01"); await page.Locator("#historyDate").DispatchEventAsync("change"); Assert.Equal(0,await page.Locator("[data-history-card]:visible").CountAsync()); Assert.True(await page.GetByRole(AriaRole.Link,new(){Name="Load older readings"}).IsVisibleAsync()); await page.Locator("#clearHistoryDate").ClickAsync(); Assert.Equal(1,await page.Locator("[data-history-card]:visible").CountAsync());
        factory.Membership="pro";
        await Check("/Zones/Details/z1","zone-api-pro");
        await page.GetByRole(AriaRole.Button,new(){Name="View Recommendations",Exact=true}).ClickAsync();
        await page.Locator(".cc-advisory-hero").WaitForAsync();
        Assert.Contains("YOUR NEXT STEP",await page.Locator("#zoneRecommendationContent").InnerTextAsync());
        Assert.False(await page.Locator("#recommendationProgram").IsVisibleAsync());
        await page.GetByText("Reference program options",new(){Exact=true}).ClickAsync();
        await page.Locator("#recommendationProgram").SelectOptionAsync("HYB-2");
        await Assertions.Expect(page.Locator("#recommendationProgram")).ToHaveValueAsync("HYB-2");
        await page.Locator("summary").Filter(new(){HasText="Fertilizer reference"}).ClickAsync();
        await page.GetByText("11.80 kg for this plot",new(){Exact=false}).WaitForAsync();
        await page.ScreenshotAsync(new(){Path=Path.Combine(artifacts,"shared-api-advisory.png"),FullPage=true});
        await page.Locator("#zoneRecommendationContent .btn-close").ClickAsync();
        await Check("/Advisories/Current?zoneId=z1","advice-full-page-desktop");
        await page.SetViewportSizeAsync(390,844);await Check("/Dashboard","dashboard-phone");
        await Check("/Advisories/Current?zoneId=z1","advice-full-page-phone");
        await Check("/Dashboard","dashboard-phone-return");
        await page.Locator(".cc-nav-toggle").ClickAsync();Assert.Equal("true",await page.Locator(".cc-nav-toggle").GetAttributeAsync("aria-expanded"));await page.Keyboard.PressAsync("Escape");Assert.Equal("false",await page.Locator(".cc-nav-toggle").GetAttributeAsync("aria-expanded"));
        await Check("/Sensors/History/s1","history-phone");await Check("/Zones/Create","add-zone-phone");
        await page.SetViewportSizeAsync(768,1024);await Check("/Settings","settings-tablet");
        Assert.Empty(errors);
    }
}
