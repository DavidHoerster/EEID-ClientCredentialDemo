using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
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
[Authorize]
public class ValidateTokenController : Controller
{
    // ConfigurationManager<T> is meant to be a long-lived, reused instance per
    // metadata address: it caches the discovery document + JWKS and refreshes
    // them automatically (every 24h by default, with retry-on-failure
    // backoff). Caching by address avoids re-fetching on every form submit.
    private static readonly ConcurrentDictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> ConfigManagers = new();

    private static readonly ConcurrentDictionary<string, string> OrganizationIds = new()
    {
        ["wilco"] = "6f666e44-e449-45ee-ab58-e0f13ffcc785",
        ["acme"] = "755417f5-2e37-4f2a-be4b-6ba27f9ee875",
        ["beta"] = "5f00c22d-1c28-42f7-a8e0-f45734735ad9"
    };

    [HttpGet]
    public IActionResult Index()
    {
        var model = new ValidateTokenWithOrgViewModel
        {
            MetadataAddress = "https://agiledave.ciamlogin.com/7ab7bda6-6617-430d-8193-c57e1d996f83/v2.0/.well-known/openid-configuration"
        };
        return View(model);
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(ValidateTokenWithOrgViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var expectedAudience = OrganizationIds.GetValueOrDefault(model.OrganizationId);
        if (string.IsNullOrEmpty(expectedAudience))
        {
            model.ErrorMessage = "Invalid organization ID.";
            return View(model);
        }

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
            // Show unvalidated claims anyway so the page is still useful while you fix the metadata URL.
            model.Claims = [.. unvalidatedToken.Claims.Select(c => new TokenClaim(c.Type, c.Value))];
            model.TagsClaimValue = model.Claims.FirstOrDefault(c => c.Type == "tags")?.Value;
            return View(model);
        }

        var unvalidatedAudience = unvalidatedToken.Audiences.FirstOrDefault();

        //validate that the org audience and the token audience match
        if (!string.IsNullOrEmpty(expectedAudience) && unvalidatedAudience != expectedAudience)
        {
            model.ErrorMessage = "Audience mismatch.";
            return View(model);
        }

        // ---- Signature (+ issuer, + optional audience) check. Lifetime excluded here on purpose. ----
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = oidcConfig.SigningKeys,
            ValidateIssuer = true,
            ValidIssuer = oidcConfig.Issuer,
            ValidateAudience = !string.IsNullOrWhiteSpace(unvalidatedAudience),
            ValidAudience = unvalidatedAudience,
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

        // Fall back to unvalidated claims for display if signature validation failed,
        // so you can still see what the token *claims* — clearly marked as unverified in the view.
        if (model.Claims.Count == 0)
        {
            model.Claims = [.. unvalidatedToken.Claims.Select(c => new TokenClaim(c.Type, c.Value))];
        }

        model.TagsClaimValue = model.Claims.FirstOrDefault(c => c.Type == "tags")?.Value;
        model.IsOverallValid = model.SignatureValid == true && model.LifetimeValid == true;

        return View(model);
    }
}