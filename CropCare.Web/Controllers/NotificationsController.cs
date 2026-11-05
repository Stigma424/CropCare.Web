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
public sealed class NotificationsController : Controller
{
    private readonly IHttpClientFactory _clients;
    private readonly FirebaseTokenStore _tokens;

    public NotificationsController(IHttpClientFactory clients, FirebaseTokenStore tokens)
    {
        _clients = clients;
        _tokens = tokens;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var session = User.FindFirst("CropCareSession")?.Value;
        var token = _tokens.GetIdToken(session);
        if (string.IsNullOrEmpty(token)) return await Expired(session);
        var model = new NotificationsPageModel();
        try
        {
            var api = _clients.CreateClient("CropCareApi");
            using var request = Authenticated(HttpMethod.Get, "api/notifications?limit=100", token);
            using var response = await api.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized) return await Expired(session);
            if (!response.IsSuccessStatusCode)
            {
                model.Error = "Could not load notification history.";
                return View(model);
            }
            model.Notifications = await response.Content
                .ReadFromJsonAsync<List<CropCareNotification>>(cancellationToken: ct) ?? new();
        }
        catch (HttpRequestException) { model.Error = "Cannot connect to CropCare API."; }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        { model.Error = "Notification request timed out."; }
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Feed(CancellationToken ct)
    {
        var token=_tokens.GetIdToken(User.FindFirst("CropCareSession")?.Value);
        if(token is null)return Unauthorized();
        using var request=Authenticated(HttpMethod.Get,"api/notifications?limit=100",token);
        try {
            using var response=await _clients.CreateClient("CropCareApi").SendAsync(request,ct);
            if(response.StatusCode==HttpStatusCode.Unauthorized)return Unauthorized();
            if(!response.IsSuccessStatusCode)return StatusCode(503);
            var rows=await response.Content.ReadFromJsonAsync<List<CropCareNotification>>(cancellationToken:ct);
            return Json(new {items=rows??new(),scope="latest100"});
        }
        catch(HttpRequestException){return StatusCode(503);}
        catch(System.Text.Json.JsonException){return StatusCode(503);}
        catch(TaskCanceledException)when(!ct.IsCancellationRequested){return StatusCode(504);}
    }
    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> MarkRead(string id, CancellationToken ct) =>
        Change(HttpMethod.Post, id, "read", ct);

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Remove(string id, CancellationToken ct) =>
        Change(HttpMethod.Delete, id, null, ct);

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> ClearAll(CancellationToken ct) =>
        Change(HttpMethod.Delete, null, null, ct);

    private async Task<IActionResult> Change(HttpMethod method, string? id,
        string? suffix, CancellationToken ct)
    {
        var session = User.FindFirst("CropCareSession")?.Value;
        var token = _tokens.GetIdToken(session);
        if (string.IsNullOrWhiteSpace(token)) return await Expired(session);
        if (id != null && (!ValidId(id))) return BadRequest("Invalid notification ID.");
        var path = "api/notifications" + (id == null ? "" : "/" + Uri.EscapeDataString(id))
            + (suffix == null ? "" : "/" + suffix);
        try
        {
            var api = _clients.CreateClient("CropCareApi");
            using var request = Authenticated(method, path, token);
            using var response = await api.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized) return await Expired(session);
            if (response.IsSuccessStatusCode && id is null)
            {
                using var remainingRequest = Authenticated(HttpMethod.Get, "api/notifications?limit=1", token);
                using var remainingResponse = await api.SendAsync(remainingRequest, ct);
                if (remainingResponse.StatusCode == HttpStatusCode.Unauthorized) return await Expired(session);
                if (remainingResponse.IsSuccessStatusCode)
                {
                    var remaining = await remainingResponse.Content.ReadFromJsonAsync<List<CropCareNotification>>(cancellationToken: ct);
                    TempData["Message"] = remaining?.Count > 0
                        ? "The clear request completed, but notifications remain. The server clears at most 5,000 per request; refresh and choose Clear All again if needed."
                        : "Notification history cleared.";
                }
                else TempData["Error"] = "The clear request completed, but remaining history could not be checked. Refresh to verify.";
                return RedirectToAction(nameof(Index));
            }
            TempData[response.IsSuccessStatusCode ? "Message" : "Error"] =
                response.IsSuccessStatusCode ? "Notification updated." : "Could not update notification.";
        }
        catch (HttpRequestException) { TempData["Error"] = "Cannot connect to CropCare API."; }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        { TempData["Error"] = "The request timed out."; }
        return RedirectToAction(nameof(Index));
    }

    private static bool ValidId(string? id) => !string.IsNullOrWhiteSpace(id)
        && id.Length <= 256 && id != "." && id != ".." && !id.Contains('/');

    private static HttpRequestMessage Authenticated(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async Task<IActionResult> Expired(string? session)
    {
        _tokens.RemoveSession(session);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        TempData["Error"] = "Your session expired. Please log in again.";
        return RedirectToAction("Login", "Account");
    }
}
