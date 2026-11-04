using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
namespace CropCare.Web.Services;

public static class ApiResponseReader
{
    public static async Task<string> ErrorAsync(HttpResponseMessage response, string fallback, CancellationToken ct)
    {
        try
        {
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (json.RootElement.ValueKind != JsonValueKind.Object) return fallback;
            foreach (var field in new[] { "error", "detail", "title" })
                if (json.RootElement.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String)
                    return value.GetString() ?? fallback;
        }
        catch (JsonException) { }
        return fallback;
    }
}

public sealed class ApiPageExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not JsonException and not NotSupportedException and not HttpRequestException and not TimeoutException) return;
        context.ExceptionHandled = true;
        context.HttpContext.Response.StatusCode = 503;
        context.Result = new ViewResult { ViewName = "~/Views/Shared/Unavailable.cshtml", StatusCode = 503 };
    }
}

public static class WebSessionPolicy
{
    public static TimeSpan Lifetime(int expiresInSeconds) => TimeSpan.FromSeconds(Math.Max(0, Math.Min(3000, expiresInSeconds - 60)));
}
