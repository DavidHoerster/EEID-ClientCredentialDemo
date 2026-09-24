using System.ComponentModel.DataAnnotations;

namespace acme_org.Models;

public abstract class TokenValidationBase
{
    [Required]
    [Display(Name = "Access token")]
    public string AccessToken { get; set; } = string.Empty;

    [Required]
    [Display(Name = "OpenID Connect metadata (discovery) URL")]
    public string MetadataAddress { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Organization ID")]
    public string OrganizationId { get; set; } = string.Empty;

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