using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TokenValidationApi.Filters;
using TokenValidationApi.Models;

namespace TokenValidationApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FSDemoController : ControllerBase
{
    // TEMPORARY diagnostic — remove once the role-claim issue is confirmed fixed.
    // Requires only a valid, authenticated token (no role, no org check) and dumps
    // exactly what claims [Authorize] sees post-processing, so you can confirm
    // whether "roles" claims are present under that exact Type after the fix below.
    [HttpGet("whoami")]
    [Authorize]
    public IActionResult WhoAmI()
    {
        return Ok(User.Claims.Select(c => new { c.Type, c.Value }));
    }

    // Requires: valid, unexpired, correctly-signed token (via [Authorize] + the
    // the "FinScan.Billing" role, and an audience matching
    // the organizationId in the body.
    [HttpPost("billing")]
    [Authorize(Policy = "FinScanBilling")]
    [ValidateOrganizationAudience]
    public IActionResult Billing([FromBody] DemoRequest request)
    {
        return Ok(new
        {
            message = "Billing API call succeeded.",
            requiredRole = "FinScan.Billing",
            organizationId = request.OrganizationId
        });
    }

    // Same shape as Billing, but requires "FinScan.Payments" instead — note that
    // both [Authorize(Policy = "...")] and [ValidateOrganizationAudience] are just
    // reused attributes; neither endpoint has any bespoke authorization code.
    [HttpPost("payments")]
    [Authorize(Policy = "FinScanPayments")]
    [ValidateOrganizationAudience]
    public IActionResult Payments([FromBody] DemoRequest request)
    {
        return Ok(new
        {
            message = "Payments API call succeeded.",
            requiredRole = "FinScan.Payments",
            organizationId = request.OrganizationId
        });
    }

    // Same shape as Billing, but requires "FinScan.Admin.Users" instead — note that
    // both [Authorize(Policy = "...")] and [ValidateOrganizationAudience] are just
    // reused attributes; neither endpoint has any bespoke authorization code.
    [HttpPost("user-admin")]
    [Authorize(Policy = "FinScanUserAdmin")]
    [ValidateOrganizationAudience]
    public IActionResult UserAdmin([FromBody] DemoRequest request)
    {
        return Ok(new
        {
            message = "User Admin API call succeeded.",
            requiredRole = "FinScan.Admin.Users",
            organizationId = request.OrganizationId
        });
    }
}
