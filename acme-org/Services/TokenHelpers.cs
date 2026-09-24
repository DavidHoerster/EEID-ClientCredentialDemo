using acme_org.Models;
using System.Security.Cryptography.X509Certificates;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace acme_org.Services;

public static class TokenHelpers
{
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5); // matches TokenValidationParameters default

    public static (T model, JwtSecurityToken unvalidatedToken) ValidateTokenFromModel<T>(JwtSecurityTokenHandler handler, T model) where T : TokenValidationBase
    {
        var token = model.AccessToken.Trim();
        JwtSecurityToken unvalidatedToken = null;

        if (!handler.CanReadToken(token))
        {
            model.ErrorMessage = "The value entered is not a readable JWT (expected a compact, three-part dot-separated token).";
            return (model, unvalidatedToken);
        }

        unvalidatedToken = handler.ReadJwtToken(token);

        // ---- Lifetime check, deliberately independent of the signature check ----
        var now = DateTime.UtcNow;
        var notBefore = unvalidatedToken.ValidFrom;
        var expires = unvalidatedToken.ValidTo;

        model.NotBeforeUtc = notBefore == DateTime.MinValue ? (DateTime?)null : notBefore;
        model.ExpiresAtUtc = expires == DateTime.MinValue ? (DateTime?)null : expires;

        var isExpired = expires != DateTime.MinValue && expires.Add(ClockSkew) < now;
        var isNotYetValid = notBefore != DateTime.MinValue && notBefore.Subtract(ClockSkew) > now;

        model.LifetimeValid = !isExpired && !isNotYetValid;
        model.LifetimeError = isExpired
            ? $"Token expired at {expires:u} (current time {now:u})."
            : isNotYetValid
                ? $"Token is not valid until {notBefore:u} (current time {now:u})."
                : null;

        return (model, unvalidatedToken);
    }

    public static X509Certificate2? LoadCertificate(ClientCredentialsRequestViewModel model)
    {
        if (model.CertificatePfxFile is { Length: > 0 })
        {
            using var ms = new MemoryStream();
            model.CertificatePfxFile.CopyTo(ms);

            // EphemeralKeySet keeps the private key in memory only, for the lifetime
            // of this request — nothing gets written to disk or the user's profile.
            return X509CertificateLoader.LoadPkcs12(
                ms.ToArray(),
                model.CertificatePfxPassword,
                X509KeyStorageFlags.EphemeralKeySet);
        }

        if (string.IsNullOrWhiteSpace(model.CertificateThumbprint))
        {
            return null;
        }

        var storeLocation = string.Equals(model.CertificateStoreLocation, "LocalMachine", StringComparison.OrdinalIgnoreCase)
            ? StoreLocation.LocalMachine
            : StoreLocation.CurrentUser;

        using var store = new X509Store(StoreName.My, storeLocation);
        store.Open(OpenFlags.ReadOnly);

        // validOnly: false is required — a self-signed certificate has no chain to a
        // trusted root, so searching with validOnly: true would silently return
        // nothing even though the certificate is sitting right there in the store.
        var matches = store.Certificates.Find(X509FindType.FindByThumbprint, model.CertificateThumbprint.Trim(), validOnly: false);
        return matches.Count > 0 ? matches[0] : null;
    }

    public static string BuildClientAssertion(string clientId, string tokenEndpoint, X509Certificate2 certificate)
    {
        var now = DateTime.UtcNow;

        // X509SigningCredentials automatically sets the JWT header's "x5t" to the
        // base64url-encoded certificate hash — no manual thumbprint conversion needed.
        var signingCredentials = new X509SigningCredentials(certificate, SecurityAlgorithms.RsaSha256);

        var jwt = new JwtSecurityToken(
            issuer: clientId,
            audience: tokenEndpoint,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, clientId),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            },
            notBefore: now,
            expires: now.AddMinutes(5),
            signingCredentials: signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    public static List<TokenClaim> DecodeClaims(string? accessToken)
    {
        var claims = new List<TokenClaim>();
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return claims;
        }

        try
        {
            var handler = new JwtSecurityTokenHandler();

            if (!handler.CanReadToken(accessToken))
            {
                claims.Add(new TokenClaim("(info)", "Token is not a readable JWT (may be an opaque/encrypted access token for this resource)."));
                return claims;
            }

            var jwt = handler.ReadJwtToken(accessToken);
            foreach (var claim in jwt.Claims)
            {
                claims.Add(new TokenClaim(claim.Type, claim.Value));
            }
        }
        catch (Exception ex)
        {
            claims.Add(new TokenClaim("(error)", $"Failed to decode token: {ex.Message}"));
        }

        return claims;
    }
}