using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using acme_org.Models;
using acme_org.Services; // rename to match your project

namespace acme_org.Controllers; // rename to match your project

// SECURITY NOTE: this controller accepts a raw access token pasted by the
// user for debugging purposes. Same caveats as TokenController: dev/test
// only, don't log the token value, consider [Authorize] even in non-prod.
//
// IMPORTANT: the authority used to fetch signing keys (MetadataAddress) is
// supplied by the caller of this page, NOT derived from the token's own
// "iss" claim. Trusting an issuer the token itself claims would let a
// forged or foreign-tenant token "validate" successfully against its own
// legitimate (but wrong) keys.
public class CallSecureApiController : Controller
{
    private static readonly ConcurrentDictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> ConfigManagers = new();
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public CallSecureApiController(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    [HttpGet]
    public IActionResult Index()
    {
        var model = new CallSecureApiViewModel
        {
            MetadataAddress = "https://agiledave.ciamlogin.com/7ab7bda6-6617-430d-8193-c57e1d996f83/v2.0/.well-known/openid-configuration"
        };
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(CallSecureApiViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        await PerformLocalValidationAsync(model);
        return View(model);
    }

    // Runs the same local validation as Index, PLUS calls the simulated
    // resource API (ResourceApiController) using the pasted token and the
    // entered organization ID. The two checks are deliberately independent —
    // this page's local validation isn't wired into the API's own validation —
    // so you can compare what each one concludes.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CallApi(CallSecureApiViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View("Index", model);
        }

        await PerformLocalValidationAsync(model);
        await CallResourceApiAsync(model);

        return View("Index", model);
    }

    private async Task PerformLocalValidationAsync(CallSecureApiViewModel model)
    {
        var handler = new JwtSecurityTokenHandler();
        (model, var unvalidatedToken) = TokenHelpers.ValidateTokenFromModel(handler, model);

        // ---- Fetch signing keys / issuer metadata from the CALLER-SUPPLIED authority ----
        var configManager = ConfigManagers.GetOrAdd(model.MetadataAddress, addr =>
            new ConfigurationManager<OpenIdConnectConfiguration>(addr, new OpenIdConnectConfigurationRetriever()));

        OpenIdConnectConfiguration oidcConfig;
        try
        {
            oidcConfig = await configManager.GetConfigurationAsync(HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            model.ErrorMessage = $"Could not retrieve signing keys from '{model.MetadataAddress}': {ex.Message}";
            model.Claims = unvalidatedToken.Claims.Select(c => new TokenClaim(c.Type, c.Value)).ToList();
            model.TagsClaimValue = model.Claims.FirstOrDefault(c => c.Type == "tags")?.Value;
            return;
        }

        // ---- Signature (+ issuer, + optional audience) check. Lifetime excluded here on purpose. ----
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = oidcConfig.SigningKeys,
            ValidateIssuer = true,
            ValidIssuer = oidcConfig.Issuer,
            ValidateAudience = !string.IsNullOrWhiteSpace(model.ExpectedAudience),
            ValidAudience = model.ExpectedAudience,
            ValidateLifetime = false
        };

        model.AudienceChecked = validationParameters.ValidateAudience;

        try
        {
            handler.ValidateToken(model.AccessToken.Trim(), validationParameters, out var validatedToken);
            model.SignatureValid = true;
            model.Claims = [.. ((JwtSecurityToken)validatedToken).Claims.Select(c => new TokenClaim(c.Type, c.Value))];
        }
        catch (SecurityTokenInvalidSignatureException)
        {
            model.SignatureValid = false;
            model.SignatureError = "Signature does not match any current signing key for this issuer. The token may have been altered, was issued by a different tenant/authority than the metadata URL points to, or the signing keys have rolled over since it was issued.";
        }
        catch (SecurityTokenInvalidIssuerException ex)
        {
            model.SignatureValid = false;
            model.SignatureError = $"Issuer check failed: {ex.Message}";
        }
        catch (SecurityTokenInvalidAudienceException ex)
        {
            model.SignatureValid = false;
            model.SignatureError = $"Audience check failed: {ex.Message}";
        }
        catch (Exception ex)
        {
            model.SignatureValid = false;
            model.SignatureError = $"Validation failed: {ex.Message}";
        }

        if (model.Claims.Count == 0)
        {
            model.Claims = unvalidatedToken.Claims.Select(c => new TokenClaim(c.Type, c.Value)).ToList();
        }

        model.TagsClaimValue = model.Claims.FirstOrDefault(c => c.Type == "tags")?.Value;
        model.IsOverallValid = model.SignatureValid == true && model.LifetimeValid == true;
    }

    private async Task CallResourceApiAsync(CallSecureApiViewModel model)
    {
        model.ApiCallAttempted = true;

        if (string.IsNullOrWhiteSpace(model.OrganizationId))
        {
            model.ApiCallErrorMessage = "Enter an organization ID to call the resource API.";
            return;
        }

        // Base URL of the separate TokenValidationApi project — configure this in
        // appsettings.json once you know its actual local port. Falls back to the
        // dotnet-generated default dev port if not configured.
        var baseUrl = _configuration["TokenValidationApi:BaseUrl"] ?? "https://localhost:7152";

        var apiUri = new Uri(new Uri(baseUrl), $"/api/fsdemo/{model.ApiEndpoint}");

        // Foo/Bar/Baz are dummy payload fields the API never evaluates — hardcoded
        // here rather than exposed as inputs, since their values genuinely don't matter.
        var payload = JsonSerializer.Serialize(new
        {
            organizationId = model.OrganizationId,
            foo = "1",
            bar = "2",
            baz = "3"
        });

        var client = _httpClientFactory.CreateClient("TokenClient");

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, apiUri)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", model.AccessToken.Trim());

        try
        {
            var response = await client.SendAsync(requestMessage);
            var body = await response.Content.ReadAsStringAsync();

            model.ApiCallStatusCode = (int)response.StatusCode;

            try
            {
                using var doc = JsonDocument.Parse(body);
                model.ApiCallResultRaw = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (JsonException)
            {
                model.ApiCallResultRaw = body;
            }
        }
        catch (Exception ex)
        {
            model.ApiCallErrorMessage = $"Call to the resource API failed: {ex.Message}";
        }
    }
}