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