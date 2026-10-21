
using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;

namespace CropCare.Web.Services;

public sealed class FirebaseTokenStore
{
    private readonly IMemoryCache _cache;

    private static readonly TimeSpan TokenLifetime =
        TimeSpan.FromMinutes(55);

    public FirebaseTokenStore(IMemoryCache cache)
    {
        _cache = cache;
    }

    // Create a random web session ID
    public string CreateSession(string idToken)
    {
        var sessionId = WebEncoders.Base64UrlEncode(
            RandomNumberGenerator.GetBytes(32));

        _cache.Set(
            GetCacheKey(sessionId),
            idToken,
            TokenLifetime);

        return sessionId;
    }

    // Retrieve the Firebase ID token for API requests
    public string? GetIdToken(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        return _cache.TryGetValue(
            GetCacheKey(sessionId),
            out string? token)
                ? token
                : null;
    }

    // Delete the session during logout
    public void RemoveSession(string? sessionId)
    {
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            _cache.Remove(GetCacheKey(sessionId));
        }
    }

    private static string GetCacheKey(string sessionId)
    {
        return $"cropcare:auth:{sessionId}";
    }
}
