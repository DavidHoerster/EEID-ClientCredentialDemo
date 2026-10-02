using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Graph;
using Microsoft.Graph.Applications.Item.AddPassword;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;


namespace acme_org.Services;

public class NewAppRegistrationResult
{
    public string DisplayName { get; init; } = string.Empty;
    public string ApplicationObjectId { get; init; } = string.Empty;
    public string ClientId { get; init; } = string.Empty; // the appId
    public string AppIdUri { get; init; } = string.Empty; // e.g. api://{clientId}
    public string ServicePrincipalObjectId { get; init; } = string.Empty;
    public string ClientSecretValue { get; init; } = string.Empty;
    public DateTimeOffset ClientSecretExpiresUtc { get; init; }
}

// Thin wrapper over the Microsoft Graph .NET SDK (GraphServiceClient), using
// the admin service principal's credentials — never whatever token a user
// pasted into the other pages. See Program.cs for how GraphServiceClient is
// registered (ClientSecretCredential from Azure.Identity).
public interface IGraphAdminService
{
    /// <summary>
    /// Creates a new app registration, sets its App ID URI (api://{clientId}),
    /// creates the corresponding service principal, and adds a client secret.
    ///
    /// Each step after the initial create is retried on transient
    /// "resource not found" errors — see the big comment on
    /// ExecuteWithRetryOnReplicationDelayAsync for why that's necessary.
    /// </summary>
    Task<NewAppRegistrationResult> CreateApplicationWithSecretAsync(
        string displayName,
        string secretDescription,
        int secretExpiresInMonths,
        CancellationToken cancellationToken = default);
}

public class GraphAdminService : IGraphAdminService
{
    private readonly GraphServiceClient _graphClient;

    public GraphAdminService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    public async Task<NewAppRegistrationResult> CreateApplicationWithSecretAsync(
        string displayName,
        string secretDescription,
        int secretExpiresInMonths,
        CancellationToken cancellationToken = default)
    {
        var endDateTime = DateTimeOffset.UtcNow.AddMonths(secretExpiresInMonths);

        // Step 1: create the application object itself. No retry needed here —
        // there's nothing to retry against yet.
        var application = new Application
        {
            DisplayName = displayName,
            SignInAudience = "AzureADMyOrg",
            PasswordCredentials =
            [
                new() {
                    DisplayName = secretDescription,
                    EndDateTime = endDateTime
                }
            ]
        };

        var createdApplication = await _graphClient.Applications
            .PostAsync(application, cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Graph returned no application object from the create call.");

        var applicationObjectId = createdApplication.Id
            ?? throw new InvalidOperationException("Created application has no object ID.");
        var clientId = createdApplication.AppId
            ?? throw new InvalidOperationException("Created application has no appId.");

        var newPassword = createdApplication.PasswordCredentials.FirstOrDefault()
            ?? throw new InvalidOperationException("Created application has no password credential.");

        var appIdUri = $"api://{clientId}";

        // Steps 2–4 all act on (or reference) the application/service-principal
        // object that was *just* created. Microsoft Graph is backed by a
        // globally-replicated directory, and a write that lands on one replica
        // is not guaranteed to be visible yet to the replica that serves your
        // very next request a few milliseconds later. In practice this shows
        // up as an intermittent 404 "Request_ResourceNotFound" on the call
        // immediately following a create — not a real error, just the new
        // object not having propagated yet. This is the exact failure you ran
        // into previously. Tooling like Terraform's azuread provider works
        // around it the same way: retry the dependent call with a short
        // backoff instead of treating the first 404 as fatal.

        // Step 2: set the App ID URI on the application.
        await ExecuteWithRetryOnReplicationDelayAsync(
            () => _graphClient.Applications[applicationObjectId].PatchAsync(
                new Application { IdentifierUris = new List<string> { appIdUri } },
                cancellationToken: cancellationToken),
            cancellationToken);

        // Step 3: create the service principal. Creating the application
        // object does NOT create one automatically — this is a separate call,
        // and the one most likely to hit the replication-delay 404 since it's
        // a brand new object referencing the application by appId.
        var createdServicePrincipal = await ExecuteWithRetryOnReplicationDelayAsync(
            () => _graphClient.ServicePrincipals.PostAsync(
                new ServicePrincipal { AppId = clientId },
                cancellationToken: cancellationToken),
            cancellationToken)
            ?? throw new InvalidOperationException("Graph returned no service principal object from the create call.");

        // Step 4: add the client secret. Graph generates and returns the
        // plaintext secretText only here, and only this once — there's no way
        // to set it directly on the application-create call, and no way to
        // retrieve it again after this response.
        // var endDateTime = DateTimeOffset.UtcNow.AddMonths(secretExpiresInMonths);

        // commented this out as I'm combining this step into step 1....need to test more to validate
        //  there aren't issues with eventual consistency in the tenant


        // var addPasswordRequestBody = new AddPasswordPostRequestBody
        // {
        //     PasswordCredential = new PasswordCredential
        //     {
        //         DisplayName = secretDescription,
        //         EndDateTime = endDateTime
        //     }
        // };

        // var newPassword = await ExecuteWithRetryOnReplicationDelayAsync(
        //     () => _graphClient.Applications[applicationObjectId].AddPassword
        //         .PostAsync(addPasswordRequestBody, cancellationToken: cancellationToken),
        //     cancellationToken)
        //     ?? throw new InvalidOperationException("Graph returned no password credential from the addPassword call.");

        return new NewAppRegistrationResult
        {
            DisplayName = createdApplication.DisplayName ?? displayName,
            ApplicationObjectId = applicationObjectId,
            ClientId = clientId,
            AppIdUri = appIdUri,
            ServicePrincipalObjectId = createdServicePrincipal.Id ?? string.Empty,
            ClientSecretValue = newPassword.SecretText ?? string.Empty,
            ClientSecretExpiresUtc = newPassword.EndDateTime ?? endDateTime
        };
    }

    /// <summary>
    /// Retries <paramref name="action"/> when Graph reports the object it
    /// refers to doesn't exist yet (HTTP 404, error code
    /// "Request_ResourceNotFound"), which — for a call made immediately after
    /// creating that same object — almost always means directory replication
    /// hasn't caught up rather than that the object is genuinely missing.
    /// Any other error (bad request, permission denied, etc.) is rethrown
    /// immediately with no retry, since waiting won't fix those.
    /// </summary>
    private static async Task<T> ExecuteWithRetryOnReplicationDelayAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken,
        int maxAttempts = 5)
    {
        var delay = TimeSpan.FromSeconds(1);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await action();
            }
            catch (ODataError ex) when (
                attempt < maxAttempts &&
                (ex.ResponseStatusCode == 404 || string.Equals(ex.Error?.Code, "Request_ResourceNotFound", StringComparison.OrdinalIgnoreCase)))
            {
                await Task.Delay(delay, cancellationToken);
                delay *= 2; // 1s, 2s, 4s, 8s — about 15s of total waiting across 5 attempts
            }
        }

        // Should be unreachable (the last attempt either returns or throws
        // out of the catch above), but keeps the compiler happy.
        return await action();
    }
}