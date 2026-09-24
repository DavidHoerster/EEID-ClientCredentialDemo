using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace acme_org.Models;

// Rename the namespace above to match your project.
public enum ClientCredentialType
{
    Secret,
    Certificate,
    PrebuiltAssertion
}

public class ClientCredentialsRequestViewModel
{
    [Required]
    [Display(Name = "Token endpoint")]
    public string TokenEndpoint { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Client ID (Application ID)")]
    public string ClientId { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Scope")]
    public string Scope { get; set; } = string.Empty;

    [Display(Name = "Grant type")]
    public string GrantType { get; set; } = "client_credentials";

    [Display(Name = "Credential type")]
    public ClientCredentialType CredentialType { get; set; } = ClientCredentialType.Secret;

    // ---- Secret ----
    [Display(Name = "Client secret")]
    [DataType(DataType.Password)]
    public string? ClientSecret { get; set; }

    // ---- Certificate (assertion built & signed automatically by the controller) ----
    [Display(Name = "Certificate store")]
    public string CertificateStoreLocation { get; set; } = "CurrentUser";

    [Display(Name = "Certificate thumbprint")]
    public string? CertificateThumbprint { get; set; }

    [Display(Name = "...or upload a .pfx instead")]
    public IFormFile? CertificatePfxFile { get; set; }

    [Display(Name = "PFX password")]
    [DataType(DataType.Password)]
    public string? CertificatePfxPassword { get; set; }

    // ---- Pre-built assertion (advanced / manual override — e.g. Key Vault-signed) ----
    [Display(Name = "Client assertion type")]
    public string? ClientAssertionType { get; set; }

    [Display(Name = "Client assertion (JWT)")]
    public string? ClientAssertion { get; set; }

    // ---- Populated after a call is made; not entered by the user. ----
    public string? GeneratedClientAssertion { get; set; }
    public string? AccessToken { get; set; }
    public string? TokenType { get; set; }
    public int? ExpiresIn { get; set; }
    public string? RawTokenResponse { get; set; }
    public List<TokenClaim> Claims { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

public record TokenClaim(string Type, string Value);