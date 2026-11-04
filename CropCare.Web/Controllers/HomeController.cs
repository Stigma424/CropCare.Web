using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CropCare.Web.Models;
namespace CropCare.Web.Controllers;
public sealed class HomeController : Controller
{
    [HttpGet] public IActionResult Index() => RedirectToAction("Index", "Dashboard");
    [HttpGet] public IActionResult Privacy() => View();
    [AllowAnonymous, ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        Response.StatusCode = 500;
        return View("~/Views/Shared/Error.cshtml", new ErrorViewModel { RequestId = HttpContext.TraceIdentifier });
    }
}
