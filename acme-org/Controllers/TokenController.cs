using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using acme_org.Models;
using acme_org.Services;
using Microsoft.AspNetCore.Authorization;

namespace acme_org.Controllers;

// SECURITY NOTE: this controller accepts and displays client secrets, certificate
// private key material (via uploaded .pfx), and raw access tokens. It is intended
// for local development / internal debugging only. Do not deploy it to an
// environment reachable by untrusted users, do not log the request body or the
// generated assertion, and consider putting it behind authentication (e.g.
// [Authorize]) even in dev/test environments.
[Authorize]
public class TokenController : Controller
{
    private readonly IHttpClientFactory _httpClientFactory;

    public TokenController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    public IActionResult Index()
    {
        var model = new ClientCredentialsRequestViewModel
        {
            TokenEndpoint = "https://agiledave.ciamlogin.com/7ab7bda6-6617-430d-8193-c57e1d996f83/oauth2/v2.0/token",
            Scope = "api://{app-id}/.default",
            GrantType = "client_credentials"
        };
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(ClientCredentialsRequestViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        string? clientSecret = null;
        string? clientAssertion = null;
        var clientAssertionType = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";

        switch (model.CredentialType)
        {
            case ClientCredentialType.Secret:
                clientSecret = model.ClientSecret;
                break;

            case ClientCredentialType.PrebuiltAssertion:
                clientAssertion = model.ClientAssertion;
                clientAssertionType = string.IsNullOrWhiteSpace(model.ClientAssertionType)
                    ? clientAssertionType
                    : model.ClientAssertionType;
                break;

            case ClientCredentialType.Certificate:
                X509Certificate2? cert;
                try
                {
                    cert = TokenHelpers.LoadCertificate(model);
                }
                catch (Exception ex)
                {
                    model.ErrorMessage = $"Could not load the certificate: {ex.Message}";
                    return View(model);
                }

                if (cert is null)
                {
                    model.ErrorMessage = string.IsNullOrWhiteSpace(model.CertificateThumbprint)
                        ? "No .pfx file was uploaded and no thumbprint was entered."
                        : $"No certificate with thumbprint '{model.CertificateThumbprint}' was found in the {model.CertificateStoreLocation} store.";
                    return View(model);
                }

                if (!cert.HasPrivateKey)
                {
                    model.ErrorMessage = "The selected certificate does not have an accessible private key, so it can't be used to sign a client assertion.";
                    return View(model);
                }

                try
                {
                    clientAssertion = TokenHelpers.BuildClientAssertion(model.ClientId, model.TokenEndpoint, cert);
                }
                catch (Exception ex)
                {
                    model.ErrorMessage = $"Failed to build/sign the client assertion: {ex.Message}";
                    return View(model);
                }

                // Shown on the page for learning/debugging purposes — set before the
                // token call so it's still visible even if that call subsequently fails.
                model.GeneratedClientAssertion = clientAssertion;
                break;
        }

        var client = _httpClientFactory.CreateClient("TokenClient");

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = string.IsNullOrWhiteSpace(model.GrantType) ? "client_credentials" : model.GrantType,
            ["client_id"] = model.ClientId,
            ["scope"] = model.Scope
        };

        if (!string.IsNullOrWhiteSpace(clientAssertion))
        {
            form["client_assertion_type"] = clientAssertionType;
            form["client_assertion"] = clientAssertion;
        }
        else
        {
            form["client_secret"] = clientSecret ?? string.Empty;
        }

        using var content = new FormUrlEncodedContent(form);

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsync(model.TokenEndpoint, content);
        }
        catch (HttpRequestException ex)
        {
            model.ErrorMessage = $"Request to token endpoint failed: {ex.Message}";
            return View(model);
        }

        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            model.ErrorMessage = $"Token endpoint returned {(int)response.StatusCode} {response.ReasonPhrase}:\n{responseBody}";
            model.RawTokenResponse = responseBody;
            return View(model);
        }

        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        model.RawTokenResponse = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        model.AccessToken = root.TryGetProperty("access_token", out var at) ? at.GetString() : null;
        model.TokenType = root.TryGetProperty("token_type", out var tt) ? tt.GetString() : null;
        model.ExpiresIn = root.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : (int?)null;

        model.Claims = TokenHelpers.DecodeClaims(model.AccessToken);

        return View(model);
    }
}