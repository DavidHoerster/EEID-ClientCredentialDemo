# Sample Client Credentials Flow for Generating and Validating a Token

## Projects

- `acme-org`: This is the token generation project. The `TokenController.cs` file has the logic to generate the access token from Entra External ID (EEID) based upon the values provided. The user can create the token either via a client secret or a certificate (more secure).
  - The *client secret* approach requires a client secret to be created on the app registration and shared with the customer. The secret is passed in like a password and isn't considered as secure as...
  - The *certificate* approach requires ISI to issue and send a certificate to the customer. The customer installs that cert in Azure Key Vault or some secure location. When they mint a token, they need to retrieve that token (in the demo, it's just a self-signed cert coming from the local cert store) and create a certificate assertion to pass to EEID in order to mint the token. There is logic in the `TokenHelpers.cs` file in the acme_org project to show how to create that assertion.
  - acme_org also has a `ValidateTokenController.cs` that shows how to accept an access token and validate it against an EEID instance. This is done automatically by the `secure-resource` project, which is done with Authorization middleware.
  - acme_org also has a `CallSecureApiController.cs` which allows the user to paste in the access token minted from `TokenController.cs` and specify which org the token should be validated against. The org is a text value (e.g. "wilco", "acme", etc.) that maps to an audience (`client_id`) id in EEID. This way, NBC can send in their org name as part of the payload calling the API without having to pass in a `client_id`GUID. The API is called and based upon the roles in the access token (which can vary from app to app registration), the api call is validated and authorized. A success or failure message is returned.
- `secure-resource`: THis is the client API project that is called by the customer. This would be the "Wrapper 2.0" API endpoint. The `FSDemoController.cs` file exposes some endpoints to simulate a customer calling FinScan billing, payments, and user admin APIs. Each API has an attribute specifying the role required to access the endpoint along with an attribute specifying the organization validation auth filter to use.
    - The `ValidateOrganizationAudienceAttribute.cs` filter simulates how the `aud` claim in the access token is checked against a static list of org names. The org names correspond to `client_id`s (audiences), so a customer should only be able to use the app registration for their org. This filter uses the OrganizationAudienceLookup.cs class to check org name against audience in the token to ensure the appropriate org is issuing the call. 
    - The `OrganizationAudienceLookup.cs` class just has a static ConcurrentDictionary property that holds a few hard-coded org names and client ids. This can be replaced with a database call instead of having the values being hard-coded.

## Entra External ID

Entra External ID (EEID) is used to house the app registrations and app roles (optional). The access token is minted by EEID.

A typical call to create an access token from EEID looks like this:

```http
POST https://{tenant-subdomain}.ciamlogin.com/{tenant-id}/oauth2/v2.0/token
Content-Type: application/x-www-form-urlencoded

grant_type=client_credentials
client_id=xxx-yyy-guid
client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer
client_assertion=<base64_encoded_cert>
scope=api://<client-id>/.default
```

The result of the call should have the access token in a `access_token` field in the response. This is a base64 encoded value - a JWT.

### App Role Assignments

If you want to have `roles` show up in the access token from a client credentials flow call,  you need to make two adjustments to the app registration's Manifest. You need to allow mapped claims and also set the access token version to 2. To do this, follow these steps:

1. Go to the Entra Admin portal (entra.microsoft.com) and go to App Registrations
2. Select the app registration that you want app role assignments to be returned
3. Under Manage, select Manifest
4. In the Manifest JSON, find the `api` section. Set `acceptMappedClaims` to `true` and set `requestedAccessTokenVersion` to `2`. Both of these values do not need double quotes (they aren't strings). It is possible that these values have already been set appropriately. If they are not set, your `roles` claim will not return when you request an access token.

#### Create App Role Assignments

To create app role assignments and have them return on the access token, you can do this for each app registration. NOTE: you cannot create a single app registration and have those roles map to other app registrations for client credentials flow. You need to set app roles on each app registration.

1. Go to the Entra Admin portal (entra.microsoft.com) and go to App Registrations
2. Select the app registration that you want to set app roles for
3. Under Manage, select App Roles
4. Select `+ Create app role` and create as many app roles as you'd like. Make sure to set the `Allowed member types` to be `Applications` as this will be used by the service principal.
5. After you've created your app roles, you need to assign them to the app registration. Go to Manage -> API Permissions
6. Select `+ Add a permission`.
7. In the flyout, select `APIs my organization uses` and then find your app registration. Select it.
8. The app roles you just created should show up in the list. Select the app roles you want to assign to the app. Click `Add permissions`
9. Make sure you `Grant admin consent for <tenant>` on the app roles you just assigned, otherwise they won't be present in the access token. Once you've done that, your app role assignments should return as `roles` claims in your access token.