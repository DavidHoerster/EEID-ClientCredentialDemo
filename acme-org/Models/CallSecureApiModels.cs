using System.ComponentModel.DataAnnotations;

namespace acme_org.Models;

// Reuses the TokenClaim record already defined in ClientCredentialsModels.cs
// (same namespace) — don't redefine it here.
public class CallSecureApiViewModel : TokenValidationBase
{
    // These properties are now inherited from TokenValidationBase, so they can be removed.

    [Display(Name = "Expected audience (optional)")]
    public string? ExpectedAudience { get; set; }

    // "endpoint-a" (requires User.Read) or "endpoint-b" (requires Directory.Write)
    // on the separate TokenValidationApi project.
    [Display(Name = "Endpoint to call")]
    public string ApiEndpoint { get; set; } = "endpoint-a";

    // All result properties are now inherited from TokenValidationBase, so they can be removed.

    // ---- Resource API call results (populated only by "Validate & call resource API") ----
    public bool ApiCallAttempted { get; set; }
    public int? ApiCallStatusCode { get; set; }
    public string? ApiCallResultRaw { get; set; }
    public string? ApiCallErrorMessage { get; set; }
}