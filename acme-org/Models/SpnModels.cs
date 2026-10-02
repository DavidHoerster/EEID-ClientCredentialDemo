using System.ComponentModel.DataAnnotations;

namespace acme_org.Models;

public class CreateServicePrincipalViewModel
{
    [Required]
    [Display(Name = "Display name")]
    public string DisplayName { get; set; } = string.Empty;

    [Display(Name = "Client secret description")]
    public string SecretDescription { get; set; } = "Created via admin tool";

    [Range(1, 24)]
    [Display(Name = "Secret expires in (months)")]
    public int SecretExpiresInMonths { get; set; } = 12;


    // ---- Results (populated after a successful or partial creation) ----
    public bool Created { get; set; }
    public string? NewDisplayName { get; set; }
    public string? NewApplicationObjectId { get; set; }
    public string? NewClientId { get; set; }
    public string? NewServicePrincipalObjectId { get; set; }

    // Shown exactly once — Graph never returns this value again after this response.
    public string? ClientSecretValue { get; set; }
    public DateTime? ClientSecretExpiresUtc { get; set; }

    public string? ErrorMessage { get; set; }
}