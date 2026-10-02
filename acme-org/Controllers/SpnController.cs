using Microsoft.AspNetCore.Mvc;
using acme_org.Models;
using acme_org.Services;

namespace acme_org.Controllers;

// SECURITY NOTE: this page creates real app registrations, service principals,
// client secrets, and app role assignments in your Entra External ID tenant,
// using a highly privileged admin service principal (Application.ReadWrite.All +
// AppRoleAssignment.ReadWrite.All). It is a dev/admin tool, not something to
// expose broadly — consider [Authorize] even in dev/test environments, and
// never log the client secret value returned to the page.
public class SpnController : Controller
{
    private readonly IGraphAdminService _graphAdminService;

    public SpnController(IGraphAdminService graphAdminService)
    {
        _graphAdminService = graphAdminService;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View(new CreateServicePrincipalViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(CreateServicePrincipalViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            // TODO: combine the app registration creation and password assignment into one operation

            // 1. Create the app registration (Application object).
            var endDateTime = DateTimeOffset.UtcNow.AddMonths(model.SecretExpiresInMonths);
            var application = await _graphAdminService.CreateApplicationWithSecretAsync(model.DisplayName, model.SecretDescription, model.SecretExpiresInMonths);

            var applicationObjectId = application.ApplicationObjectId;
            var clientId = application.ClientId;
            var password = application.ClientSecretValue;

            // // 2. Create the corresponding service principal — creating the
            // // application alone does not create one.
            // var servicePrincipal = await _graphAdminService.CreateServicePrincipalAsync(clientId);
            // var servicePrincipalObjectId = servicePrincipal.GetProperty("id").GetString()!;

            // // 3. Add a client secret. Graph returns the plaintext value exactly
            // // once, in this response — it cannot be retrieved again afterward.
            // var endDateTime = DateTimeOffset.UtcNow.AddMonths(model.SecretExpiresInMonths);
            // var password = await _graphAdminService.AddPasswordAsync(applicationObjectId, model.SecretDescription, endDateTime);

            model.Created = true;
            model.NewDisplayName = model.DisplayName;
            model.NewApplicationObjectId = applicationObjectId;
            model.NewClientId = clientId;
            model.NewServicePrincipalObjectId = application.ServicePrincipalObjectId;
            model.ClientSecretValue = password;
            model.ClientSecretExpiresUtc = application.ClientSecretExpiresUtc.UtcDateTime;
        }
        catch (Exception ex)
        {
            // The service principal/secret/roles created before the failure (if any)
            // already exist in the tenant even though this request reports an error —
            // check the partial results below rather than assuming nothing happened.
            model.ErrorMessage = $"Service principal creation failed partway through: {ex.Message}";
        }

        return View(model);
    }

    // private async Task AssignRolesAsync(CreateServicePrincipalViewModel model, string newServicePrincipalObjectId)
    // {
    //     var roleNames = model.RoleNames!
    //         .Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    //         .Distinct(StringComparer.OrdinalIgnoreCase)
    //         .ToList();

    //     var resourceObjectId = model.ResourceServicePrincipalObjectId!.Trim();
    //     var resourceServicePrincipal = await _graphAdminService.GetServicePrincipalWithAppRolesAsync(resourceObjectId);

    //     if (resourceServicePrincipal is null)
    //     {
    //         foreach (var roleName in roleNames)
    //         {
    //             model.RoleAssignmentResults.Add(new RoleAssignmentResult(roleName, false, $"Resource service principal '{resourceObjectId}' was not found."));
    //         }
    //         return;
    //     }

    //     var appRoles = resourceServicePrincipal.Value.GetProperty("appRoles");

    //     foreach (var roleName in roleNames)
    //     {
    //         string? matchedRoleId = null;
    //         foreach (var role in appRoles.EnumerateArray())
    //         {
    //             if (string.Equals(role.GetProperty("value").GetString(), roleName, StringComparison.OrdinalIgnoreCase))
    //             {
    //                 matchedRoleId = role.GetProperty("id").GetString();
    //                 break;
    //             }
    //         }

    //         if (matchedRoleId is null)
    //         {
    //             model.RoleAssignmentResults.Add(new RoleAssignmentResult(roleName, false, "No app role with this value exists on the resource service principal."));
    //             continue;
    //         }

    //         try
    //         {
    //             await _graphAdminService.AssignAppRoleAsync(resourceObjectId, newServicePrincipalObjectId, matchedRoleId);
    //             model.RoleAssignmentResults.Add(new RoleAssignmentResult(roleName, true, null));
    //         }
    //         catch (Exception ex)
    //         {
    //             model.RoleAssignmentResults.Add(new RoleAssignmentResult(roleName, false, ex.Message));
    //         }
    //     }
    // }
}