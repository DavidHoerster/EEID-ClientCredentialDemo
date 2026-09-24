using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TokenValidationApi.Models;
using TokenValidationApi.Services;

namespace TokenValidationApi.Filters;

// Usage: [ValidateOrganizationAudience] on any action whose request body implements
// IOrganizationScopedRequest.
//
// This is an ACTION filter, not an authorization policy, and that's deliberate:
// the expected audience depends on the organizationId in the request body, and
// authorization policies evaluate before model binding — they can't see the body
// yet. Action filters run after binding, so context.ActionArguments already has
// the bound request available here.
//
// By the time this filter runs, [Authorize] has already confirmed the token is
// authenticated (valid signature/issuer/expiration) and satisfies its role
// policy, so this filter only has to check the audience.
public sealed class ValidateOrganizationAudienceAttribute : TypeFilterAttribute
{
    public ValidateOrganizationAudienceAttribute() : base(typeof(ValidateOrganizationAudienceFilter))
    {
    }
}

public class ValidateOrganizationAudienceFilter(IOrganizationAudienceLookup lookup) : IAsyncActionFilter
{
    private readonly IOrganizationAudienceLookup _lookup = lookup;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var scopedRequest = context.ActionArguments.Values.OfType<IOrganizationScopedRequest>().FirstOrDefault();

        if (scopedRequest is null || string.IsNullOrWhiteSpace(scopedRequest.OrganizationId))
        {
            context.Result = new BadRequestObjectResult(new { error = "organizationId is required in the request body." });
            return;
        }

        // JwtBearerHandler exposes registered claims like "aud" as plain claims on
        // the ClaimsPrincipal, so this works even with ValidateAudience disabled.
        var tokenAudience = context.HttpContext.User.FindFirst("aud")?.Value;
        if (string.IsNullOrWhiteSpace(tokenAudience))
        {
            context.Result = new ObjectResult(new { error = "Token does not contain an 'aud' claim." })
            {
                StatusCode = StatusCodes.Status401Unauthorized
            };
            return;
        }

        if (!_lookup.TryGetAudience(scopedRequest.OrganizationId, out var expectedAudience) || string.IsNullOrWhiteSpace(expectedAudience))
        {
            context.Result = new BadRequestObjectResult(new { error = $"Unknown organization ID '{scopedRequest.OrganizationId}'." });
            return;
        }

        if (!string.Equals(tokenAudience, expectedAudience, StringComparison.OrdinalIgnoreCase))
        {
            context.Result = new ObjectResult(new
            {
                error = $"Token audience does not match the audience registered for organization '{scopedRequest.OrganizationId}'.",
                expectedAudience,
                tokenAudience
            })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        await next();
    }
}
