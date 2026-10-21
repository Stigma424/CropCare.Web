
using System.Net.Http.Json;

namespace CropCare.Web.Services;

public sealed class FirebaseAuthRestClient
{
    private readonly HttpClient _client;
    private readonly IConfiguration _config;

    public FirebaseAuthRestClient(
        HttpClient client,
        IConfiguration config)
    {
        _client = client;
        _config = config;
    }

    public async Task<FirebaseSignInResult?> SignInAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        // Firebase Web API Key from User Secrets
        var apiKey = _config["Firebase:WebApiKey"];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Firebase Web API Key is not configured.");
        }

        var url =
            $"v1/accounts:signInWithPassword?key={Uri.EscapeDataString(apiKey)}";

        // Authenticate with Firebase Email/Password
        using var response = await _client.PostAsJsonAsync(
            url,
            new
            {
                email,
                password,
                returnSecureToken = true
            },
            cancellationToken);

        // Do not reveal whether an email exists
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        // Get the Firebase ID token and user ID
        return await response.Content
            .ReadFromJsonAsync<FirebaseSignInResult>(
                cancellationToken: cancellationToken);
    }
}

public sealed class FirebaseSignInResult
{
    public string IdToken { get; set; } = "";
    public string LocalId { get; set; } = "";
    public string Email { get; set; } = "";
}
