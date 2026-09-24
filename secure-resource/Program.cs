using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using TokenValidationApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// ---- Authentication: validates signature, issuer, and expiration against the
// Entra External ID tenant configured in the "AzureAd" section of appsettings.json.
// Audience is deliberately NOT validated here — see the comment below.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

// PostConfigure, not Configure: .NET's options pattern runs ALL Configure
// delegates first, then ALL PostConfigure delegates, regardless of registration
// order between the two groups. AddMicrosoftIdentityWebApi finalizes its own
// TokenValidationParameters internally — if it does that via PostConfigure, a
// plain Configure<JwtBearerOptions> call here would run BEFORE it and get
// silently overwritten (this is exactly what caused role checks to fail while
// everything else worked: RoleClaimType got reset back to the default
// ClaimTypes.Role, which never matches Entra's "roles" claim). PostConfigure
// guarantees ours runs last, since it's registered after AddMicrosoftIdentityWebApi.
builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
{
    // Confirmed via a diagnostic endpoint dumping User.Claims: the default inbound
    // claim type mapping already renames the token's "roles" claim to the long
    // ClaimTypes.Role URI (http://schemas.microsoft.com/ws/2008/06/identity/claims/role)
    // before this code ever runs. So RoleClaimType should point at THAT — which is
    // also .NET's default, meaning this line is here to document the behavior
    // explicitly rather than to change it. (An earlier version of this file set
    // RoleClaimType = "roles" on the theory that claims arrive unmapped — the
    // diagnostic proved that wrong: they don't.)
    options.TokenValidationParameters.RoleClaimType = ClaimTypes.Role;

    // The expected audience varies per request (it depends on the organizationId in
    // the body, looked up via IOrganizationAudienceLookup), so there's no single
    // fixed value to check here. ValidateOrganizationAudienceAttribute performs this
    // check dynamically, after model binding, instead — see Filters/.
    options.TokenValidationParameters.ValidateAudience = false;
});

// ---- Authorization: one reusable named policy per required role. Apply with
// [Authorize(Policy = "...")] on any endpoint that needs it — that's the whole
// point of defining these centrally instead of checking roles inline.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("FinScanBilling", policy => policy.RequireRole("FinScan.Billing"));
    options.AddPolicy("FinScanPayments", policy => policy.RequireRole("FinScan.Payments"));
    options.AddPolicy("FinScanUserAdmin", policy => policy.RequireRole("FinScan.Admin.Users"));
});

// ---- Org -> audience lookup. ConcurrentDictionary-backed for now; swap the
// implementation for a database-backed one later without touching the filter or
// the controllers that depend on IOrganizationAudienceLookup.
builder.Services.AddSingleton<IOrganizationAudienceLookup, InMemoryOrganizationAudienceLookup>();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
