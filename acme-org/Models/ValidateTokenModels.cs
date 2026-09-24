using System.ComponentModel.DataAnnotations;

namespace acme_org.Models;

// Reuses the TokenClaim record already defined in ClientCredentialsModels.cs
// (same namespace) — don't redefine it here.
public class ValidateTokenViewModel
{
    [Required]
    [Display(Name = "Access token")]
    public string AccessToken { get; set; } = string.Empty;

    [Required]
    [Display(Name = "OpenID Connect metadata (discovery) URL")]
    public string MetadataAddress { get; set; } = string.Empty;

    [Display(Name = "Expected audience (optional)")]
    public string? ExpectedAudience { get; set; }

    // ---- Results (populated after validation; not entered by the user) ----
    public bool? SignatureValid { get; set; }
    public string? SignatureError { get; set; }

    public bool? LifetimeValid { get; set; }
    public string? LifetimeError { get; set; }

    public bool AudienceChecked { get; set; }
    public bool IsOverallValid { get; set; }

    public DateTime? NotBeforeUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }

    public List<TokenClaim> Claims { get; set; } = new();
    public string? TagsClaimValue { get; set; }

    public string? ErrorMessage { get; set; }
}