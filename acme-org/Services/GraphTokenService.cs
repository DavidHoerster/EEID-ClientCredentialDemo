using System.Text.Json;

namespace acme_org.Services;

// Acquires and caches an access token for Microsoft Graph, using the admin
// tool's own service principal (client_credentials) — configured via the
// "GraphAdmin" section of appsettings.json / user-secrets, NOT entered by
// whoever is using the page. Separate and distinct from the token-generator
// page's credentials, which represent whichever service principal the user
// is testing with.
public interface IGraphTokenService
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

public class GraphTokenService : IGraphTokenService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    // Simple process-wide cache: one admin identity, one token, shared across
    // requests. Refreshed a couple of minutes before actual expiry to avoid
    // races against in-flight requests.
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);
    private static string? _cachedToken;
    private static DateTimeOffset _cachedTokenExpiresUtc = DateTimeOffset.MinValue;

    public GraphTokenService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _cachedTokenExpiresUtc.AddMinutes(-2))
        {
            return _cachedToken;
        }

        await RefreshLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _cachedTokenExpiresUtc.AddMinutes(-2))
            {
                return _cachedToken;
            }

            var tenantId = _configuration["GraphAdmin:TenantId"];
            var clientId = _configuration["GraphAdmin:ClientId"];
            var clientSecret = _configuration["GraphAdmin:ClientSecret"];

            // Deliberately login.microsoftonline.com, not ciamlogin.com: this token is
            // for managing the External ID tenant's OWN directory via Microsoft Graph
            // (an admin/daemon scenario), which is a different authority than the
            // ciamlogin.com domain used elsewhere in this app for customer-facing
            // token requests against your own APIs. If Graph calls fail with an
            // issuer/authority error, this is the first thing to double-check.
            var tokenEndpoint = _configuration["GraphAdmin:TokenEndpoint"]
                ?? $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";

            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = clientId ?? string.Empty,
                ["client_secret"] = clientSecret ?? string.Empty,
                ["scope"] = "https://graph.microsoft.com/.default"
            };

            var client = _httpClientFactory.CreateClient("TokenClient");
            using var response = await client.PostAsync(tokenEndpoint, new FormUrlEncodedContent(form), cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Failed to acquire a Graph token for the admin service principal ({(int)response.StatusCode}): {body}");
            }

            using var doc = JsonDocument.Parse(body);
            var accessToken = doc.RootElement.GetProperty("access_token").GetString()!;
            var expiresIn = doc.RootElement.GetProperty("expires_in").GetInt32();

            _cachedToken = accessToken;
            _cachedTokenExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(expiresIn);

            return accessToken;
        }
        finally
        {
            RefreshLock.Release();
        }
    }
}