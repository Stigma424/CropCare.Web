using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using CropCare.Web.Models;
using CropCare.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CropCare.Web.Controllers;

[Authorize]
public sealed class ProfileController : Controller
{
    private readonly IHttpClientFactory _clients;
    private readonly FirebaseTokenStore _tokens;
    private readonly FirebaseAuthRestClient _firebase;

    public ProfileController(
        IHttpClientFactory clients,
        FirebaseTokenStore tokens,
        FirebaseAuthRestClient firebase)
    {
        _clients = clients;
        _tokens = tokens;
        _firebase = firebase;
    }

    private HttpRequestMessage ApiRequest(HttpMethod method, string path)
    {
        var session = User.FindFirst("CropCareSession")?.Value;
        var token = _tokens.GetIdToken(session);
        if (token is null) throw new UnauthorizedAccessException("Sign in again.");
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        try
        {
            using var req = ApiRequest(HttpMethod.Get, "api/account/me");
            using var res = await _clients.CreateClient("CropCareApi").SendAsync(req, ct);
            if (res.StatusCode == HttpStatusCode.Unauthorized)
                return RedirectToAction("Login", "Account");
            if (!res.IsSuccessStatusCode)
            {
                TempData["Error"] = "The profile could not be loaded.";
                return RedirectToAction("Index", "Dashboard");
            }
            var profile = await res.Content.ReadFromJsonAsync<CropCareProfile>(cancellationToken: ct);
            return View(profile);
        }
        catch (UnauthorizedAccessException) { return RedirectToAction("Login", "Account"); }
        catch (HttpRequestException) { TempData["Error"] = "Could not reach CropCare API."; }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        { TempData["Error"] = "The API request timed out."; }
        return RedirectToAction("Index", "Dashboard");
    }

    [HttpGet]
    public async Task<IActionResult> Edit(CancellationToken ct)
    {
        try
        {
            using var req = ApiRequest(HttpMethod.Get, "api/account/me");
            using var res = await _clients.CreateClient("CropCareApi").SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return RedirectToAction(nameof(Index));
            var profile = await res.Content.ReadFromJsonAsync<CropCareProfile>(cancellationToken: ct);
            if (profile is null) return RedirectToAction(nameof(Index));
            return View(new EditProfileViewModel
            {
                FirstName = profile.FirstName ?? "",
                MiddleName = profile.MiddleName ?? "",
                LastName = profile.LastName ?? "",
                Address = profile.Address ?? "",
                PhoneNumber = profile.PhoneNumber ?? ""
            });
        }
        catch (UnauthorizedAccessException) { return RedirectToAction("Login", "Account"); }
        catch (HttpRequestException) { TempData["Error"] = "Could not reach CropCare API."; }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        { TempData["Error"] = "The API request timed out."; }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditProfileViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);
        try
        {
            using var req = ApiRequest(HttpMethod.Patch, "api/account/me");
            req.Content = JsonContent.Create(new
            {
                firstName = model.FirstName.Trim(),
                middleName = model.MiddleName.Trim(),
                lastName = model.LastName.Trim(),
                address = model.Address.Trim(),
                phoneNumber = model.PhoneNumber.Trim()
            });
            using var res = await _clients.CreateClient("CropCareApi").SendAsync(req, ct);
            if (res.StatusCode == HttpStatusCode.Unauthorized)
                return RedirectToAction("Login", "Account");
            if (!res.IsSuccessStatusCode)
            {
                ModelState.AddModelError("", "Unable to update profile. Check your field values.");
                return View(model);
            }
            TempData["Notice"] = "Profile updated successfully.";
            return RedirectToAction(nameof(Index));
        }
        catch (UnauthorizedAccessException) { return RedirectToAction("Login", "Account"); }
        catch (HttpRequestException) { ModelState.AddModelError("", "API connection failed."); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        { ModelState.AddModelError("", "Update timed out."); }
        return View(model);
    }

    [HttpGet]
    public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(
        ChangePasswordViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);
        try
        {
            var email = User.FindFirstValue(ClaimTypes.Email);
            var expectedUid = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(expectedUid))
                return RedirectToAction("Login", "Account");

            // Re-authenticate before changing the password.
            var freshLogin = await _firebase.SignInAsync(email, model.CurrentPassword, ct);
            if (freshLogin is null || freshLogin.LocalId != expectedUid)
            {
                ModelState.AddModelError("", "Current password is incorrect.");
                return View(model);
            }

            if (!await _firebase.ChangePasswordAsync(freshLogin.IdToken, model.NewPassword, ct))
            {
                ModelState.AddModelError("", "Firebase could not update the password.");
                return View(model);
            }

            _tokens.RemoveSession(User.FindFirst("CropCareSession")?.Value);
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            TempData["Notice"] = "Password changed. Please sign in with your new password.";
            return RedirectToAction("Login", "Account");
        }
        catch (HttpRequestException)
        {
            ModelState.AddModelError("", "Firebase is temporarily unavailable.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            ModelState.AddModelError("", "Password update timed out.");
        }
        return View(model);
    }
}
