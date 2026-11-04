using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
namespace CropCare.Web.Services;
public sealed class ApiSessionHandler(IHttpContextAccessor accessor, FirebaseTokenStore tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
    {
        var response=await base.SendAsync(request,ct);
        var context=accessor.HttpContext;
        if(response.StatusCode==HttpStatusCode.Unauthorized && context?.User.Identity?.IsAuthenticated==true)
        {
            tokens.RemoveSession(context.User.FindFirst("CropCareSession")?.Value);
            lock(context.Items) context.Items["CropCareApiExpired"]=true;
        }
        return response;
    }
}
public sealed class ApiSessionResultFilter : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context,ResultExecutionDelegate next)
    {
        if(context.HttpContext.Items.ContainsKey("CropCareApiExpired"))
        {
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            context.Result=context.HttpContext.Request.Headers["X-CropCare-Partial"]=="true"
                ? new StatusCodeResult(401) : new RedirectToActionResult("Login","Account",null);
        }
        await next();
    }
}
