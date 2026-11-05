using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CropCare.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
namespace CropCare.Web.Tests;

// Test-only handlers: never registered in the production website, never access Firestore.
public sealed class FixtureFactory : WebApplicationFactory<Program>
{
    public string Membership = "free";
    public bool BadAssessment;
    public bool Malformed;
    public bool Unauthorized;
    public bool ResetConsumed;
    public bool RecoveryExpired;
    public bool Empty;
    public bool RegistrationConflict;
    public bool ProfileMissing;
    public List<(string Method,string Path,string? Bearer)> Calls = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(new Dictionary<string,string?> {
            ["CropCareApi:BaseUrl"]="https://fixture-api.test/", ["Firebase:WebApiKey"]="fixture-only-key"
        }));
        builder.ConfigureServices(services=> {
            services.AddHttpClient("CropCareApi").ConfigurePrimaryHttpMessageHandler(()=>new ApiHandler(this));
            services.AddHttpClient<FirebaseAuthRestClient>().ConfigurePrimaryHttpMessageHandler(()=>new FirebaseHandler());
        });
    }
    public HttpClient Client(bool cookies=true) => CreateClient(new WebApplicationFactoryClientOptions { BaseAddress=new Uri("https://cropcare.test"), AllowAutoRedirect=false, HandleCookies=cookies });
    private static HttpResponseMessage Json(object value, HttpStatusCode status=HttpStatusCode.OK) => new(status){ Content=JsonContent.Create(value) };
    private sealed class FirebaseHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) =>
            Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("signInWithCustomToken")
                ? Json(new { idToken="fixture-id-token",expiresIn="3600" })
                : Json(new { idToken="fixture-id-token",localId="owner",email="fixture@example.test",expiresIn="3600" }));
    }
    private sealed class ApiHandler(FixtureFactory f) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var path=request.RequestUri!.AbsolutePath;
            f.Calls.Add((request.Method.Method,path,request.Headers.Authorization?.ToString()));
            if(path=="/api/auth/login") {
                var input=await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken:ct);
                return input.GetProperty("username").GetString()!.Equals("farmer",StringComparison.OrdinalIgnoreCase) && input.GetProperty("password").GetString()=="test-password"
                    ? Json(new{customToken="fixture-custom-token",userId="owner"}) : Json(new{error="Invalid username or password."},HttpStatusCode.Unauthorized);
            }
            if(path.StartsWith("/api/recovery/")) {
                if(path.EndsWith("/request")) return Json(new{message="If the account exists, a code will be sent."},HttpStatusCode.Accepted);
                if(path.EndsWith("/verify")) return f.RecoveryExpired ? Json(new{error="Invalid or expired code"},HttpStatusCode.BadRequest) : Json(new{resetTicket=new string('A',64)});
                if(f.ResetConsumed) return Json(new{error="Used ticket"},HttpStatusCode.BadRequest);
                f.ResetConsumed=true; return Json(new{message="Updated"});
            }
            if(request.Headers.Authorization?.ToString()!="Bearer fixture-id-token" || f.Unauthorized) return Json(new{error="Expired"},HttpStatusCode.Unauthorized);
            if(f.Malformed) return new(HttpStatusCode.OK){Content=new StringContent("<html>invalid upstream</html>")};
            if(path=="/api/account/me") return f.ProfileMissing ? Json(new{error="Missing"},HttpStatusCode.NotFound) : Json(new{userId="owner",firstName="Test",middleName="",lastName="Farmer",username="farmer",email="fixture@example.test",address="Test field",phoneNumber=""});
            if(path=="/api/onboarding/profile") return f.RegistrationConflict ? Json(new{error="Conflict"},HttpStatusCode.Conflict) : Json(new{userId="owner"},HttpStatusCode.Created);
            if(path=="/api/subscriptions/me") return Json(new{status=f.Membership,isSubscribed=f.Membership=="pro",message="Fixture account state",referenceLastFour="1234"});
            if(path=="/api/subscriptions/request") { f.Membership="pending"; return Json(new{status="pending"},HttpStatusCode.Accepted); }
            if(path.StartsWith("/api/zones/z1/assessment")) {
                if(f.Membership!="pro")return Json(new{error="Pro required"},HttpStatusCode.Forbidden);
                if(f.BadAssessment)return Json(new{soilHealth=new{score=100}});
                var assessment=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","assessment.json")))!;
                assessment["zoneId"]="z1";
                if(request.RequestUri.Query.Contains("HYB-2")){assessment["programId"]="HYB-2";assessment["referenceFertilizerPlan"]!["entries"]![1]!["kgForPlot"]=11.8;}
                return path.EndsWith("historical-preview")?Json(new{historical=true,snapshotAtMs=1793750400000L,sensorsFound=1,warning="Fixture historical only",assessment}):Json(assessment);
            }            var zone=new {zoneId="z1",zoneName="Test Corn Zone",zoneAreaSqm=1000,dateOfPlanting=1761926400000L,isHarvested=false};
            var harvested=new {zoneId="z2",zoneName="Harvested Test Zone",zoneAreaSqm=500,dateOfPlanting=1761926400000L,isHarvested=true};
            if(path=="/api/zones") return Json(f.Empty ? Array.Empty<object>() : new object[]{zone,harvested});
            if(path=="/api/zones/z1" && request.Method==HttpMethod.Delete) return Json(new{error="Remove registered sensors before deleting this zone."},HttpStatusCode.Conflict);
            if(path=="/api/zones/z1") return request.Method==HttpMethod.Get ? Json(zone) : Json(new{message="Updated"});
            if(path=="/api/zones/z2") return Json(harvested);
            if(path.Contains("/other")) return Json(new{error="Not found"},HttpStatusCode.NotFound);
            var sensor=new{sensorId="s1",deviceId="TEST_DEVICE",sensorName="Test Soil Sensor",zoneId="z1",userId="owner",pairingStatus="qr_claimed",isOnline=false,lastScanTimestamp=0L};
            if(path=="/api/sensors") return Json(f.Empty ? Array.Empty<object>() : new object[]{sensor});
            if(path=="/api/sensors/s1" && request.Method==HttpMethod.Delete) return Json(new{qrReusable=true,message="Sensor unpaired. Its original QR can be used again."});
            if(path=="/api/sensors/s1") return request.Method==HttpMethod.Get ? Json(sensor) : Json(new{message="Updated"});
            var reading=new{readingId="r1",recordedAtMs=1762185600000L,nitrogen=(double?)0,phosphorus=(double?)30,potassium=(double?)40,moisture=(double?)28,ph=(double?)5.7,temperature=(double?)31,ec=(double?)null};
            if(path=="/api/sensors/s1/monitoring") return Json(new{sensorId="s1",deviceId="TEST_DEVICE",sensorName="Test Soil Sensor",pairingStatus="qr_claimed",status=f.Empty ? "awaiting_first_reading" : "stale",lastReadingAtMs=reading.recordedAtMs,reading=f.Empty ? null : (object)reading});
            if(path=="/api/sensors/s1/readings") return Json(new{sensorId="s1",sensorName="Test Soil Sensor",readings=f.Empty ? Array.Empty<object>() : new object[]{reading},nextCursor=request.RequestUri.Query.Contains("cursor") ? null : "r1"});
            if(path=="/api/notifications" && request.Method==HttpMethod.Get) return Json(f.Empty ? Array.Empty<object>() : new object[]{new{id="n1",title="Fixture soil alert",message="Test-only notification",timestampMs=1762185600000L,isRead=false,type="soil",zoneId="z1",sensorId="s1"}});
            if(path.StartsWith("/api/notifications")) return Json(new{message="Updated",removed=5000});
            return Json(new{error="Fixture endpoint unavailable"},HttpStatusCode.NotFound);
        }
    }
}
