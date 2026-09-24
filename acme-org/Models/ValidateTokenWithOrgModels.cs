using System.ComponentModel.DataAnnotations;

namespace acme_org.Models;

// Reuses the TokenClaim record already defined in ClientCredentialsModels.cs
// (same namespace) — don't redefine it here.
public class ValidateTokenWithOrgViewModel : TokenValidationBase
{
}