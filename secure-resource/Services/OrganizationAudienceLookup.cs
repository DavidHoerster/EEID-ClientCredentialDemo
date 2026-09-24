using System.Collections.Concurrent;

namespace TokenValidationApi.Services;

public interface IOrganizationAudienceLookup
{
    bool TryGetAudience(string organizationId, out string? audience);
}

// Placeholder in-memory implementation: a ConcurrentDictionary for now, meant to
// be swapped for a database-backed IOrganizationAudienceLookup later without
// changing anything else — the filter and controllers only ever depend on the
// interface, never on this class directly.
public class InMemoryOrganizationAudienceLookup : IOrganizationAudienceLookup
{
    private static readonly ConcurrentDictionary<string, string> Mapping = new(StringComparer.OrdinalIgnoreCase)
    {
        // Sample entries — replace with real organization IDs and the audience
        // (App ID URI or client ID) of the app registration that acts as that
        // organization's resource. Each distinct audience here implies a distinct
        // app registration in Entra representing that org's resource API.
        ["wilco"] = "6f666e44-e449-45ee-ab58-e0f13ffcc785",
        ["acme"] = "755417f5-2e37-4f2a-be4b-6ba27f9ee875",
        ["beta"] = "5f00c22d-1c28-42f7-a8e0-f45734735ad9"
    };

    public bool TryGetAudience(string organizationId, out string? audience) =>
        Mapping.TryGetValue(organizationId, out audience);
}
