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