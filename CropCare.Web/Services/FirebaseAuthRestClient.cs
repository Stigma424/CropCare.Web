using System.Net.Http.Json;
using System.Text.Json;

namespace CropCare.Web.Services;

public sealed class FirebaseAuthRestClient
{
    private readonly HttpClient _client;
    private readonly IConfiguration _config;

    public FirebaseAuthRestClient(HttpClient client, IConfiguration config)
    {
        _client = client;
        _config = config;
    }

    private string Endpoint(string action)
    {
        var key = _config["Firebase:WebApiKey"];
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Firebase Web API Key is missing from User Secrets.");
        return $"v1/accounts:{action}?key={Uri.EscapeDataString(key)}";
    }

    public async Task<FirebaseSignInResult?> SignInAsync(
        string email, string password, CancellationToken ct)
    {
        using var response = await _client.PostAsJsonAsync(
            Endpoint("signInWithPassword"),
            new { email, password, returnSecureToken = true }, ct);

        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<FirebaseSignInResult>(cancellationToken: ct)
            : null;
    }

    public async Task<FirebaseSignInResult?> SignUpAsync(
        string email, string password, CancellationToken ct)
    {
        using var response = await _client.PostAsJsonAsync(
            Endpoint("signUp"),
            new { email, password, returnSecureToken = true }, ct);

        if (response.IsSuccessStatusCode)
            return await response.Content.ReadFromJsonAsync<FirebaseSignInResult>(cancellationToken: ct);

        // If profile creation failed on an earlier attempt, an existing
        // account owner can sign in and retry completing their profile.
        var error = await ReadFirebaseErrorAsync(response, ct);
        if (error == "EMAIL_EXISTS")
            return await SignInAsync(email, password, ct);

        return null;
    }

    public async Task<FirebaseSignInResult?> ExchangeCustomTokenAsync(string customToken, CancellationToken ct)
    {
        using var response = await _client.PostAsJsonAsync(Endpoint("signInWithCustomToken"),
            new { token = customToken, returnSecureToken = true }, ct);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<FirebaseSignInResult>(cancellationToken: ct) : null;
    }
    public async Task<bool> ChangePasswordAsync(
        string freshIdToken, string newPassword, CancellationToken ct)
    {
        using var response = await _client.PostAsJsonAsync(
            Endpoint("update"),
            new { idToken = freshIdToken, password = newPassword, returnSecureToken = true },
            ct);
        return response.IsSuccessStatusCode;
    }

    private static async Task<string?> ReadFirebaseErrorAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var json = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return json.RootElement.GetProperty("error").GetProperty("message").GetString();
        }
        catch (JsonException) { return null; }
        catch (KeyNotFoundException) { return null; }
    }
}

public sealed class FirebaseSignInResult
{
    public string IdToken { get; set; } = "";
    public string LocalId { get; set; } = "";
    public string Email { get; set; } = "";
    public string ExpiresIn { get; set; } = "0";
    public int ExpiresInSeconds => int.TryParse(ExpiresIn, out var seconds) ? seconds : 0;
}

public sealed class UsernameLoginGrant { public string CustomToken { get; set; } = string.Empty; public string UserId { get; set; } = string.Empty; }
