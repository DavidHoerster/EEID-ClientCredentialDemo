namespace TokenValidationApi.Models;

// Any request DTO carrying an organization ID implements this, so
// ValidateOrganizationAudienceFilter can find it generically among an action's
// bound arguments without needing to know the concrete request type.
public interface IOrganizationScopedRequest
{
    string OrganizationId { get; }
}

// Dummy payload: Foo/Bar/Baz are accepted but never evaluated, per the request.
// OrganizationId is the one field that's actually used, by the audience filter.
public record DemoRequest : IOrganizationScopedRequest
{
    public string OrganizationId { get; set; } = string.Empty;

    public string? Foo { get; set; }
    public string? Bar { get; set; }
    public string? Baz { get; set; }
}
