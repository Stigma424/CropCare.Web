using System.Net.Http.Json;
using CropCare.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CropCare.Web.Controllers;

[AllowAnonymous]
public sealed class RecoveryController : Controller
{
    private readonly IHttpClientFactory _clients;
    private readonly ILogger<RecoveryController> _logger;

    public RecoveryController(
        IHttpClientFactory clients,
        ILogger<RecoveryController> logger)
    {
        _clients = clients;
        _logger = logger;
    }

    [HttpGet, ActionName("Request")]
    public IActionResult RequestCode() => View(new RequestRecoveryCodeViewModel());

    [HttpPost, ValidateAntiForgeryToken, ActionName("Request")]
    public async Task<IActionResult> RequestCode(
        RequestRecoveryCodeViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);

        try
        {
            var client = _clients.CreateClient("CropCareApi");
            using var response = await client.PostAsJsonAsync(
                "api/recovery/request", new { email = model.Email.Trim() }, ct);

            if (!response.IsSuccessStatusCode)
            {
                ModelState.AddModelError("", "Recovery requests are temporarily unavailable. Try again later.");
                return View(model);
            }

            // TempData uses ASP.NET Core's protected cookie provider by default.
            TempData.Remove("RecoveryTicket");
            TempData["RecoveryEmail"] = model.Email.Trim();
            TempData["RecoveryMessage"] =
                "If the account exists, a six-digit code has been sent. Check your email and spam folder.";
            return RedirectToAction(nameof(Verify));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Recovery request API unavailable.");
            ModelState.AddModelError("", "Cannot connect to the CropCare API.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            ModelState.AddModelError("", "The request timed out.");
        }
        return View(model);
    }

    [HttpGet]
    public IActionResult Verify()
    {
        if (TempData.Peek("RecoveryEmail") is not string)
            return RedirectToAction("Request");
        return View(new VerifyRecoveryCodeViewModel());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Verify(
        VerifyRecoveryCodeViewModel model, CancellationToken ct)
    {
        if (TempData.Peek("RecoveryEmail") is not string email)
            return RedirectToAction("Request");
        if (!ModelState.IsValid) return View(model);

        try
        {
            var client = _clients.CreateClient("CropCareApi");
            using var response = await client.PostAsJsonAsync(
                "api/recovery/verify",
                new { email, code = model.Code.Trim() }, ct);

            if (!response.IsSuccessStatusCode)
            {
                ModelState.AddModelError("", "Invalid or expired code. Check the code or request a new one.");
                return View(model);
            }

            var body = await response.Content.ReadFromJsonAsync<RecoveryVerificationResponse>(
                cancellationToken: ct);
            if (string.IsNullOrWhiteSpace(body?.ResetTicket))
            {
                ModelState.AddModelError("", "Recovery could not be verified. Try again.");
                return View(model);
            }

            TempData["RecoveryTicket"] = body.ResetTicket;
            return RedirectToAction(nameof(Reset));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Recovery verification API unavailable.");
            ModelState.AddModelError("", "Cannot connect to the CropCare API.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            ModelState.AddModelError("", "Verification timed out.");
        }
        return View(model);
    }

    [HttpGet]
    public IActionResult Reset()
    {
        if (TempData.Peek("RecoveryEmail") is not string ||
            TempData.Peek("RecoveryTicket") is not string)
            return RedirectToAction("Request");
        return View(new NewRecoveryPasswordViewModel());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reset(
        NewRecoveryPasswordViewModel model, CancellationToken ct)
    {
        if (TempData.Peek("RecoveryEmail") is not string email ||
            TempData.Peek("RecoveryTicket") is not string resetTicket)
            return RedirectToAction("Request");
        if (!ModelState.IsValid) return View(model);

        try
        {
            var client = _clients.CreateClient("CropCareApi");
            using var response = await client.PostAsJsonAsync(
                "api/recovery/reset", new
                {
                    email,
                    resetTicket,
                    newPassword = model.NewPassword
                }, ct);

            if (!response.IsSuccessStatusCode)
            {
                // Reset authorizations are one-time; a failure may require a new code.
                TempData.Remove("RecoveryTicket");
                TempData.Remove("RecoveryEmail");
                TempData["RecoveryError"] =
                    "Password reset could not be completed. Request a new code and try again.";
                return RedirectToAction("Request");
            }

            TempData.Remove("RecoveryTicket");
            TempData.Remove("RecoveryEmail");
            return RedirectToAction(nameof(Complete));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Recovery reset API unavailable.");
            ModelState.AddModelError("", "Cannot connect to the CropCare API.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            ModelState.AddModelError("", "The request timed out. Check whether the password changed before retrying.");
        }
        return View(model);
    }

    [HttpGet]
    public IActionResult Complete() => View();
}
