using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CropCare.Web.Models;
using CropCare.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CropCare.Web.Controllers;

[Authorize]
public sealed class SubscriptionController : Controller
{
    private readonly IHttpClientFactory _clients;
    private readonly FirebaseTokenStore _tokens;

    public SubscriptionController(IHttpClientFactory clients, FirebaseTokenStore tokens)
    {
        _clients = clients;
        _tokens = tokens;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var (token, session) = SessionToken();
        if (token == null) return await Expired(session);

        var model = new SubscriptionPageViewModel();
        try
        {
            var client = _clients.CreateClient("CropCareApi");
            using var req = Authorized(HttpMethod.Get, "api/subscriptions/me/demo", token);
            using var response = await client.SendAsync(req, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized) return await Expired(session);
            if (response.IsSuccessStatusCode)
            {
                model.Status = await response.Content
                    .ReadFromJsonAsync<SubscriptionStatusDto>(cancellationToken: ct)
                    ?? new SubscriptionStatusDto();
            }
            else model.Error = "Unable to check subscription status. Confirm the new API is running.";
        }
        catch (HttpRequestException) { model.Error = "CropCare API is unreachable."; }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        { model.Error = "Subscription status request timed out."; }
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetDemoPlan(bool? isSubscribed, CancellationToken ct)
    {
        var (token, session) = SessionToken();
        if (token == null) return await Expired(session);
        if (isSubscribed is null) return BadRequest();
        try
        {
            using var request = Authorized(HttpMethod.Put, "api/subscriptions/me/demo", token);
            request.Content = JsonContent.Create(new { isSubscribed });
            using var response = await _clients.CreateClient("CropCareApi").SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized) return await Expired(session);
            if (!response.IsSuccessStatusCode)
                TempData["Error"] = "Could not update your demo plan.";
        }
        catch (HttpRequestException) { TempData["Error"] = "CropCare API is unreachable."; }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        { TempData["Error"] = "The request timed out. Check your current plan below."; }
        return RedirectToAction(nameof(Index));
    }

    private (string? Token, string? Session) SessionToken()
    {
        var session = User.FindFirst("CropCareSession")?.Value;
        return (_tokens.GetIdToken(session), session);
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token)
    {
        var req = new HttpRequestMessage(method, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    private async Task<IActionResult> Expired(string? session)
    {
        _tokens.RemoveSession(session);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        TempData["Error"] = "Your session expired. Please log in again.";
        return RedirectToAction("Login", "Account");
    }
}
