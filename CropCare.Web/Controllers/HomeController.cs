
using Microsoft.AspNetCore.Mvc;

namespace CropCare.Web.Controllers;

public class HomeController : Controller
{
    private readonly IHttpClientFactory _httpClientFactory;

    public HomeController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<IActionResult> Index()
    {
        try
        {
            var client = _httpClientFactory
                .CreateClient("CropCareApi");

            using var response =
                await client.GetAsync("api/health");

            ViewBag.ApiStatus = response.IsSuccessStatusCode
                ? "Connected to CropCare API"
                : $"API returned HTTP {(int)response.StatusCode}";
        }
        catch (HttpRequestException)
        {
            ViewBag.ApiStatus = "Cannot connect to API";
        }
        catch (TaskCanceledException)
        {
            ViewBag.ApiStatus = "API request timed out";
        }

        return View();
    }
}
