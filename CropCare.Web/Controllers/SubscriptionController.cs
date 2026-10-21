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
            using var req = Authorized(HttpMethod.Get, "api/subscriptions/me", token);
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
    public async Task<IActionResult> Submit(SubscriptionPageViewModel model, CancellationToken ct)
    {
        var (token, session) = SessionToken();
        if (token == null) return await Expired(session);

        if (!ModelState.IsValid) return await Reload(model, token, ct);

        try
        {
            var client = _clients.CreateClient("CropCareApi");
            using var req = Authorized(HttpMethod.Post, "api/subscriptions/request", token);
            req.Content = JsonContent.Create(new
            {
                referenceNumber = model.ReferenceNumber.Trim()
            });
            using var response = await client.SendAsync(req, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized) return await Expired(session);
            if (response.IsSuccessStatusCode)
            {
                TempData["Success"] = "Receipt sent for manual review. Your subscription is not active yet.";
                return RedirectToAction(nameof(Index));
            }
            ModelState.AddModelError("", response.StatusCode switch
            {
                HttpStatusCode.Conflict => "A request is already pending, your account is Pro, or the reference was already used.",
                HttpStatusCode.BadRequest => "Enter a valid GCash reference number.",
                _ => "Could not submit the receipt. Please try again later."
            });
        }
        catch (HttpRequestException)
        { ModelState.AddModelError("", "CropCare API is unreachable."); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        { ModelState.AddModelError("", "Submission timed out."); }
        return await Reload(model, token, ct);
    }

    private async Task<IActionResult> Reload(SubscriptionPageViewModel model,
        string token, CancellationToken ct)
    {
        try
        {
            var client = _clients.CreateClient("CropCareApi");
            using var req = Authorized(HttpMethod.Get, "api/subscriptions/me", token);
            using var response = await client.SendAsync(req, ct);
            if (response.IsSuccessStatusCode)
                model.Status = await response.Content
                    .ReadFromJsonAsync<SubscriptionStatusDto>(cancellationToken: ct)
                    ?? new SubscriptionStatusDto();
        }
        catch (HttpRequestException) { model.Error = "Could not refresh subscription status."; }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        { model.Error = "Status request timed out."; }
        return View("Index", model);
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